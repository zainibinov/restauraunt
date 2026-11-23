using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingServiceApi;

[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingController : ControllerBase
{
    private readonly BookingService _bookings;

    public BookingController(BookingService bookings) => _bookings = bookings;

    private bool IsStaff => User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Waiter);

    /// <summary>201 = a new booking, 200 = a repeated request with the same idempotencyKey (the same booking).</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Guest)]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var result = await _bookings.CreateAsync(User.GetUserId(), request, ct);
        if (result.IsNew)
            return StatusCode(StatusCodes.Status201Created, result.Booking);
        return Ok(result.Booking);
    }

    [HttpGet]
    public async Task<ActionResult<List<BookingResponse>>> GetMy(CancellationToken ct)
        => Ok(await _bookings.GetMyAsync(User.GetUserId(), ct));

    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult<BookingResponse>> GetById(Guid bookingId, CancellationToken ct)
        => Ok(await _bookings.GetByIdAsync(bookingId, User.GetUserId(), ct));

    [HttpPut("{bookingId:guid}")]
    [Authorize(Roles = $"{RoleNames.Guest},{RoleNames.Admin},{RoleNames.Waiter}")]
    public async Task<ActionResult<BookingResponse>> Update(
        Guid bookingId, UpdateBookingRequest request, CancellationToken ct)
        => Ok(await _bookings.UpdateAsync(bookingId, User.GetUserId(), IsStaff, request, ct));

    [HttpPost("{bookingId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid bookingId, CancellationToken ct)
    {
        await _bookings.CancelAsync(bookingId, User.GetUserId(), IsStaff, ct);
        return NoContent();
    }

    [HttpPost("{bookingId:guid}/confirm")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Waiter}")]
    public async Task<ActionResult<BookingResponse>> Confirm(Guid bookingId, CancellationToken ct)
        => Ok(await _bookings.ConfirmAsync(bookingId, User.GetUserId(), ct));

    [HttpPost("{bookingId:guid}/complete")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Waiter}")]
    public async Task<ActionResult<BookingResponse>> Complete(Guid bookingId, CancellationToken ct)
        => Ok(await _bookings.CompleteAsync(bookingId, User.GetUserId(), ct));

    [HttpPost("{bookingId:guid}/no-show")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Waiter}")]
    public async Task<ActionResult<BookingResponse>> NoShow(Guid bookingId, CancellationToken ct)
        => Ok(await _bookings.NoShowAsync(bookingId, User.GetUserId(), ct));
}
