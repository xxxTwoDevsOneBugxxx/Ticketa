using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;

namespace Ticketa.Core.Interfaces.IRepositories
{
  public interface IBookingService
  {
    Task<BookingResultDto> CreateAsync(BookingCreateDto dto, string userId, CancellationToken ct = default);
    Task<BookingResultDto> CreateForPaymentAsync(BookingCreateDto dto, string userId, Payment payment, CancellationToken ct = default);
    Task<BookingDetailsDto?> GetByReferenceAsync(string reference, CancellationToken ct = default);
    Task<(bool Success, string Message)> CancelBookingsForPaymentAsync(int showtimeId, IEnumerable<PaymentSeat> paymentSeats);
  }
}
