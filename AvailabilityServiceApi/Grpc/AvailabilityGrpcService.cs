using System.Globalization;
using Grpc.Core;
using RestaurantBooking.Grpc;

namespace AvailabilityServiceApi;

/// <summary>gRPC server: gives BookingService the data about tables and their availability.</summary>
public class AvailabilityGrpcService : AvailabilityGrpc.AvailabilityGrpcBase
{
    private readonly SlotService _slots;

    public AvailabilityGrpcService(SlotService slots) => _slots = slots;

    public override async Task<TableInfo> GetTableInfo(GetTableInfoRequest request, ServerCallContext context)
    {
        var tableId = ParseGuid(request.TableId, "table_id");

        try
        {
            var info = await _slots.GetTableInfoAsync(tableId, context.CancellationToken);
            return new TableInfo
            {
                TableId = info.TableId.ToString(),
                HallId = info.HallId.ToString(),
                Capacity = info.Capacity,
                TableType = info.TableType
            };
        }
        catch (ServiceException ex) when (ex.StatusCode == 404)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<TableAvailability> GetTableAvailability(
        GetTableAvailabilityRequest request, ServerCallContext context)
    {
        var tableId = ParseGuid(request.TableId, "table_id");
        var date = ParseDate(request.Date);

        TimeSpan? time = null;
        if (!string.IsNullOrWhiteSpace(request.Time))
        {
            if (!TimeSpan.TryParseExact(request.Time, @"hh\:mm", CultureInfo.InvariantCulture, out var parsed))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "time must be HH:mm"));
            time = parsed;
        }

        try
        {
            var availability = await _slots.GetTableAvailabilityAsync(tableId, date, context.CancellationToken);

            var reply = new TableAvailability
            {
                IsAvailable = time is TimeSpan t
                    ? availability.Slots.Any(s => s.SlotTime == t && s.IsAvailable) // a slot that does not exist = not available
                    : availability.Slots.Any(s => s.IsAvailable)
            };
            reply.OccupiedSlots.AddRange(availability.Slots
                .Where(s => !s.IsAvailable)
                .Select(s => s.SlotTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture)));
            return reply;
        }
        catch (ServiceException ex) when (ex.StatusCode == 404)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<BookingParametersInfo> GetBookingParameters(
        GetBookingParametersRequest request, ServerCallContext context)
    {
        var date = ParseDate(request.Date);

        var parameters = await _slots.GetBookingParametersAsync(date, context.CancellationToken);
        if (parameters is null)
            throw new RpcException(new Status(StatusCode.NotFound, "The restaurant does not work on this day"));

        return new BookingParametersInfo
        {
            SlotStepMinutes = parameters.SlotStepMinutes,
            DurationMinutes = parameters.DurationMinutes,
            CleaningBufferMinutes = parameters.CleaningBufferMinutes
        };
    }

    private static Guid ParseGuid(string value, string field)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"{field} is not a valid GUID"));

    private static DateTime ParseDate(string value)
        => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "date must be yyyy-MM-dd"));
}
