using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing HttpExecutor registration
                var executorDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IExecutor) && s.ImplementationType == typeof(HttpExecutor));
                if (executorDescriptor != null)
                {
                    services.Remove(executorDescriptor);
                }

                // Register HttpExecutor with mocked HttpClient
                services.AddSingleton<IExecutor>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<HttpExecutor>>();
                    var httpConfig = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RemoteExecutor.Api.Configuration.HttpExecutorConfiguration>>();
                    var retryConfig = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RemoteExecutor.Api.Configuration.RetryPolicyConfiguration>>();
                    var mockHttp = new System.Net.Http.HttpClient(new MockHttpMessageHandler())
                    {
                        Timeout = TimeSpan.FromSeconds(30)
                    };
                    return new HttpExecutor(logger, httpConfig, retryConfig, mockHttp);
                });
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsPong()
    {
        var response = await _client.GetAsync("/ping");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("pong");
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsMetricsSnapshot()
    {
        var response = await _client.GetAsync("/metrics");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("total");
    }

    [Fact]
    public async Task HttpExecutor_MissingForwardBase_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/path");
        
        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK); // Controller returns 200 with error in envelope
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("BadConfiguration");
        body.Should().Contain("Missing X-Forward-Base");
    }

    [Fact]
    public async Task HttpExecutor_WithValidForwardBase_ForwardsRequest()
    {
        // Using mocked HTTP client - no external network calls
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/1");
        request.Headers.Add("X-Forward-Base", "https://api.test.com");
        request.Headers.Add("X-Executor-Type", "http");
        request.Headers.Add("X-Request-Id", "integration-test-1");

        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Request-Id", out var reqIdValues).Should().BeTrue();
        reqIdValues!.First().Should().Be("integration-test-1");
        response.Headers.Should().ContainKey("X-Executor");
        response.Headers.GetValues("X-Executor").First().Should().Be("http");
        
        // Verify the mocked response was used (check for successful execution)
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("integration-test-1"); // Request ID should be in response
        body.Should().Contain("executorType"); // Response envelope structure
        // Note: We avoid checking response body content since HttpExecutor processes it
        // The key test is that no external network call was made (mocked handler used)
    }

    [Fact]
    public async Task PowerShellExecutor_WithAllowlistedCommand_ExecutesSuccessfully()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mailbox/list");
        request.Headers.Add("X-Executor-Type", "powershell");
        request.Headers.Add("X-PS-Command", "Get-Mailbox");
        request.Headers.Add("X-Request-Id", "ps-integration-1");

        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Get-Mailbox");
        body.Should().Contain("Simulated output");
        body.Should().Contain("Success");
    }

    [Fact]
    public async Task PowerShellExecutor_WithDisallowedCommand_ReturnsError()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mailbox/delete");
        request.Headers.Add("X-Executor-Type", "powershell");
        request.Headers.Add("X-PS-Command", "Remove-Mailbox");
        request.Headers.Add("X-Request-Id", "ps-integration-2");

        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("CommandNotAllowed");
        body.Should().Contain("Failure");
    }

    [Fact]
    public async Task PowerShellExecutor_MissingCommand_ReturnsError()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mailbox/test");
        request.Headers.Add("X-Executor-Type", "powershell");
        request.Headers.Add("X-Request-Id", "ps-integration-3");

        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("InvalidRequest");
    }

    [Fact]
    public async Task UnsupportedExecutor_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        request.Headers.Add("X-Executor-Type", "unknown-executor");
        request.Headers.Add("X-Request-Id", "unknown-exec-1");

        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("UnsupportedExecutor");
    }

    [Fact]
    public async Task ResponseEnvelope_IncludesRequestId()
    {
        var customRequestId = "custom-req-id-999";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        request.Headers.Add("X-Forward-Base", "https://api.test.com");
        request.Headers.Add("X-Request-Id", customRequestId);

        var response = await _client.SendAsync(request);
        
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(customRequestId);
        response.Headers.GetValues("X-Request-Id").First().Should().Be(customRequestId);
    }

    [Fact]
    public async Task ResponseEnvelope_IncludesCorrelationId()
    {
        var correlationId = "corr-id-888";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        request.Headers.Add("X-Forward-Base", "https://api.test.com");
        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await _client.SendAsync(request);
        
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(correlationId);
    }

    [Fact]
    public async Task ResponseEnvelope_IncludesTimestamps()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/test");
        request.Headers.Add("X-Executor-Type", "powershell");
        request.Headers.Add("X-PS-Command", "Get-User");

        var response = await _client.SendAsync(request);
        
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("startedAt");
        body.Should().Contain("completedAt");
    }

    [Fact]
    public async Task LargeRequestBody_IsRejected()
    {
        var largeBody = new string('x', 2_000_001);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/test");
        request.Content = new StringContent(largeBody, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("InvalidRequest");
    }
}

/// <summary>
/// Mock HTTP message handler for integration tests - avoids external network calls
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Return a mocked successful response for any HTTP request
        // Content-Type is automatically set by StringContent constructor
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\":1,\"name\":\"test-response\",\"status\":\"ok\"}", System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}


