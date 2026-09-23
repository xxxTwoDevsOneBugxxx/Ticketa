using Microsoft.EntityFrameworkCore;
using Ticketa.Core.Entities;
using Ticketa.Core.Enums;
using Ticketa.Infrastructure.Data;
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

        private ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_fixture.ConnectionString)
                .Options;

            return new ApplicationDbContext(options);
        }

        private static string GenerateBookingReference() =>
            $"TKT-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        [Fact]
        public async Task MultiSeatBooking_WhenOneSeatViolatesDatabaseUniqueConstraint_RollsBackEntireOperationAtDatabaseLevel()
        {
            // =========================================================================
            // 1. Initial State / Setup: 1 Hall, 1 Movie, 1 Showtime, 2 Users
            // =========================================================================
            int showtimeId;
            var userAId = $"user-a-{Guid.NewGuid():N}";
            var userBId = $"user-b-{Guid.NewGuid():N}";

            const int rowA = 1;
            const int seatNumberA1 = 1; // A1
            const int seatNumberA2 = 2; // A2

            await using (var db = CreateDbContext())
            {
                var hall = new Hall
                {
                    Name = $"Atomicity-Hall-{Guid.NewGuid():N}",
                    Type = HallType.Standard,
                    TotalRows = 10,
                    SeatsPerRow = 10
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
                    Price = 100m,
                    Status = ShowtimeStatus.Scheduled
                };
                db.Showtimes.Add(showtime);

                db.Users.AddRange(
                    new AppUser
                    {
                        Id = userAId,
                        UserName = $"usera_{Guid.NewGuid():N}@test.com",
                        Email = $"usera_{Guid.NewGuid():N}@test.com",
                        FirstName = "User",
                        LastName = "A"
                    },
                    new AppUser
                    {
                        Id = userBId,
                        UserName = $"userb_{Guid.NewGuid():N}@test.com",
                        Email = $"userb_{Guid.NewGuid():N}@test.com",
                        FirstName = "User",
                        LastName = "B"
                    }
                );

                await db.SaveChangesAsync();
                showtimeId = showtime.Id;
            }

            // =========================================================================
            // 2. Step 1: Create an existing booking (User A books Seat A1)
            // =========================================================================
            var bookingRefA = GenerateBookingReference();
            await using (var db = CreateDbContext())
            {
                var bookingA = new Booking
                {
                    UserId = userAId,
                    ShowtimeId = showtimeId,
                    BookedAt = DateTime.UtcNow,
                    TotalAmount = 100m,
                    Status = BookingStatus.Confirmed,
                    BookingRefrence = bookingRefA,
                    BookedSeats = new List<BookedSeat>
                    {
                        new BookedSeat
                        {
                            ShowtimeId = showtimeId,
                            Row = rowA,
                            SeatNumber = seatNumberA1,
                            Category = SeatCategory.Regular,
                            Price = 100m
                        }
                    }
                };

                db.Bookings.Add(bookingA);
                await db.SaveChangesAsync();

                LogMessage($"[Step 1] User A successfully booked Seat R{rowA}S{seatNumberA1} (A1).");
            }

            // =========================================================================
            // 3. Step 2: Attempt an invalid multi-seat booking via fresh ApplicationDbContext
            //    Directly through DbContext (NOT BookingService) so the database unique constraint is tested!
            //    Changes include:
            //    - Booking B
            //      ├── BookedSeat A1 (duplicate -> violates unique index)
            //      └── BookedSeat A2 (valid/free)
            //    - Showtime state change (Status = SoldOut) in the same change set
            // =========================================================================
            var bookingRefB = GenerateBookingReference();

            await using (var db = CreateDbContext())
            {
                // Attach and modify showtime status to simulate capacity state update in same transaction
                var showtime = await db.Showtimes.FirstAsync(s => s.Id == showtimeId);
                showtime.Status = ShowtimeStatus.SoldOut;

                var bookingB = new Booking
                {
                    UserId = userBId,
                    ShowtimeId = showtimeId,
                    BookedAt = DateTime.UtcNow,
                    TotalAmount = 200m,
                    Status = BookingStatus.Confirmed,
                    BookingRefrence = bookingRefB,
                    BookedSeats = new List<BookedSeat>
                    {
                        new BookedSeat // A1: Duplicate (violates unique constraint)
                        {
                            ShowtimeId = showtimeId,
                            Row = rowA,
                            SeatNumber = seatNumberA1,
                            Category = SeatCategory.Regular,
                            Price = 100m
                        },
                        new BookedSeat // A2: Valid / available
                        {
                            ShowtimeId = showtimeId,
                            Row = rowA,
                            SeatNumber = seatNumberA2,
                            Category = SeatCategory.Regular,
                            Price = 100m
                        }
                    }
                };

                db.Bookings.Add(bookingB);

                LogMessage("[Step 2] Attempting db.SaveChangesAsync() with duplicate A1 + available A2 + Showtime status change...");

                // Assert that the database unique constraint throws DbUpdateException
                var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
                {
                    await db.SaveChangesAsync();
                });

                LogMessage($"[Step 2]  DbUpdateException thrown as expected: {exception.Message}");
            }

            // =========================================================================
            // 4. Critical Verification: Use a FRESH ApplicationDbContext
            // =========================================================================
            await using (var db = CreateDbContext())
            {
                // Assertion 1: Original booking for A1 still exists
                var totalBookings = await db.Bookings.CountAsync(b => b.ShowtimeId == showtimeId);
                Assert.Equal(1, totalBookings);

                var bookingA = await db.Bookings
                    .Include(b => b.BookedSeats)
                    .FirstOrDefaultAsync(b => b.UserId == userAId && b.ShowtimeId == showtimeId);

                Assert.NotNull(bookingA);
                Assert.Equal(bookingRefA, bookingA.BookingRefrence);
                Assert.Single(bookingA.BookedSeats);
                Assert.Equal(rowA, bookingA.BookedSeats.First().Row);
                Assert.Equal(seatNumberA1, bookingA.BookedSeats.First().SeatNumber);

                // Assertion 2: The valid seat A2 from the failed transaction must NOT exist
                var isA2Inserted = await db.BookedSeats.AnyAsync(s =>
                    s.ShowtimeId == showtimeId && s.Row == rowA && s.SeatNumber == seatNumberA2);
                Assert.False(isA2Inserted, "CRITICAL: Seat A2 was valid/available but must NOT be persisted when the transaction rolls back.");

                // Assertion 3: User B's booking must NOT exist
                var isBookingBPersisted = await db.Bookings.AnyAsync(b => b.UserId == userBId);
                Assert.False(isBookingBPersisted, "CRITICAL: User B's booking must NOT be persisted in the database.");

                // Assertion 4: Only 1 seat in total is booked in the database (Seat A1)
                var totalBookedSeats = await db.BookedSeats.CountAsync(s => s.ShowtimeId == showtimeId);
                Assert.Equal(1, totalBookedSeats);

                // Assertion 5: Related state change (Showtime.Status) must also be rolled back to Scheduled
                var showtime = await db.Showtimes.FindAsync(showtimeId);
                Assert.NotNull(showtime);
                Assert.Equal(ShowtimeStatus.Scheduled, showtime.Status);

                LogMessage("================================================================================");
                LogMessage("       P0.3 DATABASE-LEVEL TRANSACTION ATOMICITY & ROLLBACK VERIFIED           ");
                LogMessage("================================================================================");
                LogMessage("  DbUpdateException raised on duplicate constraint during SaveChangesAsync()");
                LogMessage("  Fresh DbContext verified: Original A1 booking intact");
                LogMessage("  Fresh DbContext verified: Available A2 was NOT inserted");
                LogMessage("  Fresh DbContext verified: Booking B was NOT inserted");
                LogMessage("  Fresh DbContext verified: Showtime Status rolled back (still Scheduled)");
                LogMessage("  Fresh DbContext verified: Exactly 1 total booking & 1 total booked seat");
                LogMessage("================================================================================");
            }
        }
    }
}