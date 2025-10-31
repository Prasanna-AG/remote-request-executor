using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;

public class PowerShellExecutorTests
{
    private readonly PowerShellExecutor _executor;

    public PowerShellExecutorTests()
    {
        var config = Options.Create(new PowerShellExecutorConfiguration
        {
            AllowedCommands = new[] { "Get-Mailbox", "Get-User", "Get-Recipient" },
            SessionReuseEnabled = false,
            SessionTimeoutSeconds = 60
        });
        _executor = new PowerShellExecutor(NullLogger<PowerShellExecutor>.Instance, config);
    }

    [Fact]
    public void ExecutorName_ShouldBePowerShell()
    {
        _executor.Name.Should().Be("powershell");
    }

    [Fact]
    public async Task MissingCommand_ReturnsError()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-1",
            Method = "POST",
            Headers = new Dictionary<string, string>()
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("MissingCommand");
        result.ErrorMessage.Should().Be("X-PS-Command header required");
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task DisallowedCommand_ReturnsError()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-2",
            Method = "POST",
            Headers = new Dictionary<string, string> { { "X-PS-Command", "Remove-Mailbox" } }
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CommandNotAllowed");
        result.ErrorMessage.Should().Contain("not in the allowlist");
        result.IsTransientFailure.Should().BeFalse();
    }

    [Theory]
    [InlineData("Get-Mailbox")]
    [InlineData("Get-User")]
    [InlineData("Get-Recipient")]
    public async Task AllowlistedCommand_ExecutesSuccessfully(string command)
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-3",
            Method = "POST",
            Headers = new Dictionary<string, string> { { "X-PS-Command", command } }
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.PsCommand.Should().Contain(command);
        result.PsStdout.Should().NotBeNull();
        result.PsStdout.Should().HaveCountGreaterThan(0);
        result.PsStderr.Should().NotBeNull();
        result.PsStderr.Should().BeEmpty();
    }

    [Fact]
    public async Task SuccessfulExecution_IncludesTimestamps()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-4",
            Method = "POST",
            Headers = new Dictionary<string, string> { { "X-PS-Command", "Get-Mailbox" } }
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.StartedAt.Should().NotBe(default);
        result.CompletedAt.Should().NotBe(default);
        result.CompletedAt.Should().BeOnOrAfter(result.StartedAt);
    }

    [Fact]
    public async Task ExecutionWithFilter_AppliesFilter()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-5",
            Method = "POST",
            Headers = new Dictionary<string, string> 
            { 
                { "X-PS-Command", "Get-Mailbox" },
                { "X-PS-Filter", "Department -eq 'IT'" }
            }
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.PsCommand.Should().Contain("Filter");
    }

    [Fact]
    public async Task ExecutionWithResultSize_AppliesLimit()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-6",
            Method = "POST",
            Headers = new Dictionary<string, string> 
            { 
                { "X-PS-Command", "Get-User" },
                { "X-PS-ResultSize", "50" }
            }
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.PsCommand.Should().Contain("ResultSize");
    }

    [Fact]
    public async Task SuccessfulExecution_ReturnsObjects()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-7",
            Method = "POST",
            Headers = new Dictionary<string, string> { { "X-PS-Command", "Get-Mailbox" } }
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.PsObjects.Should().NotBeNull();
        result.PsObjects.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CancellationToken_CancelsExecution()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-8",
            Method = "POST",
            Headers = new Dictionary<string, string> { { "X-PS-Command", "Get-Mailbox" } }
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _executor.ExecuteAsync(request, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("Timeout");
        result.IsTransientFailure.Should().BeTrue();
    }
}


