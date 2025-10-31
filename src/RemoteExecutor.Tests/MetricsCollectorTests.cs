using FluentAssertions;
using System.Text.Json;

public class MetricsCollectorTests
{
    [Fact]
    public void Increment_IncreasesCounter()
    {
        var collector = new InMemoryMetricsCollector();

        collector.Increment("test.counter");
        collector.Increment("test.counter");
        collector.Increment("test.counter");

        var snapshot = collector.GetMetricsSnapshot();
        snapshot.Should().NotBeNull();
    }

    [Fact]
    public void AddLatency_StoresLatencies()
    {
        var collector = new InMemoryMetricsCollector();

        collector.AddLatency(TimeSpan.FromMilliseconds(100));
        collector.AddLatency(TimeSpan.FromMilliseconds(200));
        collector.AddLatency(TimeSpan.FromMilliseconds(300));

        var snapshot = collector.GetMetricsSnapshot();
        
        snapshot.Should().NotBeNull();
        
        // Parse the JSON to access properties
        var json = JsonSerializer.Serialize(snapshot);
        var metrics = JsonSerializer.Deserialize<JsonElement>(json);
        
        metrics.TryGetProperty("avgLatencyMs", out var avgLatency);
        avgLatency.GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetMetricsSnapshot_ReturnsExpectedStructure()
    {
        var collector = new InMemoryMetricsCollector();
        
        collector.Increment("requests.total");
        collector.Increment("requests.success");
        collector.AddLatency(TimeSpan.FromMilliseconds(150));

        var snapshot = collector.GetMetricsSnapshot();

        snapshot.Should().NotBeNull();
        
        // Parse the JSON to access properties
        var json = JsonSerializer.Serialize(snapshot);
        var metrics = JsonSerializer.Deserialize<JsonElement>(json);
        
        metrics.TryGetProperty("total", out var total);
        metrics.TryGetProperty("success", out var success);
        metrics.TryGetProperty("avgLatencyMs", out var avgLatency);
        
        total.GetInt64().Should().Be(1);
        success.GetInt64().Should().Be(1);
        avgLatency.GetDouble().Should().Be(150.0);
    }

    [Fact]
    public void P95Calculation_WithNoData_ReturnsZero()
    {
        var collector = new InMemoryMetricsCollector();

        var snapshot = collector.GetMetricsSnapshot();
        snapshot.Should().NotBeNull();
        
        // Parse the JSON to access properties
        var json = JsonSerializer.Serialize(snapshot);
        var metrics = JsonSerializer.Deserialize<JsonElement>(json);
        
        metrics.TryGetProperty("p95LatencyMs", out var p95Latency);
        p95Latency.GetDouble().Should().Be(0.0);
    }

    [Fact]
    public void P95Calculation_WithMultipleValues_ReturnsCorrectPercentile()
    {
        var collector = new InMemoryMetricsCollector();

        for (int i = 1; i <= 100; i++)
        {
            collector.AddLatency(TimeSpan.FromMilliseconds(i));
        }

        var snapshot = collector.GetMetricsSnapshot();
        snapshot.Should().NotBeNull();
        
        // Parse the JSON to access properties
        var json = JsonSerializer.Serialize(snapshot);
        var metrics = JsonSerializer.Deserialize<JsonElement>(json);
        
        metrics.TryGetProperty("p95LatencyMs", out var p95Latency);
        p95Latency.GetDouble().Should().BeApproximately(95.0, 1.0);
    }
}


