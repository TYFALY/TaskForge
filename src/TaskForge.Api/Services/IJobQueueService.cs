using TaskForge.Core;

namespace TaskForge.Api.Services;

public interface IJobQueueService
{
    Task<bool> PushJobAsync(string queueName, JobEnvelope job);
    Task<long> GetQueueSizeAsync(string queueName);
    Task<int> GetActiveWorkerCountAsync();
    bool IsConnected { get; }
    bool IsEmbeddedMode { get; }
}

public class EmbeddedQueueAdapter : IJobQueueService
{
    private readonly Embedded.IEmbeddedQueueService _embedded;

    public EmbeddedQueueAdapter(Embedded.IEmbeddedQueueService embedded) => _embedded = embedded;

    public Task<bool> PushJobAsync(string queueName, JobEnvelope job) => _embedded.PushJobAsync(queueName, job);
    public Task<long> GetQueueSizeAsync(string queueName) => _embedded.GetQueueSizeAsync(queueName);
    public Task<int> GetActiveWorkerCountAsync() => _embedded.GetActiveWorkerCountAsync();
    public bool IsConnected => true;
    public bool IsEmbeddedMode => _embedded.IsEmbeddedMode;
}
