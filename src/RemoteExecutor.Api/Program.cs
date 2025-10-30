var builder = WebApplication.CreateBuilder(args);

// config sources (env overrides appsettings)
builder.Configuration.AddEnvironmentVariables();

// Logging: default; in production swap to structured JSON (Serilog)
builder.Services.AddLogging();

// DI: core services
builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
builder.Services.AddSingleton<ISystemClock, SystemClock>(); // simple clock wrapper (create file)
builder.Services.AddSingleton<IRetryPolicyFactory, DefaultRetryPolicyFactory>();

// Executors: register by interface; new executors add here
builder.Services.AddSingleton<IExecutor, HttpExecutor>();
builder.Services.AddSingleton<IExecutor, PowerShellExecutor>();

builder.Services.AddControllers(); // we will host a controller for route /api/{**path}

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "Remote Request Executor API", Version = "v1" });
        
        // Add custom headers to all operations
        c.OperationFilter<CustomHeaderSwaggerFilter>();
    });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


//var app = builder.Build();

// health & metrics via minimal API
app.MapGet("/ping", () => Results.Ok(new { status = "pong" }));
app.MapGet("/metrics", (IMetricsCollector m) => Results.Ok(m.GetMetricsSnapshot()));

app.MapControllers();

app.Run();

// Make Program accessible to integration tests
public partial class Program { }
