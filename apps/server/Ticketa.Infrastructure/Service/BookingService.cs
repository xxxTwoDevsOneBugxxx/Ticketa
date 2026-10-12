using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;
using Ticketa.Core.Enums;
using Ticketa.Core.Helpers;
using Ticketa.Core.Interfaces;
using Ticketa.Core.Interfaces.IRepositories;
using Ticketa.Core.Specifications;

namespace Ticketa.Infrastructure.Service
{
  public class BookingService(IUnitOfWork uow, ILogger<BookingService> logger, TimeConversions timeConversions) : IBookingService
  {
    private readonly IUnitOfWork _uow = uow;
    private readonly ILogger<BookingService> _logger = logger;

    public Task<BookingResultDto> CreateAsync(BookingCreateDto dto, string userId, CancellationToken ct = default)
    {
      return CreateInternalAsync(dto, userId, payment: null, ct);
    }

    public Task<BookingResultDto> CreateForPaymentAsync(BookingCreateDto dto, string userId, Payment payment, CancellationToken ct = default)
    {
      return CreateInternalAsync(dto, userId, payment, ct);
    }

    private async Task<BookingResultDto> CreateInternalAsync(BookingCreateDto dto, string userId, Payment? payment, CancellationToken ct = default)
    {
      _logger.LogInformation("Booking attempt: ShowtimeId={ShowtimeId}, Seats={Seats}, UserId={UserId}, PaymentId={PaymentId}",
        dto.ShowtimeId, dto.Seats.Select(s => $"R{s.Row}S{s.SeatNumber}"), userId, payment?.Id);

      var spec = new ShowtimeByIdSpecification(dto.ShowtimeId);
      var showtime = await _uow.Showtimes.GetEntityWithSpecAsync(spec, ct);

      if (showtime is null)
      {
        _logger.LogWarning("Booking conflict: ShowtimeId={ShowtimeId} not found", dto.ShowtimeId);
        return BookingResultDto.Conflict([]);
      }

      var conflict = (await _uow.BookedSeats.GetConflictAsync(dto.ShowtimeId, dto.Seats, ct)).ToList();

      if (conflict.Count > 0)
      {
        // Crash / Retry / Concurrency Reconciliation:
        // If a confirmed booking already exists for this user and showtime, reconcile!
        var committedBooking = await _uow.Bookings.GetAsync(b => b.UserId == userId && b.ShowtimeId == dto.ShowtimeId && b.Status == BookingStatus.Confirmed);
        if (committedBooking is not null)
        {
          _logger.LogInformation("Reconciled existing confirmed booking {Reference} for User {UserId}, Showtime {ShowtimeId}",
            committedBooking.BookingRefrence, userId, dto.ShowtimeId);
          if (payment is not null)
          {
            payment.BookingReference = committedBooking.BookingRefrence;
            payment.Status = PaymentStatus.Completed;
            payment.CompletedAt ??= DateTime.UtcNow;
          }
          return BookingResultDto.Success(committedBooking.BookingRefrence, committedBooking.TotalAmount);
        }

        _logger.LogWarning("Booking conflict: seats already booked. Conflicts={Conflicts}",
          conflict.Select(c => $"R{c.Row}S{c.SeatNumber}"));
        return BookingResultDto.Conflict(conflict.Select(c => new SeatDto { Row = c.Row, SeatNumber = c.SeatNumber }).ToList());
      }

      var template = HallTypeHelper.GetTemplate(showtime.Hall.Type);

      var bookedSeats = dto.Seats.Select(s =>
      {
        var category = template.RowCategoryMap[s.Row];
        decimal multiplier = HallTypeHelper.GetPriceMultiplier(category);

        return new BookedSeat
        {
          ShowtimeId = dto.ShowtimeId,
          Row = s.Row,
          SeatNumber = s.SeatNumber,
          Category = category,
          Price = showtime.Price * multiplier
        };
      }).ToList();

      var bookingRef = payment?.BookingReference ?? GenerateRefrence();

      var booking = new Booking
      {
        UserId = userId,
        ShowtimeId = dto.ShowtimeId,
        BookedAt = DateTime.UtcNow,
        TotalAmount = bookedSeats.Sum(s => s.Price),
        Status = Core.Enums.BookingStatus.Confirmed,
        BookingRefrence = bookingRef,
        BookedSeats = bookedSeats
      };

      await _uow.Bookings.CreateAsync(booking);

      var originalPaymentStatus = payment?.Status;
      var originalPaymentRef = payment?.BookingReference;
      var originalCompletedAt = payment?.CompletedAt;

      if (payment is not null)
      {
        payment.BookingReference = bookingRef;
        payment.Status = PaymentStatus.Completed;
        payment.CompletedAt = DateTime.UtcNow;
      }

      var existingBookedCount = await _uow.BookedSeats.CountAsync(
          new BookedSeatByShowtimeIdSpecification(dto.ShowtimeId));
      var totalSeats = template.VisibleSeatCount;

      if (existingBookedCount + dto.Seats.Count >= totalSeats)
      {
        showtime.Status = ShowtimeStatus.SoldOut;
        await _uow.Showtimes.UpdateAsync(showtime);
      }

      try
      {
        // Single atomic transaction persists: Booking + BookedSeats + Showtime status (+ Payment completion if present)
        await _uow.SaveAsync();
      }
      catch (DbUpdateException ex)
      {
        _logger.LogWarning(ex, "Booking save conflict");

        // Revert in-memory payment mutations since this local save failed
        if (payment is not null)
        {
          payment.Status = originalPaymentStatus ?? PaymentStatus.Pending;
          payment.BookingReference = originalPaymentRef;
          payment.CompletedAt = originalCompletedAt;
        }

        // Concurrent confirmation convergence: Did a competing request successfully commit the booking?
        var committedBooking = await _uow.Bookings.GetAsync(b => b.UserId == userId && b.ShowtimeId == dto.ShowtimeId && b.Status == BookingStatus.Confirmed);
        if (committedBooking is not null)
        {
          _logger.LogInformation("Concurrent request successfully committed booking {Reference} for User {UserId}, Showtime {ShowtimeId}",
            committedBooking.BookingRefrence, userId, dto.ShowtimeId);
          if (payment is not null)
          {
            payment.BookingReference = committedBooking.BookingRefrence;
            payment.Status = PaymentStatus.Completed;
            payment.CompletedAt = DateTime.UtcNow;
          }
          return BookingResultDto.Success(committedBooking.BookingRefrence, committedBooking.TotalAmount);
        }

        var lateConflict = await _uow.BookedSeats
          .GetConflictAsync(dto.ShowtimeId, dto.Seats, ct);

        return BookingResultDto.Conflict(lateConflict.Select(c => new SeatDto { Row = c.Row, SeatNumber = c.SeatNumber }).ToList());
      }

      _logger.LogInformation("Booking successful: Reference={Ref}", booking.BookingRefrence);
      return BookingResultDto.Success(booking.BookingRefrence, booking.TotalAmount);
    }
    public async Task<BookingDetailsDto?> GetByReferenceAsync(string reference, CancellationToken ct = default)
    {
      var booking = await _uow.Bookings.GetBookingByRefrenceAsync(reference, ct);
      return booking is null ? null : new BookingDetailsDto
      {
        UserId = booking.UserId,
        UserEmail = booking.User.UserName!,
        CustomerEmail = booking.User.Email!,
        CustomerFirstName = booking.User.FirstName,
        Status = booking.Status,
        BookedAt = timeConversions.EnsureUtcKind(booking.BookedAt),
        TotalAmount = booking.TotalAmount,
        MovieTitle = booking.Showtime.Movie.Title,
        MoviePosterPath = booking.Showtime.Movie.PosterPath,
        MovieBackdropPath = booking.Showtime.Movie.BackdropPath,
        StartsAt = timeConversions.EnsureUtcKind(booking.Showtime.StartTime),
        HallName = booking.Showtime.Hall.Name,
        HallType = booking.Showtime.Hall.Type.ToString(),
        Seats = booking.BookedSeats.Select(s => new BookingDetailsSeatDto
        {
          Row = s.Row,
          SeatNumber = s.SeatNumber,
          Category = s.Category.ToString(),
          Price = s.Price
        }).ToList(),
      };
    }

    public async Task<(bool Success, string Message)> CancelBookingsForPaymentAsync(int showtimeId, IEnumerable<PaymentSeat> paymentSeats)
    {
      var showtimeSpec = new ShowtimeByIdSpecification(showtimeId);
      var showtime = await _uow.Showtimes.GetEntityWithSpecAsync(showtimeSpec);

      if (showtime is null)
        return (false, "Showtime not found.");

      var paymentSeatList = paymentSeats.ToList();
      if (paymentSeatList.Count == 0)
        return (false, "No seats associated with this payment.");

      var seatPairs = paymentSeatList.Select(s => (s.Row, s.SeatNumber)).ToList();

      var allBookedSeats = (await _uow.BookedSeats.GetByShowtimeIdAsync(showtimeId)).ToList();

      var matchedSeats = allBookedSeats
          .Where(bs => seatPairs.Any(sp => sp.Row == bs.Row && sp.SeatNumber == bs.SeatNumber))
          .ToList();

      if (matchedSeats.Count == 0)
        return (false, "No matching booked seats found for this payment.");

      var matchedBookingIds = matchedSeats.Select(s => s.BookingId).Distinct().ToList();
      var bookings = new List<Booking>();
      foreach (var id in matchedBookingIds)
      {
        var b = await _uow.Bookings.GetAsync(b => b.Id == id);
        if (b is not null) bookings.Add(b);
      }

      foreach (var seat in matchedSeats)
      {
        _uow.BookedSeats.Delete(seat);
      }

      foreach (var booking in bookings)
      {
        var remainingForBooking = allBookedSeats
            .Count(bs => bs.BookingId == booking.Id && !matchedSeats.Contains(bs));
        if (remainingForBooking == 0)
        {
          booking.Status = BookingStatus.Cancelled;
        }
      }

      await _uow.SaveAsync();

      if (showtime.Status == ShowtimeStatus.SoldOut)
      {
        var template = HallTypeHelper.GetTemplate(showtime.Hall.Type);
        var remainingSeatsCount = await _uow.BookedSeats.CountAsync(
            new BookedSeatByShowtimeIdSpecification(showtimeId));

        if (remainingSeatsCount < template.VisibleSeatCount)
        {
          showtime.Status = ShowtimeStatus.Scheduled;
        }

        await _uow.SaveAsync();
      }

      return (true, "Bookings cancelled successfully.");
    }

    private static string GenerateRefrence() => $"TKT-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

  }
}
