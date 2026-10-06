using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;
using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;
using Ticketa.Core.Enums;
using Ticketa.Core.Interfaces;
using Ticketa.Core.Interfaces.IRepositories;
using Ticketa.Core.Interfaces.IServices;
using Ticketa.Core.Specifications;
using Ticketa.Infrastructure.Service;
using Ticketa.Tests.TestBuilders;
using Xunit;

namespace Ticketa.Tests.Infrastructure.Services
{
  public class PaymentServiceTests
  {
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<IShowtimeRepository> _mockShowtimeRepo;
    private readonly Mock<IPaymentRepository> _mockPaymentRepo;
    private readonly Mock<IBookingService> _mockBookingService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<IQrCodeService> _mockQrCodeService;
    private readonly Mock<PaymentIntentService> _mockPaymentIntentService;
    private readonly Mock<RefundService> _mockRefundService;
    private readonly ILogger<PaymentService> _logger;
    private readonly IConfiguration _configuration;
    private readonly PaymentService _sut;

    private const int DefaultShowtimeId = 1;
    private const string DefaultUserId = "user-123";
    private const string DefaultPaymentIntentId = "pi_test_123456789";
    private const string DefaultClientSecret = "pi_test_123456789_secret_xyz";
    private const string DefaultBookingRef = "TKT-20260908-9999";

    public PaymentServiceTests()
    {
      _mockUow = new Mock<IUnitOfWork>();
      _mockShowtimeRepo = new Mock<IShowtimeRepository>();
      _mockPaymentRepo = new Mock<IPaymentRepository>();

      _mockUow.Setup(u => u.Showtimes).Returns(_mockShowtimeRepo.Object);
      _mockUow.Setup(u => u.Payments).Returns(_mockPaymentRepo.Object);

      _mockBookingService = new Mock<IBookingService>();
      _mockEmailService = new Mock<IEmailService>();
      _mockQrCodeService = new Mock<IQrCodeService>();
      _mockPaymentIntentService = new Mock<PaymentIntentService>();
      _mockRefundService = new Mock<RefundService>();

      _logger = Mock.Of<ILogger<PaymentService>>();

      var inMemorySettings = new Dictionary<string, string?>
      {
        { "ClientSettings:BaseUrl", "http://localhost:5173" }
      };
      _configuration = new ConfigurationBuilder()
          .AddInMemoryCollection(inMemorySettings)
          .Build();

      _sut = new PaymentService(
          _mockUow.Object,
          _mockBookingService.Object,
          _mockEmailService.Object,
          _mockQrCodeService.Object,
          _configuration,
          _logger,
          _mockPaymentIntentService.Object,
          _mockRefundService.Object);
    }

    #region CreateIntentAsync Tests

    [Fact]
    public async Task CreateIntentAsync_WhenShowtimeNotFound_ReturnsNullAndNeverCallsStripeOrSaves()
    {
      // Arrange
      var dto = new CreatePaymentIntentDto
      {
        ShowtimeId = DefaultShowtimeId,
        Seats = [new SeatDto { Row = 1, SeatNumber = 1 }]
      };

      _mockShowtimeRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<ShowtimeByIdSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((Showtime?)null);

      // Act
      var result = await _sut.CreateIntentAsync(dto, DefaultUserId);

      // Assert
      Assert.Null(result);
      _mockPaymentIntentService.Verify(s => s.CreateAsync(
          It.IsAny<PaymentIntentCreateOptions>(),
          It.IsAny<RequestOptions>(),
          It.IsAny<CancellationToken>()), Times.Never);
      _mockPaymentRepo.Verify(r => r.CreateAsync(It.IsAny<Payment>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateIntentAsync_WhenDuplicatePendingPaymentExists_ReturnsCachedIntentWithoutCallingStripe()
    {
      // Arrange
      var seats = new List<SeatDto>
      {
        new() { Row = 1, SeatNumber = 1 }
      };
      var dto = new CreatePaymentIntentDto
      {
        ShowtimeId = DefaultShowtimeId,
        Seats = seats
      };

      var showtime = new ShowtimeBuilder().WithId(DefaultShowtimeId).Build();
      _mockShowtimeRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<ShowtimeByIdSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(showtime);

      var existingPayment = new PaymentBuilder()
          .WithStripePaymentIntentId(DefaultPaymentIntentId)
          .WithClientSecret(DefaultClientSecret)
          .WithTotalAmount(100m)
          .Build();

      _mockPaymentRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<PaymentSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(existingPayment);

      // Act
      var result = await _sut.CreateIntentAsync(dto, DefaultUserId);

      // Assert
      Assert.NotNull(result);
      Assert.Equal(DefaultPaymentIntentId, result.PaymentIntentId);
      Assert.Equal(DefaultClientSecret, result.ClientSecret);
      Assert.Equal(100m, result.TotalAmount);

      _mockPaymentIntentService.Verify(s => s.CreateAsync(
          It.IsAny<PaymentIntentCreateOptions>(),
          It.IsAny<RequestOptions>(),
          It.IsAny<CancellationToken>()), Times.Never);
      _mockPaymentRepo.Verify(r => r.CreateAsync(It.IsAny<Payment>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateIntentAsync_WithValidSeats_CalculatesAmountInMinorUnitsAndCreatesPendingPayment()
    {
      // Arrange: Standard Hall ($100 base): Row 1 Regular (100) + Row 10 VIP (150) = 250 Total
      var showtime = new ShowtimeBuilder()
          .WithId(DefaultShowtimeId)
          .WithHallType(HallType.Standard)
          .WithPrice(100m)
          .Build();

      var seats = new List<SeatDto>
      {
        new() { Row = 10, SeatNumber = 5 }, // VIP (1.5x = 150)
        new() { Row = 1, SeatNumber = 5 }   // Regular (1.0x = 100)
      };
      var dto = new CreatePaymentIntentDto
      {
        ShowtimeId = DefaultShowtimeId,
        Seats = seats
      };

      _mockShowtimeRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<ShowtimeByIdSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(showtime);

      _mockPaymentRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<PaymentSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((Payment?)null);

      PaymentIntentCreateOptions? capturedOptions = null;
      var stripeIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        ClientSecret = DefaultClientSecret
      };

      RequestOptions? capturedRequestOptions = null;
      _mockPaymentIntentService
          .Setup(s => s.CreateAsync(
              It.IsAny<PaymentIntentCreateOptions>(),
              It.IsAny<RequestOptions>(),
              It.IsAny<CancellationToken>()))
          .Callback<PaymentIntentCreateOptions, RequestOptions, CancellationToken>((opts, req, _) =>
          {
            capturedOptions = opts;
            capturedRequestOptions = req;
          })
          .ReturnsAsync(stripeIntent);

      Payment? capturedPayment = null;
      _mockPaymentRepo
          .Setup(r => r.CreateAsync(It.IsAny<Payment>()))
          .Callback<Payment>(p => capturedPayment = p)
          .Returns(Task.CompletedTask);

      // Act
      var result = await _sut.CreateIntentAsync(dto, DefaultUserId);

      // Assert
      Assert.NotNull(result);
      Assert.Equal(DefaultPaymentIntentId, result.PaymentIntentId);
      Assert.Equal(DefaultClientSecret, result.ClientSecret);
      Assert.Equal(250m, result.TotalAmount);

      // Verify deterministic Stripe IdempotencyKey
      Assert.NotNull(capturedRequestOptions);
      Assert.StartsWith("pi_", capturedRequestOptions.IdempotencyKey);
      Assert.Contains(DefaultUserId, capturedRequestOptions.IdempotencyKey);

      // Verify Stripe amount converted to minor units (250 * 100 = 25000)
      Assert.NotNull(capturedOptions);
      Assert.Equal(25000, capturedOptions.Amount);
      Assert.Equal("AED", capturedOptions.Currency);
      Assert.Equal(DefaultUserId, capturedOptions.Metadata["userId"]);
      Assert.Equal(DefaultShowtimeId.ToString(), capturedOptions.Metadata["showtimeId"]);

      // Verify DB Payment entity
      Assert.NotNull(capturedPayment);
      Assert.Equal(DefaultPaymentIntentId, capturedPayment.StripePaymentIntentId);
      Assert.Equal(DefaultClientSecret, capturedPayment.ClientSecret);
      Assert.Equal(PaymentStatus.Pending, capturedPayment.Status);
      Assert.Equal(250m, capturedPayment.TotalAmount);
      Assert.Equal(2, capturedPayment.PaymentSeats.Count);
      Assert.Equal("1:5,10:5", capturedPayment.SeatHash); // Sorted row/seat order

      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    #endregion

    #region ConfirmAsync Tests

    [Fact]
    public async Task ConfirmAsync_WhenPaymentIntentStatusNotSucceeded_ReturnsFailureAndNeverCreatesBooking()
    {
      // Arrange: Payment incomplete on Stripe (e.g. requires_payment_method)
      var incompleteIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "requires_payment_method"
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(incompleteIntent);

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert
      Assert.False(result.Succeeded);
      Assert.Equal("Payment has not been completed.", result.Message);
      _mockBookingService.Verify(b => b.CreateAsync(It.IsAny<BookingCreateDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_WhenUserIdDoesNotMatchIntentMetadata_ReturnsUnauthorizedFailure()
    {
      // Arrange: Intent belongs to "attacker-999" but called by "user-123"
      var stolenIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", "attacker-999" }
        }
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(stolenIntent);

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert
      Assert.False(result.Succeeded);
      Assert.Equal("Unauthorized access.", result.Message);
      _mockBookingService.Verify(b => b.CreateAsync(It.IsAny<BookingCreateDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_WhenPaymentAlreadyCompletedInDb_ReturnsCachedSuccessAndNeverCreatesBooking()
    {
      // Arrange: Idempotency check: Payment is already Completed
      var seatsJson = JsonSerializer.Serialize(new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } });
      var succeededIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", DefaultUserId },
          { "showtimeId", DefaultShowtimeId.ToString() },
          { "seats", seatsJson }
        }
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(succeededIntent);

      var alreadyCompletedPayment = new PaymentBuilder()
          .WithStripePaymentIntentId(DefaultPaymentIntentId)
          .WithStatus(PaymentStatus.Completed)
          .WithBookingReference(DefaultBookingRef)
          .WithTotalAmount(100m)
          .Build();

      _mockPaymentRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Payment, bool>>>()))
          .ReturnsAsync(alreadyCompletedPayment);

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert
      Assert.True(result.Succeeded);
      Assert.Equal(DefaultBookingRef, result.BookingReference);
      Assert.Equal(100m, result.TotalAmount);

      _mockBookingService.Verify(b => b.CreateAsync(It.IsAny<BookingCreateDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_WhenPaymentSucceeded_CompletesPaymentGeneratesQrAndSendsEmail()
    {
      // Arrange
      var seats = new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } };
      var seatsJson = JsonSerializer.Serialize(seats);
      var succeededIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", DefaultUserId },
          { "showtimeId", DefaultShowtimeId.ToString() },
          { "seats", seatsJson }
        }
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(succeededIntent);

      var payment = new PaymentBuilder()
          .WithStripePaymentIntentId(DefaultPaymentIntentId)
          .WithStatus(PaymentStatus.Pending)
          .Build();

      _mockPaymentRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Payment, bool>>>()))
          .ReturnsAsync(payment);

      _mockBookingService
          .Setup(b => b.CreateForPaymentAsync(It.IsAny<BookingCreateDto>(), DefaultUserId, It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(BookingResultDto.Success(DefaultBookingRef, 100m));

      var bookingDetails = new BookingDetailsDto
      {
        CustomerFirstName = "John",
        CustomerEmail = "john@example.com",
        MovieTitle = "Inception",
        MovieBackdropPath = "/backdrop.jpg",
        StartsAt = DateTime.UtcNow.AddHours(2),
        HallName = "Main Hall",
        Seats = [new BookingDetailsSeatDto { Row = 1, SeatNumber = 1, Category = "Regular", Price = 100m }],
        TotalAmount = 100m
      };

      _mockBookingService
          .Setup(b => b.GetByReferenceAsync(DefaultBookingRef, It.IsAny<CancellationToken>()))
          .ReturnsAsync(bookingDetails);

      var fakeQrBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
      _mockQrCodeService
          .Setup(q => q.GeneratePng(It.IsAny<string>(), It.IsAny<int>()))
          .Returns(fakeQrBytes);

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert
      Assert.True(result.Succeeded);
      Assert.Equal(DefaultBookingRef, result.BookingReference);

      _mockQrCodeService.Verify(q => q.GeneratePng("http://localhost:5173/bookings/" + DefaultBookingRef, It.IsAny<int>()), Times.Once);
      _mockEmailService.Verify(e => e.SendEmailWithInlineImageAsync(
          "john@example.com",
          "Your Ticketa Booking Confirmation",
          It.IsAny<string>(),
          fakeQrBytes,
          "ticket-qr",
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAsync_WhenBookingReturnsSeatConflict_TriggersStripeRefundAndMarksPaymentRefunded()
    {
      // Arrange: Payment passed on Stripe, but concurrent user grabbed seat
      var seats = new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } };
      var seatsJson = JsonSerializer.Serialize(seats);
      var succeededIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", DefaultUserId },
          { "showtimeId", DefaultShowtimeId.ToString() },
          { "seats", seatsJson }
        }
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(succeededIntent);

      var payment = new PaymentBuilder()
          .WithStripePaymentIntentId(DefaultPaymentIntentId)
          .WithStatus(PaymentStatus.Pending)
          .Build();

      _mockPaymentRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Payment, bool>>>()))
          .ReturnsAsync(payment);

      _mockBookingService
          .Setup(b => b.CreateForPaymentAsync(It.IsAny<BookingCreateDto>(), DefaultUserId, It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(BookingResultDto.Conflict(seats));

      RefundCreateOptions? capturedRefundOptions = null;
      RequestOptions? capturedRefundRequestOptions = null;
      _mockRefundService
          .Setup(r => r.CreateAsync(It.IsAny<RefundCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .Callback<RefundCreateOptions, RequestOptions, CancellationToken>((opts, req, _) =>
          {
            capturedRefundOptions = opts;
            capturedRefundRequestOptions = req;
          })
          .ReturnsAsync(new Refund { Id = "re_123" });

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert
      Assert.False(result.Succeeded);
      Assert.NotEmpty(result.ConflictingSeats);

      // Verify Stripe Refund was triggered with the PaymentIntentId & deterministic IdempotencyKey
      Assert.NotNull(capturedRefundOptions);
      Assert.Equal(DefaultPaymentIntentId, capturedRefundOptions.PaymentIntent);
      Assert.NotNull(capturedRefundRequestOptions);
      Assert.Equal($"rf_{DefaultPaymentIntentId}", capturedRefundRequestOptions.IdempotencyKey);

      // Verify Payment record status is Refunded
      Assert.Equal(PaymentStatus.Refunded, payment.Status);
      Assert.NotNull(payment.RefundedAt);

      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task ConfirmAsync_WhenEmailSendingThrows_BookingStillSucceeds()
    {
      // Arrange: Email service throws (e.g. SMTP connection timeout)
      var seats = new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } };
      var seatsJson = JsonSerializer.Serialize(seats);
      var succeededIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", DefaultUserId },
          { "showtimeId", DefaultShowtimeId.ToString() },
          { "seats", seatsJson }
        }
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(succeededIntent);

      var payment = new PaymentBuilder()
          .WithStripePaymentIntentId(DefaultPaymentIntentId)
          .WithStatus(PaymentStatus.Pending)
          .Build();

      _mockPaymentRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Payment, bool>>>()))
          .ReturnsAsync(payment);

      _mockBookingService
          .Setup(b => b.CreateForPaymentAsync(It.IsAny<BookingCreateDto>(), DefaultUserId, It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(BookingResultDto.Success(DefaultBookingRef, 100m));

      var bookingDetails = new BookingDetailsDto
      {
        CustomerFirstName = "John",
        CustomerEmail = "john@example.com",
        MovieTitle = "Inception",
        StartsAt = DateTime.UtcNow.AddHours(2),
        HallName = "Main Hall",
        Seats = [new BookingDetailsSeatDto { Row = 1, SeatNumber = 1, Category = "Regular", Price = 100m }],
        TotalAmount = 100m
      };

      _mockBookingService
          .Setup(b => b.GetByReferenceAsync(DefaultBookingRef, It.IsAny<CancellationToken>()))
          .ReturnsAsync(bookingDetails);

      _mockQrCodeService
          .Setup(q => q.GeneratePng(It.IsAny<string>(), It.IsAny<int>()))
          .Returns([0x89, 0x50]);

      _mockEmailService
          .Setup(e => e.SendEmailWithInlineImageAsync(
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<byte[]>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("SMTP server connection timeout"));

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert: Result is STILL SUCCESS even when email fails
      Assert.True(result.Succeeded);
      Assert.Equal(DefaultBookingRef, result.BookingReference);
    }

    #endregion
  }
}
