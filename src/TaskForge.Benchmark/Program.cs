using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskForge.Benchmark;

public record EnqueueJob(string QueueName, string Payload, int MaxRetries);

public record Result(
    int Total, int Success, int Failed, double TotalMs,
    double RPS, double Mean, double P50, double P95, double P99, double Min, double Max);

public static class Program
{
    const string URL = "http://localhost:5000/api/v1/jobs/enqueue";

    public static async Task Main(string[] args)
    {
        int total = args.Length > 0 && int.TryParse(args[0], out var t) ? t : 5000;
        int conc = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 100;
        string q = args.Length > 2 ? args[2] : "benchmark";

        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║     TaskForge High-Throughput Benchmark Tool        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
        Console.WriteLine($"\n  Target: {URL}");
        Console.WriteLine($"  Requests: {total:N0}  |  Concurrency: {conc}  |  Queue: {q}\n");

        using var client = new HttpClient { BaseAddress = new("http://localhost:5000"), Timeout = TimeSpan.FromSeconds(30) };
        await WarmUp(client);

        var lat = new List<double>(total);
        var lk = new object();
        int ok = 0, fail = 0;
        var sem = new SemaphoreSlim(conc, conc);
        var sw = Stopwatch.StartNew();

        Console.WriteLine("  Running benchmark...\n");

        var tasks = Enumerable.Range(0, total).Select(async i =>
        {
            await sem.WaitAsync();
            try
            {
                var ms = await Send(client, q, i);
                lock (lk) { lat.Add(ms); ok++; }
            }
            catch { lock (lk) { fail++; } }
            finally { sem.Release(); }
        }).ToList();

        for (int i = 0; i < total; i++)
        {
            await tasks[i];
            if ((i + 1) % 1000 == 0)
                Console.Write($"\r  {(double)(i + 1) / total * 100:F0}% complete");
        }
        sw.Stop();

        Console.WriteLine("\n\n  Results:\n");
        PrintResult(total, ok, fail, lat, sw.Elapsed.TotalMilliseconds);
    }

    static async Task WarmUp(HttpClient c)
    {
        try { await c.PostAsJsonAsync("/api/v1/jobs/enqueue", new EnqueueJob("warmup", "{}", 3)); await Task.Delay(100); }
        catch { }
    }

    static async Task<double> Send(HttpClient c, string q, int i)
    {
        var sw = Stopwatch.StartNew();
        var r = await c.PostAsJsonAsync("/api/v1/jobs/enqueue", new EnqueueJob(q, JsonSerializer.Serialize(new { i, t = DateTime.UtcNow }), 3));
        sw.Stop();
        if (!r.IsSuccessStatusCode) throw new Exception($"Failed: {r.StatusCode}");
        return sw.Elapsed.TotalMilliseconds;
    }

    static void PrintResult(int total, int ok, int fail, List<double> lat, double ms)
    {
        lat.Sort();
        int p95 = Math.Min((int)(lat.Count * 0.95), lat.Count - 1);
        int p99 = Math.Min((int)(lat.Count * 0.99), lat.Count - 1);

        Console.WriteLine($"  Total Time     : {ms / 1000.0:F2}s");
        Console.WriteLine($"  RPS            : {ok / (ms / 1000.0):F2}");
        Console.WriteLine($"  Success Rate   : {ok}/{total} ({ok * 100.0 / total:F2}%)");
        Console.WriteLine($"\n  Latency Stats:");
        Console.WriteLine($"    Mean         : {lat.Average():F2}ms");
        Console.WriteLine($"    Median (p50) : {lat[lat.Count / 2]:F2}ms");
        Console.WriteLine($"    p95          : {lat[p95]:F2}ms");
        Console.WriteLine($"    p99          : {lat[p99]:F2}ms");
        Console.WriteLine($"    Min          : {lat.First():F2}ms");
        Console.WriteLine($"    Max          : {lat.Last():F2}ms");
        if (fail > 0) Console.WriteLine($"\n  ⚠ {fail} requests failed!");
    }
}