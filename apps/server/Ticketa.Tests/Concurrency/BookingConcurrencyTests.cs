using System.Collections.Concurrent;
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
    [CollectionDefinition("MsSqlCollection")]
    public class MsSqlCollection : ICollectionFixture<MsSqlDatabaseFixture>
    {
    }

    [Collection("MsSqlCollection")]
    public class BookingConcurrencyTests
    {
        private readonly MsSqlDatabaseFixture _fixture;
        private readonly ITestOutputHelper _output;

        public BookingConcurrencyTests(MsSqlDatabaseFixture fixture, ITestOutputHelper output)
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
        public async Task Concurrency_50UsersBookingSameSingleSeat_ResultsIn1WinnerAnd49Conflicts()
        {
            // 1. Arrange: Setup Service Provider and Seed Database
            const int concurrentUsersCount = 50;
            var targetSeat = new SeatDto { Row = 1, SeatNumber = 1 };

            var serviceProvider = BuildServiceProvider(_fixture.ConnectionString);

            int showtimeId;
            var userIds = new List<string>();

            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var hall = new Hall
                {
                    Name = $"Concurrency-Hall-{Guid.NewGuid():N}",
                    Type = HallType.Standard,
                    TotalRows = 12,
                    SeatsPerRow = 16
                };
                db.Halls.Add(hall);

                var movie = new Movie
                {
                    Title = $"Concurrency-Movie-{Guid.NewGuid():N}",
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
                    Price = 150m,
                    Status = ShowtimeStatus.Scheduled
                };
                db.Showtimes.Add(showtime);

                for (int i = 1; i <= concurrentUsersCount; i++)
                {
                    var user = new AppUser
                    {
                        Id = $"user-concurrent-{Guid.NewGuid():N}",
                        UserName = $"concurrent_user_{i}_{Guid.NewGuid():N}@test.com",
                        Email = $"concurrent_user_{i}_{Guid.NewGuid():N}@test.com",
                        FirstName = "User",
                        LastName = $"{i}"
                    };
                    db.Users.Add(user);
                    userIds.Add(user.Id);
                }

                await db.SaveChangesAsync();
                showtimeId = showtime.Id;
            }

            // 2. Act: Non-blocking asynchronous barrier release
            var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readyCounter = 0;
            var allReadySignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var results = new ConcurrentBag<(string UserId, int UserIndex, BookingResultDto Result)>();

            var bookingTasks = userIds.Select((userId, index) => Task.Run(async () =>
            {
                using var scope = serviceProvider.CreateScope();
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();

                var dto = new BookingCreateDto
                {
                    ShowtimeId = showtimeId,
                    Seats = new List<SeatDto> { targetSeat }
                };

                // Signal readiness
                if (Interlocked.Increment(ref readyCounter) == concurrentUsersCount)
                {
                    allReadySignal.SetResult();
                }

                // Await asynchronous release trigger (zero thread blocking)
                await startSignal.Task;

                var result = await bookingService.CreateAsync(dto, userId);
                results.Add((userId, index + 1, result));
            })).ToArray();

            // Wait until all 50 tasks have allocated their scope and reached the starting line
            await allReadySignal.Task;

            // Trigger all 50 requests simultaneously
            startSignal.SetResult();

            await Task.WhenAll(bookingTasks);

            // 3. Assert & Log: Exactly 1 Winner and 49 Losers
            var winners = results.Where(r => r.Result.Succeeded).ToList();
            var losers = results.Where(r => !r.Result.Succeeded).ToList();

            var winner = winners.FirstOrDefault();

            // Output detailed execution summary to console and IDE Test Explorer
            LogMessage("================================================================================");
            LogMessage("                         CONCURRENCY TEST SUMMARY                               ");
            LogMessage("================================================================================");
            LogMessage($"Total Concurrent Users Dispatched : {concurrentUsersCount}");
            LogMessage($"Target Contested Seat             : Row {targetSeat.Row}, Seat {targetSeat.SeatNumber}");
            LogMessage($"Total Successful Bookings (Winner): {winners.Count}");
            LogMessage($"Total Conflicted Bookings (Losers): {losers.Count}");
            LogMessage("--------------------------------------------------------------------------------");

            if (winner.Result != null)
            {
                LogMessage($"🏆 WINNER DETAILS:");
                LogMessage($"   - User Index      : #{winner.UserIndex}");
                LogMessage($"   - User ID         : {winner.UserId}");
                LogMessage($"   - Booking Ref     : {winner.Result.BookingReference}");
                LogMessage($"   - Total Amount    : {winner.Result.TotalAmount:C}");
            }

            LogMessage("--------------------------------------------------------------------------------");
            LogMessage($"❌ CONFLICTED USERS ({losers.Count} Total):");
            foreach (var loser in losers.OrderBy(l => l.UserIndex))
            {
                var conflictSeatStr = loser.Result.ConflictingSeats != null 
                    ? string.Join(", ", loser.Result.ConflictingSeats.Select(s => $"R{s.Row}S{s.SeatNumber}"))
                    : "None";
                LogMessage($"   - User #{loser.UserIndex} (ID: {loser.UserId}) -> Conflict on: [{conflictSeatStr}]");
            }
            LogMessage("================================================================================");

            Assert.Single(winners);
            Assert.Equal(49, losers.Count);

            Assert.NotNull(winner.Result);
            Assert.False(string.IsNullOrWhiteSpace(winner.Result.BookingReference));
            Assert.NotNull(winner.Result.TotalAmount);
            Assert.True(winner.Result.TotalAmount > 0);

            // All 49 losers must receive conflict status with the contested seat
            foreach (var loser in losers)
            {
                Assert.NotNull(loser.Result.ConflictingSeats);
                Assert.Contains(loser.Result.ConflictingSeats, s => s.Row == targetSeat.Row && s.SeatNumber == targetSeat.SeatNumber);
            }

            // 4. Assert: Database Integrity
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var totalBookedSeats = await db.BookedSeats.CountAsync(s => s.ShowtimeId == showtimeId && s.Row == targetSeat.Row && s.SeatNumber == targetSeat.SeatNumber);
                var totalBookings = await db.Bookings.CountAsync(b => b.ShowtimeId == showtimeId);
                var savedBooking = await db.Bookings.Include(b => b.BookedSeats).FirstOrDefaultAsync(b => b.ShowtimeId == showtimeId);

                Assert.Equal(1, totalBookedSeats);
                Assert.Equal(1, totalBookings);
                Assert.NotNull(savedBooking);
                Assert.Equal(winner.UserId, savedBooking.UserId);
                Assert.Equal(winner.Result.BookingReference, savedBooking.BookingRefrence);
                Assert.Single(savedBooking.BookedSeats);
                Assert.Equal(targetSeat.Row, savedBooking.BookedSeats.First().Row);
                Assert.Equal(targetSeat.SeatNumber, savedBooking.BookedSeats.First().SeatNumber);
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