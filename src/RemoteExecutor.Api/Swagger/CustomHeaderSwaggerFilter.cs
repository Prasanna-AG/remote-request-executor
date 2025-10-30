using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public class CustomHeaderSwaggerFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Forward-Base",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Base URL to forward the request to (REQUIRED for HTTP executor, e.g., https://jsonplaceholder.typicode.com)",
            Schema = new OpenApiSchema { Type = "string" }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Executor-Type",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Type of executor to use: 'http' or 'powershell' (default: http)",
            Schema = new OpenApiSchema 
            { 
                Type = "string",
                Default = new Microsoft.OpenApi.Any.OpenApiString("http")
            }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Request-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Custom request ID for tracking (auto-generated if not provided)",
            Schema = new OpenApiSchema { Type = "string" }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Correlation-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Correlation ID for distributed tracing",
            Schema = new OpenApiSchema { Type = "string" }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-PS-Command",
            In = ParameterLocation.Header,
            Required = false,
            Description = "PowerShell command to execute (required when X-Executor-Type=powershell). Allowed: Get-Mailbox, Get-User, Get-Recipient",
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}


