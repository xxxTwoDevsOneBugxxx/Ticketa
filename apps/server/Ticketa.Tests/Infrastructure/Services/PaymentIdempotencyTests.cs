using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
  public class PaymentIdempotencyTests
  {
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<IShowtimeRepository> _mockShowtimeRepo;
    private readonly Mock<IPaymentRepository> _mockPaymentRepo;
    private readonly Mock<IBookingRepository> _mockBookingRepo;
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
    private const string DefaultPaymentIntentId = "pi_test_idem_12345";
    private const string DefaultClientSecret = "pi_test_idem_secret_abc";
    private const string DefaultBookingRef = "TKT-20261006-8888";

    public PaymentIdempotencyTests()
    {
      _mockUow = new Mock<IUnitOfWork>();
      _mockShowtimeRepo = new Mock<IShowtimeRepository>();
      _mockPaymentRepo = new Mock<IPaymentRepository>();
      _mockBookingRepo = new Mock<IBookingRepository>();

      _mockUow.Setup(u => u.Showtimes).Returns(_mockShowtimeRepo.Object);
      _mockUow.Setup(u => u.Payments).Returns(_mockPaymentRepo.Object);
      _mockUow.Setup(u => u.Bookings).Returns(_mockBookingRepo.Object);

      _mockBookingService = new Mock<IBookingService>();
      _mockEmailService = new Mock<IEmailService>();
      _mockQrCodeService = new Mock<IQrCodeService>();
      _mockPaymentIntentService = new Mock<PaymentIntentService>();
      _mockRefundService = new Mock<RefundService>();

      _logger = Mock.Of<ILogger<PaymentService>>();

      var inMemorySettings = new Dictionary<string, string?>
      {
        { "ClientSettings:BaseUrl", "http://localhost:5173" },
        { "EmailSettings:SendSynchronously", "true" }
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

    [Fact]
    public async Task CreateIntentAsync_UsesDeterministicStripeIdempotencyKey()
    {
      // Arrange
      var showtime = new ShowtimeBuilder().WithId(DefaultShowtimeId).Build();
      _mockShowtimeRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<ShowtimeByIdSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(showtime);

      _mockPaymentRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<PaymentSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((Payment?)null);

      RequestOptions? capturedRequestOptions = null;
      _mockPaymentIntentService
          .Setup(s => s.CreateAsync(
              It.IsAny<PaymentIntentCreateOptions>(),
              It.IsAny<RequestOptions>(),
              It.IsAny<CancellationToken>()))
          .Callback<PaymentIntentCreateOptions, RequestOptions, CancellationToken>((_, req, _) => capturedRequestOptions = req)
          .ReturnsAsync(new PaymentIntent { Id = DefaultPaymentIntentId, ClientSecret = DefaultClientSecret });

      var dto = new CreatePaymentIntentDto
      {
        ShowtimeId = DefaultShowtimeId,
        Seats = [new SeatDto { Row = 1, SeatNumber = 1 }]
      };

      // Act
      var result = await _sut.CreateIntentAsync(dto, DefaultUserId);

      // Assert: Verify idempotency key is deterministic and starts with pi_
      Assert.NotNull(result);
      Assert.NotNull(capturedRequestOptions);
      Assert.False(string.IsNullOrWhiteSpace(capturedRequestOptions.IdempotencyKey));
      Assert.StartsWith("pi_", capturedRequestOptions.IdempotencyKey);
      Assert.Contains(DefaultUserId, capturedRequestOptions.IdempotencyKey);
    }

    [Fact]
    public async Task CreateIntentAsync_WhenConcurrentCollisionThrowsDbUpdateException_ConvergesAndReturnsWinningPayment()
    {
      // Arrange: First check returns null, but when saving, another concurrent request already saved
      var showtime = new ShowtimeBuilder().WithId(DefaultShowtimeId).Build();
      _mockShowtimeRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<ShowtimeByIdSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(showtime);

      var existingPayment = new PaymentBuilder()
          .WithStripePaymentIntentId("pi_concurrent_winner")
          .WithClientSecret("secret_concurrent_winner")
          .WithTotalAmount(100m)
          .Build();

      // First call (dedup check) returns null
      // Second call (inside catch) returns the winner payment
      _mockPaymentRepo
          .SetupSequence(r => r.GetEntityWithSpecAsync(It.IsAny<PaymentSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((Payment?)null)
          .ReturnsAsync(existingPayment);

      _mockPaymentIntentService
          .Setup(s => s.CreateAsync(It.IsAny<PaymentIntentCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PaymentIntent { Id = DefaultPaymentIntentId, ClientSecret = DefaultClientSecret });

      // Simulate SQL Server unique constraint violation
      _mockUow.Setup(u => u.SaveAsync())
          .ThrowsAsync(new DbUpdateException("Cannot insert duplicate key row in object 'dbo.Payments' with unique index"));

      var dto = new CreatePaymentIntentDto
      {
        ShowtimeId = DefaultShowtimeId,
        Seats = [new SeatDto { Row = 1, SeatNumber = 1 }]
      };

      // Act: Should NOT throw 500 error! Must converge gracefully!
      var result = await _sut.CreateIntentAsync(dto, DefaultUserId);

      // Assert
      Assert.NotNull(result);
      Assert.Equal("pi_concurrent_winner", result.PaymentIntentId);
      Assert.Equal("secret_concurrent_winner", result.ClientSecret);
      Assert.Equal(100m, result.TotalAmount);
    }

    [Fact]
    public async Task ConfirmAsync_WhenPaymentAlreadyRefunded_ReturnsConflictAndNeverCallsStripeRefundOrCreatesBooking()
    {
      // Arrange
      var seats = new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } };
      var succeededIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", DefaultUserId },
          { "showtimeId", DefaultShowtimeId.ToString() },
          { "seats", JsonSerializer.Serialize(seats) }
        }
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(succeededIntent);

      var refundedPayment = new PaymentBuilder()
          .WithStripePaymentIntentId(DefaultPaymentIntentId)
          .WithStatus(PaymentStatus.Refunded)
          .Build();

      _mockPaymentRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Payment, bool>>>()))
          .ReturnsAsync(refundedPayment);

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert
      Assert.False(result.Succeeded);
      _mockBookingService.Verify(b => b.CreateForPaymentAsync(It.IsAny<BookingCreateDto>(), It.IsAny<string>(), It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockRefundService.Verify(r => r.CreateAsync(It.IsAny<RefundCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAsync_WhenGenuineConflictTriggersStripeRefund_UsesDeterministicRefundIdempotencyKey()
    {
      // Arrange
      var seats = new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } };
      var succeededIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", DefaultUserId },
          { "showtimeId", DefaultShowtimeId.ToString() },
          { "seats", JsonSerializer.Serialize(seats) }
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

      RequestOptions? capturedRefundRequest = null;
      _mockRefundService
          .Setup(r => r.CreateAsync(It.IsAny<RefundCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .Callback<RefundCreateOptions, RequestOptions, CancellationToken>((_, req, _) => capturedRefundRequest = req)
          .ReturnsAsync(new Refund { Id = "re_test_123" });

      // Act
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert
      Assert.False(result.Succeeded);
      Assert.NotNull(capturedRefundRequest);
      Assert.Equal($"rf_{DefaultPaymentIntentId}", capturedRefundRequest.IdempotencyKey);
      Assert.Equal(PaymentStatus.Refunded, payment.Status);
      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task ConfirmAsync_WhenBookingAlreadyCreatedFromPreviousCrash_ReconcilesWithoutRefunding()
    {
      // Arrange: Payment was saved with BookingReference, but crashed before marking Completed
      var seats = new List<SeatDto> { new() { Row = 1, SeatNumber = 1 } };
      var succeededIntent = new PaymentIntent
      {
        Id = DefaultPaymentIntentId,
        Status = "succeeded",
        Metadata = new Dictionary<string, string>
        {
          { "userId", DefaultUserId },
          { "showtimeId", DefaultShowtimeId.ToString() },
          { "seats", JsonSerializer.Serialize(seats) }
        }
      };

      _mockPaymentIntentService
          .Setup(s => s.GetAsync(DefaultPaymentIntentId, It.IsAny<PaymentIntentGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(succeededIntent);

      var payment = new PaymentBuilder()
          .WithStripePaymentIntentId(DefaultPaymentIntentId)
          .WithStatus(PaymentStatus.Pending)
          .WithBookingReference(DefaultBookingRef)
          .WithTotalAmount(100m)
          .Build();

      _mockPaymentRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Payment, bool>>>()))
          .ReturnsAsync(payment);

      var existingBooking = new Booking
      {
        BookingRefrence = DefaultBookingRef,
        TotalAmount = 100m,
        UserId = DefaultUserId,
        ShowtimeId = DefaultShowtimeId,
        Status = BookingStatus.Confirmed
      };

      _mockBookingRepo
          .Setup(b => b.GetBookingByRefrenceAsync(DefaultBookingRef, It.IsAny<CancellationToken>()))
          .ReturnsAsync(existingBooking);

      // Act: Retry confirm-payment
      var result = await _sut.ConfirmAsync(DefaultPaymentIntentId, DefaultUserId);

      // Assert: Must reconcile and NEVER refund!
      Assert.True(result.Succeeded);
      Assert.Equal(DefaultBookingRef, result.BookingReference);
      Assert.Equal(PaymentStatus.Completed, payment.Status);
      _mockRefundService.Verify(r => r.CreateAsync(It.IsAny<RefundCreateOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockBookingService.Verify(b => b.CreateForPaymentAsync(It.IsAny<BookingCreateDto>(), It.IsAny<string>(), It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }
  }
}
