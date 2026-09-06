using Npgsql;
using StackExchange.Redis;
using TaskForge.Worker;
using TaskForge.Worker.Handlers;
using TaskForge.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IJobHandler, DefaultJobHandler>();
builder.Services.AddHttpClient<WebhookJobHandler>();
builder.Services.AddSingleton<IJobHandler>(sp => sp.GetRequiredService<WebhookJobHandler>());

var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configOptions = ConfigurationOptions.Parse(redisConnectionString);
    configOptions.AbortOnConnectFail = false;
    configOptions.ConnectRetry = 3;
    configOptions.ConnectTimeout = 5000;
    return ConnectionMultiplexer.Connect(configOptions);
});

var pgConnectionString = builder.Configuration["PostgreSql:ConnectionString"]
    ?? "Host=localhost;Port=5432;Database=taskforge_db;Username=taskforge;Password=password";
builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(pgConnectionString));

builder.Services.AddSingleton<IPostgresJobRepository, PostgresJobRepositoryImpl>();
builder.Services.AddSingleton<IRedisQueueConsumer, RedisQueueConsumer>();

builder.Services.AddSingleton<WorkerHeartbeatService>();
builder.Services.AddSingleton<IWorkerHeartbeatService>(sp => sp.GetRequiredService<WorkerHeartbeatService>());
builder.Services.AddSingleton<CronSchedulerService>();

var maxRetries = int.Parse(builder.Configuration["Worker:MaxRetries"] ?? "3");
builder.Services.AddSingleton(sp => new JobProcessor(
    sp.GetRequiredService<IPostgresJobRepository>(),
    sp.GetRequiredService<IRedisQueueConsumer>(),
    sp.GetRequiredService<WorkerHeartbeatService>(),
    sp.GetRequiredService<IEnumerable<IJobHandler>>(),
    sp.GetRequiredService<ILogger<JobProcessor>>(),
    maxRetries
));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CronSchedulerService>());

var host = builder.Build();
host.Run();
