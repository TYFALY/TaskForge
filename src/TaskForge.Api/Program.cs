using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
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

// Get the actual executable directory - more reliable for single-file executables
string baseDir;
try
{
    // Try to get from the main module first (more reliable for single-file)
    var mainModule = Process.GetCurrentProcess().MainModule;
    baseDir = Path.GetDirectoryName(mainModule?.FileName) ?? AppContext.BaseDirectory;
}
catch
{
    baseDir = AppContext.BaseDirectory;
}

// Check if we are running as a single-file executable with embedded wwwroot
var potentialWwwroot = Path.Combine(baseDir, "wwwroot");
var wwwrootExists = Directory.Exists(potentialWwwroot);

Console.WriteLine($"[Startup] Base directory: {baseDir}");
Console.WriteLine($"[Startup] Potential wwwroot: {potentialWwwroot}");
Console.WriteLine($"[Startup] wwwroot exists: {wwwrootExists}");

// Set ContentRootPath and WebRootPath
builder.Environment.ContentRootPath = baseDir;
builder.Environment.WebRootPath = potentialWwwroot;

builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Any, port));

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

// Register the job event broadcaster (SSE)
builder.Services.AddSingleton<IJobEventBroadcaster, JobEventBroadcaster>();

if (isEmbedded)
{
    var dbPath = builder.Configuration["Embedded:DatabasePath"] ?? "taskforge.db";
    // Use absolute path for database
    if (!Path.IsPathRooted(dbPath))
    {
        dbPath = Path.Combine(baseDir, dbPath);
    }
    builder.Services.AddSingleton<IEmbeddedQueueService, EmbeddedQueueService>();
    builder.Services.AddSingleton<IEmbeddedJobRepository>(sp =>
        new EmbeddedJobRepository(dbPath, sp.GetRequiredService<ILogger<EmbeddedJobRepository>>()));
    builder.Services.AddSingleton<IJobBufferService, JobBufferService>();
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
}

builder.Services.AddSingleton<IWorkflowStore, InMemoryWorkflowStore>();
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

// Configure CORS for development/testing
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
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

// Use CORS early
app.UseCors();

// Health check endpoint (before static files to bypass SPA routing)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow, mode = isEmbedded ? "embedded" : "distributed" }));

// Configure static file serving using PhysicalFileProvider for explicit path control
if (wwwrootExists)
{
    var fileCount = Directory.GetFiles(potentialWwwroot, "*", SearchOption.AllDirectories).Length;
    logger.LogInformation("Serving static UI from: {Path} ({FileCount} files)", potentialWwwroot, fileCount);
    
    // Use PhysicalFileProvider for explicit path control
    var fileProvider = new PhysicalFileProvider(potentialWwwroot);
    
    // Serve default files (index.html)
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        DefaultFileNames = new[] { "index.html" },
        FileProvider = fileProvider
    });
    
    // Serve static files with explicit provider
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

// Root redirect to index.html
app.MapGet("/", () => Results.Redirect("/index.html")).ExcludeFromDescription();

// Map all API endpoints
app.MapJobsEndpoints();
app.MapJobEventsEndpoints(); // Register SSE endpoint for job events
app.MapMetricsEndpoints();
app.MapWorkflowEndpoints();

// SPA fallback - must be LAST to catch all non-API routes
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