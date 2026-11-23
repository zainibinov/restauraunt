using Grpc.Core;
using RestaurantBooking.Grpc;

namespace AuthServiceApi;

/// <summary>gRPC server: gives other services the contact data of a user.</summary>
public class AuthGrpcService : AuthGrpc.AuthGrpcBase
{
    private readonly UserService _users;

    public AuthGrpcService(UserService users) => _users = users;

    public override async Task<UserContactInfo> GetUserContact(
        GetUserContactRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id is not a valid GUID"));

        var contact = await _users.GetContactAsync(userId, context.CancellationToken);
        if (contact is null)
            throw new RpcException(new Status(StatusCode.NotFound, "User not found"));

        return new UserContactInfo
        {
            UserId = contact.UserId.ToString(),
            Email = contact.Email,
            Phone = contact.Phone,
            FullName = contact.FullName
        };
    }
}
