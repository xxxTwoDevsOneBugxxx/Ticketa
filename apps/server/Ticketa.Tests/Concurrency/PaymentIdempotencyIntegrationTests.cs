using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;
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
  public class PaymentIdempotencyIntegrationTests
  {
    private readonly MsSqlDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PaymentIdempotencyIntegrationTests(MsSqlDatabaseFixture fixture, ITestOutputHelper output)
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

    [Fact]
    public async Task CreateIntentAsync_ConcurrentRequests_ConvergeOnSingleDatabasePaymentRecord()
    {
      // 1. Arrange: Seed 1 Hall, 1 Movie, 1 Showtime, 1 User
      int showtimeId;
      var userId = $"user-idem-{Guid.NewGuid():N}";
      var seat = new SeatDto { Row = 1, SeatNumber = 1 };

      await using (var db = CreateDbContext())
      {
        var hall = new Hall
        {
          Name = $"Idem-Hall-{Guid.NewGuid():N}",
          Type = HallType.Standard,
          TotalRows = 5,
          SeatsPerRow = 5
        };
        db.Halls.Add(hall);

        var movie = new Movie
        {
          Title = $"Idem-Movie-{Guid.NewGuid():N}",
          TmdbId = Random.Shared.Next(100000, 999999),
          Status = MovieStatus.Active,
          RuntimeMinutes = 110
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

        db.Users.Add(new AppUser
        {
          Id = userId,
          UserName = $"user_{Guid.NewGuid():N}@test.com",
          Email = $"user_{Guid.NewGuid():N}@test.com",
          FirstName = "Idempotent",
          LastName = "User"
        });

        await db.SaveChangesAsync();
        showtimeId = showtime.Id;
      }

      // Setup mock Stripe service: returns the same deterministic PaymentIntent for the same operation
      var mockStripe = new Mock<PaymentIntentService>();
      var stableIntentId = $"pi_test_{Guid.NewGuid():N}";
      var stableSecret = $"{stableIntentId}_secret";

      mockStripe
          .Setup(s => s.CreateAsync(
              It.IsAny<PaymentIntentCreateOptions>(),
              It.IsAny<RequestOptions>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PaymentIntent
          {
            Id = stableIntentId,
            ClientSecret = stableSecret
          });

      var serviceProvider = BuildServiceProvider(_fixture.ConnectionString, mockStripe.Object, new Mock<RefundService>().Object);

      // 2. Act: 10 concurrent requests to CreateIntentAsync for the exact same user and seat
      const int concurrentCount = 10;
      var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var results = new ConcurrentBag<PaymentIntentResultDto>();

      var tasks = Enumerable.Range(0, concurrentCount).Select(async _ =>
      {
        using var scope = serviceProvider.CreateScope();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

        await barrier.Task; // Synchronize start

        var dto = new CreatePaymentIntentDto
        {
          ShowtimeId = showtimeId,
          Seats = [seat]
        };

        var result = await paymentService.CreateIntentAsync(dto, userId);
        if (result != null)
        {
          results.Add(result);
        }
      });

      var runningTasks = tasks.ToArray();
      barrier.SetResult(); // Fire all tasks simultaneously
      await Task.WhenAll(runningTasks);

      // 3. Assert: All 10 callers converged on the same PaymentIntent
      Assert.Equal(concurrentCount, results.Count);
      foreach (var r in results)
      {
        Assert.Equal(stableIntentId, r.PaymentIntentId);
        Assert.Equal(stableSecret, r.ClientSecret);
      }

      // 4. Assert: Database integrity - Exactly 1 row in Payments table
      await using (var db = CreateDbContext())
      {
        var paymentsInDb = await db.Payments
            .Where(p => p.UserId == userId && p.ShowtimeId == showtimeId)
            .ToListAsync();

        Assert.Single(paymentsInDb);
        Assert.Equal(stableIntentId, paymentsInDb[0].StripePaymentIntentId);
        Assert.Equal(PaymentStatus.Pending, paymentsInDb[0].Status);
        LogMessage($"[Verified] 10 concurrent CreateIntent calls converged to exactly 1 DB Payment record: {stableIntentId}");
      }
    }

    [Fact]
    public async Task ConfirmAsync_WhenRepeated_ReturnsCachedSuccessWithoutCreatingDuplicateBookings()
    {
      // 1. Arrange: Seed 1 Hall, 1 Movie, 1 Showtime, 1 User, 1 Pending Payment
      int showtimeId;
      var userId = $"user-confirm-{Guid.NewGuid():N}";
      var paymentIntentId = $"pi_confirm_{Guid.NewGuid():N}";
      var seats = new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } };
      var seatsJson = System.Text.Json.JsonSerializer.Serialize(seats);

      await using (var db = CreateDbContext())
      {
        var hall = new Hall
        {
          Name = $"Confirm-Hall-{Guid.NewGuid():N}",
          Type = HallType.Standard,
          TotalRows = 5,
          SeatsPerRow = 5
        };
        db.Halls.Add(hall);

        var movie = new Movie
        {
          Title = $"Confirm-Movie-{Guid.NewGuid():N}",
          TmdbId = Random.Shared.Next(100000, 999999),
          Status = MovieStatus.Active,
          RuntimeMinutes = 110
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

        db.Users.Add(new AppUser
        {
          Id = userId,
          UserName = $"user_{Guid.NewGuid():N}@test.com",
          Email = $"user_{Guid.NewGuid():N}@test.com",
          FirstName = "Confirm",
          LastName = "User"
        });

        await db.SaveChangesAsync();
        showtimeId = showtime.Id;

        var payment = new Payment
        {
          StripePaymentIntentId = paymentIntentId,
          ClientSecret = $"{paymentIntentId}_secret",
          UserId = userId,
          ShowtimeId = showtimeId,
          TotalAmount = 100m,
          Currency = "AED",
          Status = PaymentStatus.Pending,
          SeatHash = "1:1",
          SeatCount = 1,
          CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);

        await db.SaveChangesAsync();
      }

      var mockStripe = new Mock<PaymentIntentService>();
      mockStripe
          .Setup(s => s.GetAsync(paymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PaymentIntent
          {
            Id = paymentIntentId,
            Status = "succeeded",
            Metadata = new Dictionary<string, string>
            {
              { "userId", userId },
              { "showtimeId", showtimeId.ToString() },
              { "seats", seatsJson }
            }
          });

      var serviceProvider = BuildServiceProvider(_fixture.ConnectionString, mockStripe.Object, new Mock<RefundService>().Object);

      // 2. Act: Call ConfirmAsync twice in sequence (simulating user double-clicking or page refresh)
      using var scope1 = serviceProvider.CreateScope();
      var svc1 = scope1.ServiceProvider.GetRequiredService<IPaymentService>();
      var result1 = await svc1.ConfirmAsync(paymentIntentId, userId);

      using var scope2 = serviceProvider.CreateScope();
      var svc2 = scope2.ServiceProvider.GetRequiredService<IPaymentService>();
      var result2 = await svc2.ConfirmAsync(paymentIntentId, userId);

      // 3. Assert: Both calls return the exact same booking reference and success
      Assert.True(result1.Succeeded);
      Assert.True(result2.Succeeded);
      Assert.Equal(result1.BookingReference, result2.BookingReference);

      // 4. Assert: Database contains strictly 1 Booking and 1 BookedSeat
      await using (var db = CreateDbContext())
      {
        var totalBookings = await db.Bookings.CountAsync(b => b.UserId == userId && b.ShowtimeId == showtimeId);
        var totalSeats = await db.BookedSeats.CountAsync(s => s.ShowtimeId == showtimeId && s.Row == 1 && s.SeatNumber == 1);
        var paymentInDb = await db.Payments.FirstAsync(p => p.StripePaymentIntentId == paymentIntentId);

        Assert.Equal(1, totalBookings);
        Assert.Equal(1, totalSeats);
        Assert.Equal(PaymentStatus.Completed, paymentInDb.Status);
        Assert.Equal(result1.BookingReference, paymentInDb.BookingReference);
        LogMessage($"[Verified] Repeating ConfirmAsync returned cached booking {result1.BookingReference} with 0 duplicate records.");
      }
    }

    [Fact]
    public async Task ConfirmPayment_ConcurrentRequestsForSamePayment_CreateSingleBooking()
    {
      // 1. Preconditions / Arrange:
      // Seed 1 Hall, 1 Movie, 1 Showtime, 1 User, 1 Pending Payment with 3 seats (A1, A2, A3)
      int showtimeId;
      var userId = $"user-concurrent-{Guid.NewGuid():N}";
      var paymentIntentId = $"pi_test_concurrent_{Guid.NewGuid():N}";
      var seats = new List<SeatDto>
      {
        new() { Row = 1, SeatNumber = 1 },
        new() { Row = 1, SeatNumber = 2 },
        new() { Row = 1, SeatNumber = 3 }
      };
      var seatsJson = System.Text.Json.JsonSerializer.Serialize(seats);

      await using (var db = CreateDbContext())
      {
        var hall = new Hall
        {
          Name = $"Concurrent-Hall-{Guid.NewGuid():N}",
          Type = HallType.Standard,
          TotalRows = 10,
          SeatsPerRow = 10
        };
        db.Halls.Add(hall);

        var movie = new Movie
        {
          Title = $"Concurrent-Movie-{Guid.NewGuid():N}",
          TmdbId = Random.Shared.Next(100000, 999999),
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

        db.Users.Add(new AppUser
        {
          Id = userId,
          UserName = $"user_{Guid.NewGuid():N}@test.com",
          Email = $"user_{Guid.NewGuid():N}@test.com",
          FirstName = "Concurrent",
          LastName = "User"
        });

        await db.SaveChangesAsync();
        showtimeId = showtime.Id;

        var payment = new Payment
        {
          StripePaymentIntentId = paymentIntentId,
          ClientSecret = $"{paymentIntentId}_secret",
          UserId = userId,
          ShowtimeId = showtimeId,
          TotalAmount = 300m,
          Currency = "AED",
          Status = PaymentStatus.Pending,
          SeatHash = "1:1,1:2,1:3",
          SeatCount = 3,
          CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);

        await db.SaveChangesAsync();
      }

      // Configure Fake Stripe Service
      var mockStripe = new Mock<PaymentIntentService>();
      mockStripe
          .Setup(s => s.GetAsync(paymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PaymentIntent
          {
            Id = paymentIntentId,
            Status = "succeeded",
            Metadata = new Dictionary<string, string>
            {
              { "userId", userId },
              { "showtimeId", showtimeId.ToString() },
              { "seats", seatsJson }
            }
          });

      var mockRefund = new Mock<RefundService>();

      var serviceProvider = BuildServiceProvider(_fixture.ConnectionString, mockStripe.Object, mockRefund.Object);

      // 2. Concurrent Requests: 20 simultaneous ConfirmAsync calls starting together
      const int confirmCount = 20;
      var startGate = new ManualResetEventSlim(false);

      var tasks = Enumerable.Range(0, confirmCount)
          .Select(_ => Task.Run(async () =>
          {
            using var scope = serviceProvider.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

            startGate.Wait();

            return await paymentService.ConfirmAsync(
                paymentIntentId,
                userId,
                CancellationToken.None);
          }))
          .ToArray();

      startGate.Set(); // Release all tasks simultaneously

      var results = await Task.WhenAll(tasks);

      // 3. Result Assertions
      var successfulBookings = results
          .Where(r => r.Succeeded)
          .Select(r => r.BookingReference)
          .Distinct()
          .ToList();

      Assert.Single(successfulBookings);
      var canonicalBookingRef = successfulBookings.First();
      Assert.False(string.IsNullOrWhiteSpace(canonicalBookingRef));

      // 4. Refund Assertions: Zero refunds triggered
      mockRefund.Verify(r => r.CreateAsync(
          It.IsAny<RefundCreateOptions>(),
          It.IsAny<RequestOptions>(),
          It.IsAny<CancellationToken>()), Times.Never);

      // 5. Fresh DbContext Assertions
      await using (var db = CreateDbContext())
      {
        // Payment Assertions
        var paymentInDb = await db.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId);
        Assert.NotNull(paymentInDb);
        Assert.Equal(PaymentStatus.Completed, paymentInDb.Status);
        Assert.Equal(canonicalBookingRef, paymentInDb.BookingReference);

        var totalPayments = await db.Payments.CountAsync(p => p.StripePaymentIntentId == paymentIntentId);
        Assert.Equal(1, totalPayments);

        // Booking Assertions
        var bookingsInDb = await db.Bookings
            .Include(b => b.BookedSeats)
            .Where(b => b.ShowtimeId == showtimeId && b.UserId == userId)
            .ToListAsync();

        Assert.Single(bookingsInDb);
        var booking = bookingsInDb[0];
        Assert.Equal(userId, booking.UserId);
        Assert.Equal(showtimeId, booking.ShowtimeId);
        Assert.Equal(300m, booking.TotalAmount);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(canonicalBookingRef, booking.BookingRefrence);
        Assert.Equal(3, booking.BookedSeats.Count);

        // Booked Seat Assertions: Each expected seat exists exactly once
        var bookedSeatsInDb = await db.BookedSeats
            .Where(s => s.ShowtimeId == showtimeId)
            .ToListAsync();

        Assert.Equal(3, bookedSeatsInDb.Count);
        Assert.Contains(bookedSeatsInDb, s => s.Row == 1 && s.SeatNumber == 1);
        Assert.Contains(bookedSeatsInDb, s => s.Row == 1 && s.SeatNumber == 2);
        Assert.Contains(bookedSeatsInDb, s => s.Row == 1 && s.SeatNumber == 3);

        LogMessage("================================================================================");
        LogMessage("   P0.4 CONCURRENT CONFIRM INTEGRATION TEST VERIFIED SUCCESSFULLY               ");
        LogMessage("================================================================================");
        LogMessage($"  20 concurrent ConfirmAsync requests converged on Booking: {canonicalBookingRef}");
        LogMessage("  Exactly 1 Payment in database (Status = Completed)");
        LogMessage("  Exactly 1 Booking in database");
        LogMessage("  Exactly 3 BookedSeats in database (no duplicates)");
        LogMessage("  Exactly 0 Stripe refunds triggered (no accidental refund)");
        LogMessage("================================================================================");
      }
    }

    private static IServiceProvider BuildServiceProvider(string connectionString, PaymentIntentService stripeService, RefundService refundService)
    {
      var services = new ServiceCollection();
      services.AddLogging(builder => builder.AddConsole());

      services.AddDbContext<ApplicationDbContext>(options =>
          options.UseSqlServer(connectionString));

      var inMemoryConfig = new Dictionary<string, string?>
      {
        { "AppTimeZone", "UTC" },
        { "ClientSettings:BaseUrl", "http://localhost:5173" }
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
      services.AddScoped<IPaymentRepository, PaymentRepository>();
      services.AddScoped<IBookingService, BookingService>();

      services.AddScoped<IEmailService>(_ => Mock.Of<IEmailService>());
      services.AddScoped<IQrCodeService>(_ =>
      {
        var mock = new Mock<IQrCodeService>();
        mock.Setup(q => q.GeneratePng(It.IsAny<string>(), It.IsAny<int>())).Returns([0x89, 0x50]);
        return mock.Object;
      });

      services.AddSingleton(stripeService);
      services.AddSingleton(refundService);
      services.AddScoped<IPaymentService, PaymentService>();

      return services.BuildServiceProvider();
    }
  }
}
