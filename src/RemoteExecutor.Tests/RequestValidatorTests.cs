using FluentAssertions;
using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;

public class RequestValidatorTests
{
    private readonly RequestValidator _validator;

    public RequestValidatorTests()
    {
        var config = Options.Create(new ServiceConfiguration
        {
            MaxRequestBodySizeKb = 1024
        });
        _validator = new RequestValidator(config);
    }

    [Fact]
    public void ValidHttpRequest_PassesValidation()
    {
        var request = new RequestEnvelope
        {
            RequestId = "test-123",
            Method = "GET",
            Path = "users/1",
            Headers = new Dictionary<string, string> 
            { 
                { "X-Executor-Type", "http" },
                { "X-Forward-Base", "https://api.test.com" } 
            }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeTrue();
        result.Message.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void NullRequest_FailsValidation()
    {
        var result = _validator.Validate(null!);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("NullRequest");
        result.Message.Should().Be("Request envelope is null");
    }

    [Fact]
    public void EmptyRequestId_FailsValidation()
    {
        var request = new RequestEnvelope
        {
            RequestId = "",
            Method = "GET",
            Path = "test"
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("MissingRequestId");
        result.Message.Should().Be("RequestId is required");
    }

    [Fact]
    public void PowerShellRequestWithoutCommand_FailsValidation()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-123",
            Method = "POST",
            Headers = new Dictionary<string, string> { { "X-Executor-Type", "powershell" } }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("MissingPsCommand");
        result.Message.Should().Be("X-PS-Command header required for PowerShell executor");
    }

    [Fact]
    public void PowerShellRequestWithCommand_PassesValidation()
    {
        var request = new RequestEnvelope
        {
            RequestId = "ps-123",
            Method = "POST",
            Headers = new Dictionary<string, string> 
            { 
                { "X-Executor-Type", "powershell" },
                { "X-PS-Command", "Get-Mailbox" }
            }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeTrue();
        result.Message.Should().BeNull();
    }

    [Fact]
    public void HttpRequestWithoutForwardBase_FailsValidation()
    {
        var request = new RequestEnvelope
        {
            RequestId = "http-123",
            Method = "GET",
            Headers = new Dictionary<string, string> { { "X-Executor-Type", "http" } }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("MissingForwardBase");
        result.Message.Should().Be("X-Forward-Base header required for HTTP executor");
    }

    [Fact]
    public void RequestWithOversizedBody_FailsValidation()
    {
        var largeBody = new string('a', 1_048_577); // 1MB + 1 byte
        var request = new RequestEnvelope
        {
            RequestId = "large-123",
            Method = "POST",
            Body = largeBody,
            Headers = new Dictionary<string, string> 
            { 
                { "X-Executor-Type", "http" },
                { "X-Forward-Base", "https://api.test.com" } 
            }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("BodyTooLarge");
        result.Message.Should().Contain("exceeds maximum size");
    }

    [Fact]
    public void InvalidHttpMethod_FailsValidation()
    {
        var request = new RequestEnvelope
        {
            RequestId = "invalid-123",
            Method = "INVALID",
            Headers = new Dictionary<string, string> 
            { 
                { "X-Executor-Type", "http" },
                { "X-Forward-Base", "https://api.test.com" } 
            }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidHttpMethod");
        result.Message.Should().Contain("not supported");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void ValidHttpMethods_PassValidation(string method)
    {
        var request = new RequestEnvelope
        {
            RequestId = "test-123",
            Method = method,
            Headers = new Dictionary<string, string> 
            { 
                { "X-Executor-Type", "http" },
                { "X-Forward-Base", "https://api.test.com" } 
            }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }
}


