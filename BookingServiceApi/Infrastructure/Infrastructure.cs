using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace BookingServiceApi;

// ---------- time (can be replaced in tests) ----------

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

// ---------- settings ----------

/// <summary>JWT settings (must match AuthService!)</summary>
public class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";
}

/// <summary>The "Booking" section of appsettings.json</summary>
public class BookingSettings
{
    /// <summary>A Pending booking that is not confirmed within this time is cancelled automatically.</summary>
    public int PendingTimeoutMinutes { get; set; } = 30;
    public int ReminderHoursBefore { get; set; } = 2;
}

public static class RoleNames
{
    public const string Guest = "Guest";
    public const string Admin = "Admin";
    public const string Waiter = "Waiter";
}

// ---------- errors ----------

public class ServiceException : Exception
{
    public int StatusCode { get; }
    public ServiceException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ServiceException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = ex.StatusCode,
                Title = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error"
            });
        }
    }
}

// ---------- helper: user id from the token ----------

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id)
            ? id
            : throw new ServiceException(401, "Invalid token");
    }
}
