using System.Collections.Concurrent;

public interface IMetricsCollector
{
    void Increment(string key);
    void AddLatency(TimeSpan latency);
    object GetMetricsSnapshot();
}

public class InMemoryMetricsCollector : IMetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _counts = new();
    private readonly ConcurrentQueue<double> _latencies = new();
    private const int MaxSamples = 10000;

    public void Increment(string key) => _counts.AddOrUpdate(key, 1, (_, v) => v + 1);

    public void AddLatency(TimeSpan latency)
    {
        if (_latencies.Count >= MaxSamples) return;
        _latencies.Enqueue(latency.TotalMilliseconds);
    }

    public object GetMetricsSnapshot()
    {
        _counts.TryGetValue("requests.total", out var total);
        _counts.TryGetValue("requests.success", out var success);
        _counts.TryGetValue("requests.failed", out var failed);
        _counts.TryGetValue("requests.retried", out var retried);

        var arr = _latencies.ToArray();
        Array.Sort(arr);
        var avg = arr.Length == 0 ? 0.0 : arr.Average();
        var p95 = arr.Length == 0 ? 0.0 : arr[(int)Math.Floor(0.95 * (arr.Length - 1))];

        return new { total, success, failed, retried, avgLatencyMs = avg, p95LatencyMs = p95 };
    }
}
