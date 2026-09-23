using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;
using Ticketa.Core.Enums;
using Ticketa.Core.Helpers;
using Ticketa.Core.Interfaces;
using Ticketa.Core.Interfaces.IRepositories;
using Ticketa.Core.Interfaces.IServices;
using Ticketa.Infrastructure.Data;
using Ticketa.Infrastructure.Repositories;
using Ticketa.Infrastructure.Service;
using Xunit;
using Xunit.Abstractions;

namespace Ticketa.Tests.Concurrency
{
    [Collection("MsSqlCollection")]
    public class BookingTransactionAtomicityTests
    {
        private readonly MsSqlDatabaseFixture _fixture;
        private readonly ITestOutputHelper _output;

        public BookingTransactionAtomicityTests(MsSqlDatabaseFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        private void LogMessage(string message)
        {
            _output.WriteLine(message);
            Console.WriteLine(message);
        }

        [Fact]
        public async Task Booking_WithOneTakenSeatAndOneAvailableSeat_FailsAndDoesNotInsertAvailableSeat()
        {
            // 1. Arrange: Setup Service Provider and Seed DB
            var serviceProvider = BuildServiceProvider(_fixture.ConnectionString);
            int showtimeId;
            var userAId = $"user-a-{Guid.NewGuid():N}";
            var userBId = $"user-b-{Guid.NewGuid():N}";

            var contestedSeat = new SeatDto { Row = 2, SeatNumber = 1 };
            var availableSeat = new SeatDto { Row = 2, SeatNumber = 2 };

            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var hall = new Hall
                {
                    Name = $"Atomicity-Hall-{Guid.NewGuid():N}",
                    Type = HallType.Standard,
                    TotalRows = 12,
                    SeatsPerRow = 16
                };
                db.Halls.Add(hall);

                var movie = new Movie
                {
                    Title = $"Atomicity-Movie-{Guid.NewGuid():N}",
                    TmdbId = Random.Shared.Next(10000, 99999),
                    Status = MovieStatus.Active,
                    RuntimeMinutes = 120
                };
                db.Movies.Add(movie);
                await db.SaveChangesAsync();

                var showtime = new Showtime
                {
                    HallId = hall.Id,
                    MovieId = movie.Id,
                    StartTime = DateTime.UtcNow.AddDays(1),
                    EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
                    Price = 120m,
                    Status = ShowtimeStatus.Scheduled
                };
                db.Showtimes.Add(showtime);

                db.Users.AddRange(
                    new AppUser { Id = userAId, UserName = $"usera_{Guid.NewGuid():N}@test.com", Email = $"usera_{Guid.NewGuid():N}@test.com", FirstName = "User", LastName = "A" },
                    new AppUser { Id = userBId, UserName = $"userb_{Guid.NewGuid():N}@test.com", Email = $"userb_{Guid.NewGuid():N}@test.com", FirstName = "User", LastName = "B" }
                );

                await db.SaveChangesAsync();
                showtimeId = showtime.Id;
            }

            // 2. Act 1: User A successfully books the contested seat
            using (var scope = serviceProvider.CreateScope())
            {
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var resultA = await bookingService.CreateAsync(new BookingCreateDto
                {
                    ShowtimeId = showtimeId,
                    Seats = new List<SeatDto> { contestedSeat }
                }, userAId);

                Assert.True(resultA.Succeeded, "Initial booking by User A should succeed.");
                LogMessage($"[User A] Successfully booked Seat R{contestedSeat.Row}S{contestedSeat.SeatNumber} with Ref: {resultA.BookingReference}");
            }

            // 3. Act 2: User B attempts to book [contestedSeat, availableSeat] in a single transaction
            BookingResultDto resultB;
            using (var scope = serviceProvider.CreateScope())
            {
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                resultB = await bookingService.CreateAsync(new BookingCreateDto
                {
                    ShowtimeId = showtimeId,
                    Seats = new List<SeatDto> { contestedSeat, availableSeat }
                }, userBId);
            }

            LogMessage($"[User B] Booking attempt result: Succeeded={resultB.Succeeded}");

            // 4. Assert: Result checks for User B
            Assert.False(resultB.Succeeded, "User B booking must fail due to contested seat.");
            Assert.NotNull(resultB.ConflictingSeats);
            Assert.Contains(resultB.ConflictingSeats, s => s.Row == contestedSeat.Row && s.SeatNumber == contestedSeat.SeatNumber);

            // 5. Assert: Atomicity & Database Integrity
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // A. Only User A's booking exists
                var totalBookings = await db.Bookings.CountAsync(b => b.ShowtimeId == showtimeId);
                Assert.Equal(1, totalBookings);

                var userBBookingExists = await db.Bookings.AnyAsync(b => b.UserId == userBId);
                Assert.False(userBBookingExists, "User B should not have any persisted booking record.");

                // B. Only contestedSeat is booked; availableSeat was rolled back / not inserted
                var allBookedSeats = await db.BookedSeats
                    .Where(s => s.ShowtimeId == showtimeId)
                    .ToListAsync();

                Assert.Single(allBookedSeats);
                Assert.Equal(contestedSeat.Row, allBookedSeats[0].Row);
                Assert.Equal(contestedSeat.SeatNumber, allBookedSeats[0].SeatNumber);

                var availableSeatExists = await db.BookedSeats.AnyAsync(s =>
                    s.ShowtimeId == showtimeId && s.Row == availableSeat.Row && s.SeatNumber == availableSeat.SeatNumber);
                Assert.False(availableSeatExists, "The available seat must NOT be inserted into the database when the transaction fails.");

                LogMessage(" Atomicity verification passed: No partial seat or booking records saved.");
            }
        }

        [Fact]
        public async Task Booking_FailedMultiSeatBooking_PreservesShowtimeStatusAndDoesNotTriggerSoldOut()
        {
            // 1. Arrange: Setup Gold Hall (6 rows, 8 seats per row = 38 visible seats)
            var serviceProvider = BuildServiceProvider(_fixture.ConnectionString);
            int showtimeId;
            var initialUserId = $"user-initial-{Guid.NewGuid():N}";
            var candidateUserId = $"user-candidate-{Guid.NewGuid():N}";

            var template = HallTypeHelper.GetTemplate(HallType.Gold);
            var totalVisibleSeats = template.VisibleSeatCount; // 38 seats

            // Collect all valid visible seats in Gold hall (Rows 1..6, Seats 1..8 excluding skip seats)
            var allValidSeats = new List<SeatDto>();
            for (int r = 1; r <= template.Rows; r++)
            {
                int skip = (r == 1) ? 3 : (r == template.Rows ? 2 : 0);
                for (int s = 1 + skip; s <= template.SeatsPerRow - skip; s++)
                {
                    allValidSeats.Add(new SeatDto { Row = r, SeatNumber = s });
                }
            }

            Assert.Equal(totalVisibleSeats, allValidSeats.Count);

            // Let's pre-book totalVisibleSeats - 1 seats (leaving exactly 1 seat open)
            var seatsToPreBook = allValidSeats.Take(totalVisibleSeats - 1).ToList();
            var lastAvailableSeat = allValidSeats.Last();
            var alreadyBookedSeat = seatsToPreBook.First();

            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var hall = new Hall
                {
                    Name = $"SoldOut-Hall-{Guid.NewGuid():N}",
                    Type = HallType.Gold,
                    TotalRows = template.Rows,
                    SeatsPerRow = template.SeatsPerRow
                };
                db.Halls.Add(hall);

                var movie = new Movie
                {
                    Title = $"SoldOut-Movie-{Guid.NewGuid():N}",
                    TmdbId = Random.Shared.Next(10000, 99999),
                    Status = MovieStatus.Active,
                    RuntimeMinutes = 100
                };
                db.Movies.Add(movie);
                await db.SaveChangesAsync();

                var showtime = new Showtime
                {
                    HallId = hall.Id,
                    MovieId = movie.Id,
                    StartTime = DateTime.UtcNow.AddDays(2),
                    EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
                    Price = 200m,
                    Status = ShowtimeStatus.Scheduled
                };
                db.Showtimes.Add(showtime);

                db.Users.AddRange(
                    new AppUser { Id = initialUserId, UserName = $"init_{Guid.NewGuid():N}@test.com", Email = $"init_{Guid.NewGuid():N}@test.com", FirstName = "Init", LastName = "User" },
                    new AppUser { Id = candidateUserId, UserName = $"cand_{Guid.NewGuid():N}@test.com", Email = $"cand_{Guid.NewGuid():N}@test.com", FirstName = "Cand", LastName = "User" }
                );

                await db.SaveChangesAsync();
                showtimeId = showtime.Id;
            }

            // Pre-book 37 seats
            using (var scope = serviceProvider.CreateScope())
            {
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var preBookResult = await bookingService.CreateAsync(new BookingCreateDto
                {
                    ShowtimeId = showtimeId,
                    Seats = seatsToPreBook
                }, initialUserId);

                Assert.True(preBookResult.Succeeded, "Pre-booking initial batch of seats must succeed.");
            }

            // Verify showtime is still Scheduled (since 1 seat remains)
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var st = await db.Showtimes.FindAsync(showtimeId);
                Assert.NotNull(st);
                Assert.Equal(ShowtimeStatus.Scheduled, st.Status);
            }

            // 2. Act: Candidate tries to book [alreadyBookedSeat, lastAvailableSeat]
            // If this had succeeded, total booked would equal totalVisibleSeats and could trigger SoldOut
            BookingResultDto candidateResult;
            using (var scope = serviceProvider.CreateScope())
            {
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                candidateResult = await bookingService.CreateAsync(new BookingCreateDto
                {
                    ShowtimeId = showtimeId,
                    Seats = new List<SeatDto> { alreadyBookedSeat, lastAvailableSeat }
                }, candidateUserId);
            }

            // 3. Assert
            Assert.False(candidateResult.Succeeded, "Candidate booking must fail due to already booked seat.");

            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // A. Showtime Status must still be Scheduled
                var st = await db.Showtimes.FindAsync(showtimeId);
                Assert.NotNull(st);
                Assert.Equal(ShowtimeStatus.Scheduled, st.Status);

                // B. The last available seat must NOT have been booked
                var isLastSeatBooked = await db.BookedSeats.AnyAsync(s =>
                    s.ShowtimeId == showtimeId && s.Row == lastAvailableSeat.Row && s.SeatNumber == lastAvailableSeat.SeatNumber);
                Assert.False(isLastSeatBooked, "The last available seat must remain open.");

                // C. Total booked seats count remains 37
                var totalBooked = await db.BookedSeats.CountAsync(s => s.ShowtimeId == showtimeId);
                Assert.Equal(seatsToPreBook.Count, totalBooked);

                LogMessage(" Showtime status & capacity invariant verified successfully: status remains Scheduled.");
            }
        }

        private static IServiceProvider BuildServiceProvider(string connectionString)
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddConsole());

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));

            var inMemoryConfig = new Dictionary<string, string?>
            {
                { "AppTimeZone", "UTC" }
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemoryConfig)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<TimeConversions>();

            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IBookingRepository, BookingRepository>();
            services.AddScoped<IBookedSeatRepository, BookedSeatRepository>();
            services.AddScoped<IShowtimeRepository, ShowtimeRepository>();
            services.AddScoped<IBookingService, BookingService>();

            return services.BuildServiceProvider();
        }
    }
}