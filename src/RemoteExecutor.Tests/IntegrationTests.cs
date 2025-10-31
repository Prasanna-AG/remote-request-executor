using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
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
        // Using a mock HTTP server would be ideal, but for now we test the flow
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/1");
        request.Headers.Add("X-Forward-Base", "https://jsonplaceholder.typicode.com");
        request.Headers.Add("X-Executor-Type", "http");
        request.Headers.Add("X-Request-Id", "integration-test-1");

        var response = await _client.SendAsync(request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Request-Id", out var reqIdValues).Should().BeTrue();
        reqIdValues!.First().Should().Be("integration-test-1");
        response.Headers.Should().ContainKey("X-Executor");
        response.Headers.GetValues("X-Executor").First().Should().Be("http");
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
        request.Headers.Add("X-Forward-Base", "https://httpbin.org");
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
        request.Headers.Add("X-Forward-Base", "https://httpbin.org");
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


