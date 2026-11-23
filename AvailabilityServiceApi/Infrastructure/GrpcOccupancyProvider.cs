using System.Globalization;
using Grpc.Core;
using RestaurantBooking.Grpc;

namespace AvailabilityServiceApi;

/// <summary>
/// The real IOccupancyProvider: asks BookingService over gRPC which tables are booked.
/// If BookingService is unavailable, the slot lists still work (just without the bookings), a warning is logged.
/// </summary>
public class GrpcOccupancyProvider : IOccupancyProvider
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly BookingGrpc.BookingGrpcClient _client;
    private readonly ILogger<GrpcOccupancyProvider> _logger;

    public GrpcOccupancyProvider(BookingGrpc.BookingGrpcClient client, ILogger<GrpcOccupancyProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<BookingStart>> GetBookingStartsAsync(
        DateTime date, IReadOnlyCollection<Guid> tableIds, CancellationToken ct = default)
    {
        if (tableIds.Count == 0)
            return new List<BookingStart>();

        try
        {
            var request = new GetBookedSlotsRequest { Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            request.TableIds.AddRange(tableIds.Select(id => id.ToString()));

            var reply = await _client.GetBookedSlotsAsync(request,
                deadline: DateTime.UtcNow.Add(Timeout), cancellationToken: ct);

            return reply.Slots
                .Select(s => new BookingStart(
                    Guid.Parse(s.TableId),
                    DateTime.ParseExact(s.StartTime, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None)))
                .ToList();
        }
        catch (RpcException ex)
        {
            _logger.LogWarning("BookingService is unavailable ({Status}); showing slots without bookings", ex.StatusCode);
            return new List<BookingStart>();
        }
    }
}
