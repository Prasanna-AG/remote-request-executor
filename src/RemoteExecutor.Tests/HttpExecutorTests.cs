using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;

public class HttpExecutorTests
{
    private readonly HttpExecutor _executor;

    public HttpExecutorTests()
    {
        var httpConfig = Options.Create(new HttpExecutorConfiguration
        {
            MaxResponseBodyLengthKb = 512,
            DefaultTimeoutSeconds = 15,
            FilteredHeaders = new[] { "Authorization", "Cookie" },
            AllowedHeaderPrefixes = new[] { "Accept", "Content-Type" }
        });

        var retryConfig = Options.Create(new RetryPolicyConfiguration
        {
            TransientStatusCodes = new[] { 408, 429, 500, 502, 503, 504 }
        });

        _executor = new HttpExecutor(NullLogger<HttpExecutor>.Instance, httpConfig, retryConfig);
    }

    [Fact]
    public void ExecutorName_ShouldBeHttp()
    {
        _executor.Name.Should().Be("http");
    }

    [Fact]
    public async Task MissingForwardBase_ReturnsError()
    {
        var request = new RequestEnvelope
        {
            RequestId = "test-1",
            Method = "GET",
            Path = "users/1",
            Headers = new Dictionary<string, string>()
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("BadConfiguration");
        result.ErrorMessage.Should().Be("Missing X-Forward-Base header");
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidForwardBaseUrl_ReturnsError()
    {
        var request = new RequestEnvelope
        {
            RequestId = "test-2",
            Method = "GET",
            Path = "users/1",
            Headers = new Dictionary<string, string> { { "X-Forward-Base", "not-a-valid-url" } }
        };

        var result = await _executor.ExecuteAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidUri");
        result.ErrorMessage.Should().Contain("Invalid");
    }

    [Fact]
    public async Task TransientStatusCode_MarkedAsTransient()
    {
        // This test verifies the classification logic
        // In a real scenario, we'd use a mock HTTP server
        var result = ExecutionResult.FromHttp(503, new Dictionary<string, string>(), "Service Unavailable", new[] { 503 });

        result.IsSuccess.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
    }

    [Fact]
    public async Task NonTransientStatusCode_NotMarkedAsTransient()
    {
        var result = ExecutionResult.FromHttp(400, new Dictionary<string, string>(), "Bad Request", new[] { 503 });

        result.IsSuccess.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task SuccessStatusCode_MarkedAsSuccess()
    {
        var result = ExecutionResult.FromHttp(200, new Dictionary<string, string>(), "OK");

        result.IsSuccess.Should().BeTrue();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task TransientStatusCodes_ClassifiedCorrectly(int statusCode)
    {
        var transientCodes = new[] { 408, 429, 500, 502, 503, 504 };
        var result = ExecutionResult.FromHttp(statusCode, new Dictionary<string, string>(), "Error", transientCodes);

        result.IsTransientFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task NonTransientStatusCodes_ClassifiedCorrectly(int statusCode)
    {
        var transientCodes = new[] { 408, 429, 500, 502, 503, 504 };
        var result = ExecutionResult.FromHttp(statusCode, new Dictionary<string, string>(), "Error", transientCodes);

        result.IsTransientFailure.Should().BeFalse();
    }
}


