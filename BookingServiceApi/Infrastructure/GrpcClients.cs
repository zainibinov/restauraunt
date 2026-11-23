using System.Globalization;
using Grpc.Core;

namespace BookingServiceApi;

// Real implementations of IAuthClient and IAvailabilityClient: they call the other services over gRPC.

public class GrpcAuthClient : IAuthClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly RestaurantBooking.Grpc.AuthGrpc.AuthGrpcClient _client;

    public GrpcAuthClient(RestaurantBooking.Grpc.AuthGrpc.AuthGrpcClient client) => _client = client;

    public async Task<UserContact?> GetUserContactAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var reply = await _client.GetUserContactAsync(
                new RestaurantBooking.Grpc.GetUserContactRequest { UserId = userId.ToString() },
                deadline: DateTime.UtcNow.Add(Timeout),
                cancellationToken: ct);

            return new UserContact(Guid.Parse(reply.UserId), reply.FullName, reply.Phone, reply.Email);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null; // the user does not exist
        }
        catch (RpcException)
        {
            throw new ServiceException(503, "AuthService is unavailable");
        }
    }
}

public class GrpcAvailabilityClient : IAvailabilityClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly RestaurantBooking.Grpc.AvailabilityGrpc.AvailabilityGrpcClient _client;

    public GrpcAvailabilityClient(RestaurantBooking.Grpc.AvailabilityGrpc.AvailabilityGrpcClient client) => _client = client;

    public async Task<TableInfo?> GetTableInfoAsync(Guid tableId, CancellationToken ct = default)
    {
        try
        {
            var reply = await _client.GetTableInfoAsync(
                new RestaurantBooking.Grpc.GetTableInfoRequest { TableId = tableId.ToString() },
                deadline: DateTime.UtcNow.Add(Timeout),
                cancellationToken: ct);

            return new TableInfo(Guid.Parse(reply.TableId), Guid.Parse(reply.HallId), reply.Capacity, reply.TableType);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null; // the table does not exist
        }
        catch (RpcException)
        {
            throw new ServiceException(503, "AvailabilityService is unavailable");
        }
    }

    public async Task<BookingParameters> GetBookingParametersAsync(DateTime date, CancellationToken ct = default)
    {
        try
        {
            var reply = await _client.GetBookingParametersAsync(
                new RestaurantBooking.Grpc.GetBookingParametersRequest { Date = FormatDate(date) },
                deadline: DateTime.UtcNow.Add(Timeout),
                cancellationToken: ct);

            return new BookingParameters(reply.SlotStepMinutes, reply.DurationMinutes, reply.CleaningBufferMinutes);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new ServiceException(409, "The restaurant does not work on this day");
        }
        catch (RpcException)
        {
            throw new ServiceException(503, "AvailabilityService is unavailable");
        }
    }

    public async Task<bool> IsTableOpenAsync(Guid tableId, DateTime startTime, DateTime endTime,
        CancellationToken ct = default)
    {
        // AvailabilityService already takes the stay duration and the closing time into account
        try
        {
            var reply = await _client.GetTableAvailabilityAsync(
                new RestaurantBooking.Grpc.GetTableAvailabilityRequest
                {
                    TableId = tableId.ToString(),
                    Date = FormatDate(startTime),
                    Time = startTime.ToString("HH:mm", CultureInfo.InvariantCulture)
                },
                deadline: DateTime.UtcNow.Add(Timeout),
                cancellationToken: ct);

            return reply.IsAvailable;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
        catch (RpcException)
        {
            throw new ServiceException(503, "AvailabilityService is unavailable");
        }
    }

    private static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}