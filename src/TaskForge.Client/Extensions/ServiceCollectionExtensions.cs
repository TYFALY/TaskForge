using Microsoft.Extensions.DependencyInjection;

namespace TaskForge.Client.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers TaskForge strongly-typed client with HttpClientFactory.
    /// </summary>
    public static IHttpClientBuilder AddTaskForgeClient(
        this IServiceCollection services,
        Action<TaskForgeClientOptions>? configure = null)
    {
        var options = new TaskForgeClientOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        return services.AddHttpClient<ITaskForgeClient, TaskForgeClient>((sp, client) =>
        {
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
            }
        });
    }
}
