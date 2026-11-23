using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using BookingServiceApi;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RestaurantBooking.Grpc;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Two ports: 5003 = REST + Swagger (HTTP/1.1), 5013 = gRPC (HTTP/2) for AvailabilityService.
builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenLocalhost(5003, l => l.Protocols = HttpProtocols.Http1);
    o.ListenLocalhost(5013, l => l.Protocols = HttpProtocols.Http2);
});

// --- database ---
var connectionString = builder.Configuration.GetConnectionString("BookingDb")!;
builder.Services.AddDbContext<BookingDbContext>(o =>
    o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// --- settings ---
builder.Services.Configure<BookingSettings>(builder.Configuration.GetSection("Booking"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

// --- dependency injection ---
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

// AuthService and AvailabilityService: real gRPC clients, or the fakes for debugging
// ("Services:UseFakeClients": true in appsettings.json means the fakes).
if (builder.Configuration.GetValue<bool>("Services:UseFakeClients"))
{
    builder.Services.AddSingleton<IAuthClient, FakeAuthClient>();
    builder.Services.AddSingleton<IAvailabilityClient, FakeAvailabilityClient>();
}
else
{
    // fail at startup with a clear message if the addresses are missing in appsettings.json
    var authUrl = builder.Configuration["Grpc:AuthUrl"]
                  ?? throw new InvalidOperationException("Grpc:AuthUrl is not set in appsettings.json");
    var availabilityUrl = builder.Configuration["Grpc:AvailabilityUrl"]
                          ?? throw new InvalidOperationException("Grpc:AvailabilityUrl is not set in appsettings.json");

    builder.Services.AddGrpcClient<AuthGrpc.AuthGrpcClient>(o => o.Address = new Uri(authUrl));
    builder.Services.AddGrpcClient<AvailabilityGrpc.AvailabilityGrpcClient>(o => o.Address = new Uri(availabilityUrl));
    builder.Services.AddScoped<IAuthClient, GrpcAuthClient>();
    builder.Services.AddScoped<IAvailabilityClient, GrpcAvailabilityClient>();
}

// STAGE 1-4 fakes that are still here. Each line is replaced by the real one at the next steps:

// Redis: idempotency keys and slot locks. If "Redis:ConnectionString" is empty - the in-memory fakes (as before).
var redisConnection = builder.Configuration["Redis:ConnectionString"];
var useRedis = !string.IsNullOrWhiteSpace(redisConnection);
if (useRedis)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConnection!);
        options.AbortOnConnectFail = false; // the service starts even if Redis is still down and connects later
        return ConnectionMultiplexer.Connect(options);
    });
    builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
    builder.Services.AddSingleton<ITableLockStore, RedisTableLockStore>();
}
else
{
    builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
    builder.Services.AddSingleton<ITableLockStore, InMemoryTableLockStore>();
}

// RabbitMQ: the booking events go to the broker. If "RabbitMq:Host" is not set - they are only written to the log (as before).
var rabbitSettings = builder.Configuration.GetSection("RabbitMq").Get<RabbitMqSettings>();
var useRabbit = !string.IsNullOrWhiteSpace(builder.Configuration["RabbitMq:Host"]);
if (useRabbit)
{
    builder.Services.AddSingleton(rabbitSettings!);
    builder.Services.AddSingleton<IMessagePublisher>(new RabbitMqMessagePublisher(rabbitSettings!));
}
else
    builder.Services.AddSingleton<IMessagePublisher, LoggingMessagePublisher>();

// STAGE 1-4 fakes that are still here:

// SignalR: "the table is occupied/free" and "the booking status changed" go to the browsers over WebSocket.
builder.Services.AddSignalR();
builder.Services.AddSingleton<ITableNotifier, SignalRTableNotifier>();

builder.Services.AddScoped<BookingService>();

// the initializer must be registered BEFORE the worker: hosted services start in this order
builder.Services.AddHostedService<BookingDbInitializer>();
builder.Services.AddHostedService<BookingExpirationWorker>();
builder.Services.AddHostedService<BookingReminderWorker>();
if (useRabbit)
    builder.Services.AddHostedService<NotificationResultConsumer>(); // NotificationSent -> Confirmed

// --- JWT: validates tokens issued by AuthService (same Issuer, Audience and Key!) ---
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.Zero
        };

        // A browser cannot put the Authorization header on a WebSocket, so for the hub the token comes in the address (?access_token=...)
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// --- controllers + Swagger ---
builder.Services.AddControllers();
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken from AuthService (without the word Bearer)"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.Logger.LogInformation(useRedis
    ? "Idempotency keys and slot locks: Redis ({Redis})"
    : "Idempotency keys and slot locks: IN MEMORY (Redis:ConnectionString is not set)", redisConnection);
app.Logger.LogInformation(useRabbit
    ? "Events: RabbitMQ ({Host}:{Port}, exchange {Exchange})"
    : "Events: only written to the log (RabbitMq:Host is not set)",
    rabbitSettings?.Host, rabbitSettings?.Port, rabbitSettings?.Exchange);

app.UseMiddleware<ExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles(); // wwwroot/signalr-test.html
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGrpcService<BookingGrpcService>();
app.MapHub<BookingHub>("/hubs/bookings");

app.Run();
