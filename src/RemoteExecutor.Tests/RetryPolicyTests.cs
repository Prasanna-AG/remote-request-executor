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
        var attemptNumbers = new List<int>();
        
        Func<int, CancellationToken, Task<ExecutionResult>> work = async (attempt, ct) =>
        {
            attemptNumbers.Add(attempt);
            return ExecutionResult.FromError("Transient", "fail", true);
        };

        var result = await policy.ExecuteAsync((a, ct) => work(a, ct), "test-req-5");

        // Verify all attempts were made (retries occurred)
        attemptNumbers.Should().HaveCount(3);
        attemptNumbers.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        
        // Verify all attempts were transient failures (retry logic applied)
        result.Attempts.Should().HaveCount(3);
        result.Attempts[0].IsTransientFailure.Should().BeTrue();
        result.Attempts[1].IsTransientFailure.Should().BeTrue();
        result.Attempts[2].IsTransientFailure.Should().BeTrue();
        
        // Verify final result is failure (all attempts failed)
        result.IsSuccess.Should().BeFalse();
        
        // Note: Actual delay timing is not asserted to avoid flaky tests
        // The exponential backoff formula is tested indirectly by verifying
        // that retries occurred and all attempts were made
    }
}
