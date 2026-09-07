using System.Diagnostics;
using System.IO;
using System.Net;
using Microsoft.Extensions.FileProviders;
using Prometheus;
using TaskForge.Api.Endpoints;
using TaskForge.Api.Middleware;
using TaskForge.Api.Services;
using TaskForge.Api.Services.Embedded;
using TaskForge.Core.Workflows;

var builder = WebApplication.CreateBuilder(args);

var executionMode = builder.Configuration["ExecutionMode"] ?? "Embedded";
var isEmbedded = executionMode.Equals("Embedded", StringComparison.OrdinalIgnoreCase);

var port = int.Parse(builder.Configuration["Server:Port"] ?? "5000");
var autoOpenBrowser = builder.Configuration.GetValue("Server:AutoOpenBrowser", true);

string baseDir;
try
{
    var mainModule = Process.GetCurrentProcess().MainModule;
    baseDir = Path.GetDirectoryName(mainModule?.FileName) ?? AppContext.BaseDirectory;
}
catch
{
    baseDir = AppContext.BaseDirectory;
}

var potentialWwwroot = Path.Combine(baseDir, "wwwroot");
var wwwrootExists = Directory.Exists(potentialWwwroot);

Console.WriteLine($"[Startup] Base directory: {baseDir}");
Console.WriteLine($"[Startup] Potential wwwroot: {potentialWwwroot}");
Console.WriteLine($"[Startup] wwwroot exists: {wwwrootExists}");

builder.Environment.ContentRootPath = baseDir;
builder.Environment.WebRootPath = potentialWwwroot;

builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Any, port));

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
if (isEmbedded)
{
    var dbPath = builder.Configuration["Embedded:DatabasePath"] ?? "taskforge.db";
    if (!Path.IsPathRooted(dbPath))
    {
        dbPath = Path.Combine(baseDir, dbPath);
    }
    builder.Services.AddSingleton<IJobEventBroadcaster, JobEventBroadcaster>();
    builder.Services.AddSingleton<IEmbeddedQueueService, EmbeddedQueueService>();
    builder.Services.AddSingleton<IEmbeddedJobRepository>(sp =>
        new EmbeddedJobRepository(dbPath, sp.GetRequiredService<ILogger<EmbeddedJobRepository>>()));
    builder.Services.AddSingleton<IJobBufferService>(sp =>
        new JobBufferService(
            sp.GetRequiredService<ILogger<JobBufferService>>(),
            sp.GetRequiredService<IJobEventBroadcaster>(),
            sp.GetRequiredService<IEmbeddedJobRepository>()));
    builder.Services.AddSingleton<IJobQueueService, EmbeddedQueueAdapter>();
    builder.Services.AddSingleton<EmbeddedJobProcessorService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EmbeddedJobProcessorService>());
}
else
{
    var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
    {
        var config = StackExchange.Redis.ConfigurationOptions.Parse(redisConn);
        config.AbortOnConnectFail = false;
        config.ConnectRetry = 3;
        config.ConnectTimeout = 5000;
        return StackExchange.Redis.ConnectionMultiplexer.Connect(config);
    });
    builder.Services.AddSingleton<IJobBufferService, JobBufferService>();
    builder.Services.AddSingleton<IRedisQueueService, RedisQueueService>();
    builder.Services.AddSingleton<IWorkflowStore, InMemoryWorkflowStore>();
    builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
    builder.Services.AddSingleton<IIdempotencyService, IdempotencyService>();
    builder.Services.AddSingleton<IJobQueueService, RedisQueueServiceAdapter>();
    builder.Services.AddHostedService<BufferProcessorService>();
}

// CORS: explicit allow-list driven by configuration.
// Configure "Cors:AllowedOrigins" with a comma-separated list of origins.
// If empty, defaults to localhost dev origins in Development; denies all in Production.
var allowedOriginsRaw = builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty;
var allowedOrigins = allowedOriginsRaw
    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    allowedOrigins = new[] { "http://localhost:3000", "http://localhost:5173", "http://127.0.0.1:3000" };
}

builder.Services.AddHttpClient("WebhookClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            // Production default: deny all cross-origin requests (fail-closed).
            // Server-to-server requests (no Origin header) still work.
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("---------------------------------------------------------------");
logger.LogInformation("TaskForge API - Mode: {Mode} - Enterprise Edition", isEmbedded ? "EMBEDDED" : "DISTRIBUTED");
logger.LogInformation("---------------------------------------------------------------");

if (isEmbedded)
{
    logger.LogInformation("Using Embedded Queue (in-memory)");
    logger.LogInformation("Using Embedded Job Processor (1 worker active)");
    logger.LogInformation("Using SQLite database");
    var repo = app.Services.GetRequiredService<IEmbeddedJobRepository>();
    try { await repo.InitializeSchemaAsync(); }
    catch (Exception ex) { logger.LogWarning("DB init warning: {Message}", ex.Message); }
}
else
{
    logger.LogInformation("Using Redis Queue");
    logger.LogInformation("Using PostgreSQL");
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow, mode = isEmbedded ? "embedded" : "distributed" }));

if (wwwrootExists)
{
    var fileCount = Directory.GetFiles(potentialWwwroot, "*", SearchOption.AllDirectories).Length;
    logger.LogInformation("Serving static UI from: {Path} ({FileCount} files)", potentialWwwroot, fileCount);

    var fileProvider = new PhysicalFileProvider(potentialWwwroot);

    app.UseDefaultFiles(new DefaultFilesOptions
    {
        DefaultFileNames = new[] { "index.html" },
        FileProvider = fileProvider
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider,
        ServeUnknownFileTypes = false,
        DefaultContentType = "text/plain"
    });
}
else
{
    logger.LogWarning("Static UI not found at: {Path}", potentialWwwroot);
}

app.UseApiKeyAuth();

app.MapGet("/", () => Results.Redirect("/index.html")).ExcludeFromDescription();

app.MapJobsEndpoints();
app.MapJobEventsEndpoints();
app.MapMetricsEndpoints();
app.MapWorkflowEndpoints();

if (wwwrootExists)
{
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(potentialWwwroot),
        ServeUnknownFileTypes = false,
        DefaultContentType = "text/html"
    });
    logger.LogInformation("SPA fallback routing enabled for index.html");
}

app.UseHttpMetrics();
app.MapMetrics();

var appUrl = $"http://localhost:{port}";
logger.LogInformation("---------------------------------------------------------------");
logger.LogInformation("Server starting on: {Url}", appUrl);
logger.LogInformation("Prometheus metrics available at: {Url}/metrics", appUrl);
logger.LogInformation("Job SSE stream available at: {Url}/api/v1/jobs/stream", appUrl);
logger.LogInformation("---------------------------------------------------------------");

if (autoOpenBrowser && isEmbedded)
{
    await Task.Delay(500);
    try { Process.Start(new ProcessStartInfo { FileName = appUrl, UseShellExecute = true }); }
    catch (Exception ex) { logger.LogWarning("Could not auto-open browser: {Message}", ex.Message); }
}

app.Run();
