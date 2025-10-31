using RemoteExecutor.Api.Configuration;
using Microsoft.Extensions.Options;
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
builder.Services.AddSingleton<IExecutor, HttpExecutor>();
builder.Services.AddSingleton<IExecutor, PowerShellExecutor>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
    });

// Swagger in development
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() 
        { 
            Title = "Remote Request Executor API", 
            Version = "v1",
            Description = @"
A resilient proxy API for executing HTTP and PowerShell requests with:
- Automatic retry with exponential backoff
- Request/response envelope with traceability
- Structured logging and metrics
- Configurable timeouts and limits
- Command allowlisting for security
            "
        });
        c.OperationFilter<CustomHeaderSwaggerFilter>();
    });
}

var app = builder.Build();

// Log startup configuration
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var serviceConfig = app.Services.GetRequiredService<IOptions<ServiceConfiguration>>().Value;
logger.LogInformation("Starting Remote Executor API: InstanceId={InstanceId}, Environment={Environment}", 
    serviceConfig.InstanceId, app.Environment.EnvironmentName);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Remote Executor API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

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
