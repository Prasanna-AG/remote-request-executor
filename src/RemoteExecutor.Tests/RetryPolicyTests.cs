using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

public class RetryPolicyTests
{
    [Fact]
    public async Task SucceedsAfterTransientFailures()
    {
        var policy = new RetryPolicy(3, 1, 10, 5000, 0.1, NullLogger.Instance);
        int call = 0;
        Func<int, CancellationToken, Task<ExecutionResult>> work = async (attempt, ct) =>
        {
            call++;
            if (call < 3) return ExecutionResult.FromError("NetErr", "fail", true);
            return ExecutionResult.FromHttp(200, new Dictionary<string, string>(), "ok");
        };

        var result = await policy.ExecuteAsync((a, ct) => work(a, ct), "test-req-1");
        result.IsSuccess.Should().BeTrue();
        result.FinalResult.Attempt.Should().Be(3);
        result.TotalAttempts.Should().Be(3);
        result.Attempts.Should().HaveCount(3);
    }

    [Fact]
    public async Task DoesNotRetryOnPermanentFailure()
    {
        var policy = new RetryPolicy(3, 1, 10, 5000, 0.1, NullLogger.Instance);
        Func<int, CancellationToken, Task<ExecutionResult>> work = (a, ct) => Task.FromResult(ExecutionResult.FromError("BadReq", "bad", false));
        
        var result = await policy.ExecuteAsync((a, ct) => work(a, ct), "test-req-2");
        
        result.IsSuccess.Should().BeFalse();
        result.FinalResult.Attempt.Should().Be(1);
        result.TotalAttempts.Should().Be(1);
        result.Attempts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReturnsAllAttemptSummaries()
    {
        var policy = new RetryPolicy(3, 1, 10, 5000, 0.1, NullLogger.Instance);
        int call = 0;
        Func<int, CancellationToken, Task<ExecutionResult>> work = async (attempt, ct) =>
        {
            call++;
            if (call < 3) return ExecutionResult.FromError("Transient", $"fail {call}", true);
            return ExecutionResult.FromHttp(200, new Dictionary<string, string>(), "success");
        };

        var result = await policy.ExecuteAsync((a, ct) => work(a, ct), "test-req-3");

        result.Attempts.Should().HaveCount(3);
        result.Attempts[0].IsTransientFailure.Should().BeTrue();
        result.Attempts[0].ErrorMessage.Should().Be("fail 1");
        result.Attempts[1].IsTransientFailure.Should().BeTrue();
        result.Attempts[1].ErrorMessage.Should().Be("fail 2");
        result.Attempts[2].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandlesTimeoutCorrectly()
    {
        var policy = new RetryPolicy(2, 1, 10, 100, 0.1, NullLogger.Instance); // 100ms timeout
        
        Func<int, CancellationToken, Task<ExecutionResult>> work = async (attempt, ct) =>
        {
            await Task.Delay(200, ct); // Exceed timeout
            return ExecutionResult.FromHttp(200, new Dictionary<string, string>(), "ok");
        };

        var result = await policy.ExecuteAsync((a, ct) => work(a, ct), "test-req-4");

        result.IsSuccess.Should().BeFalse();
        result.FinalResult.ErrorCode.Should().Be("Timeout");
        result.FinalResult.IsTransientFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AppliesExponentialBackoffWithJitter()
    {
        var policy = new RetryPolicy(3, 100, 1000, 5000, 0.25, NullLogger.Instance);
        var attemptTimes = new List<DateTime>();
        
        Func<int, CancellationToken, Task<ExecutionResult>> work = async (attempt, ct) =>
        {
            attemptTimes.Add(DateTime.UtcNow);
            return ExecutionResult.FromError("Transient", "fail", true);
        };

        var result = await policy.ExecuteAsync((a, ct) => work(a, ct), "test-req-5");

        // Verify increasing delays between attempts
        attemptTimes.Should().HaveCount(3);
        var delay1 = attemptTimes[1] - attemptTimes[0];
        var delay2 = attemptTimes[2] - attemptTimes[1];
        
        // First delay should be around 100ms + jitter (75-125ms)
        delay1.TotalMilliseconds.Should().BeInRange(75, 150);
        
        // Second delay should be around 200ms + jitter (150-250ms)
        delay2.TotalMilliseconds.Should().BeInRange(150, 300);
    }
}
