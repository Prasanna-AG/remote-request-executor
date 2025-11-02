# Multi-stage Dockerfile for Remote Request Executor API
# Produces a small, secure runtime image with non-root user

# =============================================================================
# Stage 1: Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Copy solution and project files
COPY src/RemoteExecutor.Api/RemoteExecutor.Api.csproj src/RemoteExecutor.Api/
COPY src/RemoteExecutor.Tests/RemoteExecutor.Tests.csproj src/RemoteExecutor.Tests/

# Restore dependencies (cached layer if project files unchanged)
RUN dotnet restore src/RemoteExecutor.Api/RemoteExecutor.Api.csproj

# Copy source code
COPY src/ src/

# Build and publish
WORKDIR /source/src/RemoteExecutor.Api
RUN dotnet publish -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# =============================================================================
# Stage 2: Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Install curl for healthcheck
RUN apt-get update && \
    apt-get install -y curl && \
    rm -rf /var/lib/apt/lists/*

# Install required dependencies for PowerShell remoting (if needed in production)
# Uncomment if you need PowerShell SDK for real remote sessions
# RUN apt-get update && apt-get install -y \
#     powershell \
#     && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Create non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published application
COPY --from=build /app/publish .

# Change ownership to non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose HTTP port
EXPOSE 5072

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5072/ping || exit 1

# Environment variables with defaults
# HTTP only - use reverse proxy (nginx/traefik) for SSL termination in production
ENV ASPNETCORE_URLS=http://+:5072 \
    ASPNETCORE_ENVIRONMENT=Production \
    Service__InstanceId=docker-instance \
    Service__MaxRequestBodySizeKb=1000 \
    RetryPolicy__MaxAttempts=3 \
    RetryPolicy__BaseDelayMs=200 \
    RetryPolicy__MaxDelayMs=5000 \
    Logging__Console__FormatterName=json

# Entry point
ENTRYPOINT ["dotnet", "RemoteExecutor.Api.dll"]

# =============================================================================
# Build and Run Instructions
# =============================================================================
#
# Build the image:
#   docker build -t remote-executor:latest .
#
# Run with default settings:
#   docker run -p 5072:5072 remote-executor:latest
#   curl http://localhost:5072/ping
#
# Run with custom configuration:
#   docker run -p 5072:5072 \
#     -e Service__InstanceId=prod-01 \
#     -e RetryPolicy__MaxAttempts=5 \
#     remote-executor:latest
#
# Run with volume-mounted config:
#   docker run -p 5072:5072 \
#     -v $(pwd)/appsettings.json:/app/appsettings.json:ro \
#     remote-executor:latest
#
# Health check:
#   curl http://localhost:5072/ping
#
# Metrics:
#   curl http://localhost:5072/metrics
#
# Docker Compose example (create docker-compose.yml):
#   version: '3.8'
#   services:
#     remote-executor:
#       build: .
#       ports:
#         - "5072:5072"
#       environment:
#         - Service__InstanceId=compose-instance
#         - RetryPolicy__MaxAttempts=5
#       restart: unless-stopped
#
# =============================================================================


