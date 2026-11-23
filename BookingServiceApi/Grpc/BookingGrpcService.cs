using System.Globalization;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using RestaurantBooking.Grpc;

namespace BookingServiceApi;

/// <summary>gRPC server: tells AvailabilityService when the tables are already booked.</summary>
public class BookingGrpcService : BookingGrpc.BookingGrpcBase
{
    private readonly BookingDbContext _db;

    public BookingGrpcService(BookingDbContext db) => _db = db;

    public override async Task<BookedSlotsReply> GetBookedSlots(
        GetBookedSlotsRequest request, ServerCallContext context)
    {
        if (!DateTime.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "date must be yyyy-MM-dd"));

        var tableIds = new List<Guid>();
        foreach (var raw in request.TableIds)
        {
            if (Guid.TryParse(raw, out var id))
                tableIds.Add(id);
        }

        var reply = new BookedSlotsReply();
        if (tableIds.Count == 0)
            return reply;

        var dayEnd = date.AddDays(1);
        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => tableIds.Contains(b.TableId)
                        && b.StartTime >= date && b.StartTime < dayEnd
                        && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
            .Select(b => new { b.TableId, b.StartTime })
            .ToListAsync(context.CancellationToken);

        foreach (var booking in bookings)
        {
            reply.Slots.Add(new BookedSlot
            {
                TableId = booking.TableId.ToString(),
                StartTime = booking.StartTime.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
            });
        }
        return reply;
    }
}
