using System.Net.Http.Json;
using System.Text.Json;
using TaskForge.Client.Models;

namespace TaskForge.Client;

public class TaskForgeClient : ITaskForgeClient
{
    private readonly HttpClient _httpClient;
    private readonly TaskForgeClientOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TaskForgeClient(HttpClient httpClient, TaskForgeClientOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new TaskForgeClientOptions();

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = _options.BaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey) &&
            !_httpClient.DefaultRequestHeaders.Contains("X-Api-Key"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey);
        }
    }

    public async Task<JobResponse> EnqueueAsync(
        string queueName,
        string payload,
        int maxRetries = 3,
        string? tenantId = null,
        string? jobNamespace = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EnqueueJobRequest
        {
            QueueName = queueName,
            Payload = payload,
            MaxRetries = maxRetries,
            TenantId = tenantId,
            Namespace = jobNamespace ?? _options.DefaultNamespace
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/v1/jobs/enqueue",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JobResponse>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response received from TaskForge server.");
    }

    public Task<JobResponse> EnqueueAsync<T>(
        string queueName,
        T payload,
        int maxRetries = 3,
        string? tenantId = null,
        string? jobNamespace = null,
        CancellationToken cancellationToken = default)
    {
        var serialized = JsonSerializer.Serialize(payload, JsonOptions);
        return EnqueueAsync(queueName, serialized, maxRetries, tenantId, jobNamespace, cancellationToken);
    }

    public async Task<JobResponse> EnqueueWebhookAsync(
        string targetUrl,
        string method = "POST",
        string body = "",
        Dictionary<string, string>? headers = null,
        string queueName = "webhooks",
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var request = new EnqueueWebhookRequest
        {
            QueueName = queueName,
            TargetUrl = targetUrl,
            Method = method,
            Body = body,
            Headers = headers ?? new Dictionary<string, string>(),
            MaxRetries = maxRetries,
            Namespace = _options.DefaultNamespace
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/v1/jobs/webhook",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JobResponse>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response received from TaskForge server.");
    }

    public async Task<JobResponse?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/v1/jobs/{jobId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobResponse>(JsonOptions, cancellationToken);
    }

    public async Task<bool> RetryJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/v1/jobs/{jobId}/retry", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/v1/jobs/{jobId}/cancel", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<int> ReplayAllDlqAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/v1/jobs/dlq/replay-all", null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (doc.RootElement.TryGetProperty("replayedCount", out var countProp))
        {
            return countProp.GetInt32();
        }

        return 0;
    }

    public async Task<JobResponse> WaitForCompletionAsync(
        Guid jobId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        var maxWait = timeout ?? TimeSpan.FromMinutes(2);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(maxWait);

        while (!timeoutCts.Token.IsCancellationRequested)
        {
            var job = await GetJobStatusAsync(jobId, timeoutCts.Token);
            if (job != null && job.IsTerminal)
            {
                return job;
            }

            await Task.Delay(interval, timeoutCts.Token);
        }

        throw new TimeoutException($"Job {jobId} did not reach a terminal state within {maxWait.TotalSeconds} seconds.");
    }
}
