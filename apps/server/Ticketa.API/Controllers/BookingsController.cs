using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Ticketa.Core.DTOs;
using Ticketa.Core.Interfaces.IRepositories;

namespace Ticketa.API.Controllers
{
  [Route("api/[controller]")]
  [ApiController]
  [Authorize]
  public class BookingsController(IBookingService bookingService) : ControllerBase
  {
    private readonly IBookingService _bookingService = bookingService;

    [HttpPost]
    public async Task<IActionResult> Book(BookingCreateDto dto, CancellationToken ct)
    {
      if (!ModelState.IsValid) return ValidationProblem(ModelState);

      var userId = User.FindFirstValue("uid")!;
      var result = await _bookingService.CreateAsync(dto, userId, ct);

      if (!result.Succeeded)
        return Conflict(new { message = $"{result.ConflictingSeats.Count} seat(s) already booked.", conflictingSeats = result.ConflictingSeats });

      return Ok(new
      {
        bookingReference = result.BookingReference,
        totalAmount = result.TotalAmount
      });
    }

    [HttpGet("{reference}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByReference(string reference, CancellationToken ct)
    {
      var result = await _bookingService.GetByReferenceAsync(reference, ct);
      return result is null ? NotFound() : Ok(result);
    }
  }
}