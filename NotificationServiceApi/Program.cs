using NotificationServiceApi;
using StackExchange.Redis;

// NotificationService has no HTTP API: it only listens to RabbitMQ and uses Redis.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<NotificationSettings>(builder.Configuration.GetSection("Notification"));

// --- Redis: the marks "this notification was already sent" ---
var redisConnection = builder.Configuration["Redis:ConnectionString"]
                      ?? throw new InvalidOperationException("Redis:ConnectionString is not set in appsettings.json");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<INotificationStateStore, RedisNotificationStateStore>();

// --- sending, retries, processing ---
builder.Services.AddSingleton<INotificationSender, LogNotificationSender>();
builder.Services.AddSingleton<RetryService>();
builder.Services.AddSingleton<RabbitMqEventPublisher>();
builder.Services.AddSingleton<NotificationProcessor>();

// --- the consumer of BookingService events ---
builder.Services.AddHostedService<BookingEventsConsumer>();

await builder.Build().RunAsync();
