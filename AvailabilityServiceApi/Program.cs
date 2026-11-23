using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using AvailabilityServiceApi;

var builder = WebApplication.CreateBuilder(args);

// Two ports: 5002 = REST + Swagger (HTTP/1.1), 5012 = gRPC (HTTP/2).
// Without TLS, HTTP/2 needs its own endpoint.
builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenLocalhost(5002, l => l.Protocols = HttpProtocols.Http1);
    o.ListenLocalhost(5012, l => l.Protocols = HttpProtocols.Http2);
});

// --- database ---
var connectionString = builder.Configuration.GetConnectionString("CatalogDb")!;
builder.Services.AddDbContext<CatalogDbContext>(o =>
    o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// --- settings ---
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

// --- dependency injection ---
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
// Where the bookings come from: BookingService over gRPC (if "Grpc:BookingUrl" is set), otherwise nobody (empty stub).
var bookingUrl = builder.Configuration["Grpc:BookingUrl"];
if (!string.IsNullOrWhiteSpace(bookingUrl))
{
    builder.Services.AddGrpcClient<RestaurantBooking.Grpc.BookingGrpc.BookingGrpcClient>(o =>
        o.Address = new Uri(bookingUrl));
    builder.Services.AddScoped<IOccupancyProvider, GrpcOccupancyProvider>();
}
else
{
    builder.Services.AddSingleton<IOccupancyProvider, NullOccupancyProvider>();
}
builder.Services.AddScoped<HallService>();
builder.Services.AddScoped<TableService>();
builder.Services.AddScoped<WorkingHoursService>();
builder.Services.AddScoped<BlackoutDateService>();
builder.Services.AddScoped<SlotService>();

// the initializer must be registered BEFORE the worker: hosted services start in this order
builder.Services.AddHostedService<CatalogDbInitializer>();
builder.Services.AddHostedService<SlotGenerationWorker>();

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
    });
builder.Services.AddAuthorization();

// --- controllers + Swagger ---
builder.Services.AddControllers();
builder.Services.AddGrpc();

// The web page (served by BookingService, another port) calls this API from the browser, so CORS must allow it.
// Open for any origin: fine for a course project, the real protection is the JWT.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
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

app.UseCors();
app.UseMiddleware<ExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGrpcService<AvailabilityGrpcService>();

app.Run();
