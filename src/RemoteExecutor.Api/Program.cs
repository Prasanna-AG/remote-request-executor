using RemoteExecutor.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configuration: Environment variables override appsettings.json
builder.Configuration.AddEnvironmentVariables();

// Bind configuration sections
builder.Services.Configure<ServiceConfiguration>(
    builder.Configuration.GetSection(ServiceConfiguration.SectionName));
builder.Services.Configure<RetryPolicyConfiguration>(
    builder.Configuration.GetSection(RetryPolicyConfiguration.SectionName));
builder.Services.Configure<HttpExecutorConfiguration>(
    builder.Configuration.GetSection(HttpExecutorConfiguration.SectionName));
builder.Services.Configure<PowerShellExecutorConfiguration>(
    builder.Configuration.GetSection(PowerShellExecutorConfiguration.SectionName));
builder.Services.Configure<ObservabilityConfiguration>(
    builder.Configuration.GetSection(ObservabilityConfiguration.SectionName));

// Structured JSON logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "json";
});
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
    options.JsonWriterOptions = new JsonWriterOptions
    {
        Indented = false
    };
});

// Core services
builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IRetryPolicyFactory, DefaultRetryPolicyFactory>();
builder.Services.AddSingleton<RequestValidator>();

// Executors: Register by interface for extensibility
// Conditionally use mock HttpClient in Development mode if configured
var httpConfig = builder.Configuration.GetSection(HttpExecutorConfiguration.SectionName).Get<HttpExecutorConfiguration>();

// Diagnostic logging for configuration
var tempLoggerFactory = LoggerFactory.Create(config => config.AddConsole());
var tempLogger = tempLoggerFactory.CreateLogger<Program>();
var envVarValue = builder.Configuration.GetSection(HttpExecutorConfiguration.SectionName)["UseMockHttpClient"];
var envVarRaw = Environment.GetEnvironmentVariable("Executors__Http__UseMockHttpClient");
tempLogger.LogInformation(
    "[CONFIG DEBUG] Environment={Environment}, UseMockHttpClient={UseMockHttpClient}, " +
    "ConfigSectionValue={ConfigSectionValue}, EnvVarRaw={EnvVarRaw}, WillUseMock={WillUseMock}",
    builder.Environment.EnvironmentName,
    httpConfig?.UseMockHttpClient ?? false,
    envVarValue ?? "null",
    envVarRaw ?? "null",
    (httpConfig?.UseMockHttpClient == true && builder.Environment.IsDevelopment()));

if (httpConfig?.UseMockHttpClient == true && builder.Environment.IsDevelopment())
{
    // Register HttpExecutor with mocked HttpClient for testing without external network calls
    builder.Services.AddSingleton<IExecutor>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<HttpExecutor>>();
        var httpConfigOpts = sp.GetRequiredService<IOptions<HttpExecutorConfiguration>>();
        var retryConfig = sp.GetRequiredService<IOptions<RetryPolicyConfiguration>>();
        var mockHttp = new HttpClient(new MockHttpMessageHandler())
        {
            Timeout = TimeSpan.FromSeconds(httpConfigOpts.Value.DefaultTimeoutSeconds)
        };
        logger.LogWarning("HttpExecutor configured with MOCK HttpClient - no external network calls will be made");
        return new HttpExecutor(logger, httpConfigOpts, retryConfig, mockHttp);
    });
}
else
{
    builder.Services.AddSingleton<IExecutor, HttpExecutor>();
}
builder.Services.AddSingleton<IExecutor, PowerShellExecutor>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
    });

var app = builder.Build();

// Log startup configuration
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var serviceConfig = app.Services.GetRequiredService<IOptions<ServiceConfiguration>>().Value;
logger.LogInformation("Starting Remote Executor API: InstanceId={InstanceId}, Environment={Environment}", 
    serviceConfig.InstanceId, app.Environment.EnvironmentName);

// Health check endpoint (lightweight)
app.MapGet("/ping", () => Results.Text("pong", "text/plain"));

// Metrics endpoint
app.MapGet("/metrics", (IMetricsCollector metrics, ISystemClock clock) => 
{
    var snapshot = metrics.GetMetricsSnapshot();
    return Results.Ok(new
    {
        timestamp = clock.UtcNow,
        instance = serviceConfig.InstanceId,
        metrics = snapshot
    });
});

// Main API routes
app.MapControllers();

app.Run();

// Make Program accessible to integration tests
public partial class Program { }
