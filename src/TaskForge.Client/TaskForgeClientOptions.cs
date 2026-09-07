namespace TaskForge.Client;

public class TaskForgeClientOptions
{
    /// <summary>
    /// Base URL of the TaskForge API instance (e.g. "http://localhost:5000").
    /// </summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:5000");

    /// <summary>
    /// Optional API key for authenticating requests.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default HTTP request timeout (defaults to 30 seconds).
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default namespace for enqueued jobs.
    /// </summary>
    public string DefaultNamespace { get; set; } = "default";
}
