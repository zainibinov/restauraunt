using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BookingServiceApi;

/// <summary>
/// The WebSocket hub for the web client (address: /hubs/bookings, the JWT is required).
/// Events from the server:
///   TableLocked(tableId, slotTime)            - a slot of a table became occupied   (to everybody)
///   TableReleased(tableId, slotTime)          - a slot of a table became free       (to everybody)
///   BookingStatusChanged(bookingId, status)   - only to those who subscribed to this booking
/// Methods the client can call: SubscribeBooking(bookingId), UnsubscribeBooking(bookingId).
/// </summary>
[Authorize]
public class BookingHub : Hub
{
    private readonly BookingDbContext _db;

    public BookingHub(BookingDbContext db) => _db = db;

    public static string GroupName(Guid bookingId) => $"booking-{bookingId}";

    /// <summary>A guest can follow only their own bookings; Admin and Waiter can follow any.</summary>
    public async Task SubscribeBooking(Guid bookingId)
    {
        var user = Context.User!;
        var userId = user.GetUserId();
        var isStaff = user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.Waiter);

        var allowed = await _db.Bookings.AnyAsync(b => b.Id == bookingId && (isStaff || b.UserId == userId));
        if (!allowed)
            throw new HubException("Booking not found");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(bookingId));
    }

    public Task UnsubscribeBooking(Guid bookingId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(bookingId));
}
