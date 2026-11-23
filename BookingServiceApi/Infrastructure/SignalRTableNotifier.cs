using System.Globalization;
using Microsoft.AspNetCore.SignalR;

namespace BookingServiceApi;

/// <summary>The real ITableNotifier: pushes the changes to the browsers over WebSocket (SignalR).</summary>
public class SignalRTableNotifier : ITableNotifier
{
    private readonly IHubContext<BookingHub> _hub;

    public SignalRTableNotifier(IHubContext<BookingHub> hub) => _hub = hub;

    private static string Fmt(DateTime slot) => slot.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    public Task TableLockedAsync(Guid tableId, DateTime slotTime, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("TableLocked", new { tableId, slotTime = Fmt(slotTime) }, ct);

    public Task TableReleasedAsync(Guid tableId, DateTime slotTime, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("TableReleased", new { tableId, slotTime = Fmt(slotTime) }, ct);

    public Task BookingStatusChangedAsync(Guid bookingId, BookingStatus status, CancellationToken ct = default)
        => _hub.Clients.Group(BookingHub.GroupName(bookingId))
            .SendAsync("BookingStatusChanged", new { bookingId, status = status.ToString() }, ct);
}
