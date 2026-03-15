# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first to cache restore
COPY ["Mcp.Compliance.Validator.sln", "./"]
COPY ["Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj", "Mcp.Benchmark.CLI/"]
COPY ["Mcp.Benchmark.Core/Mcp.Benchmark.Core.csproj", "Mcp.Benchmark.Core/"]
COPY ["Mcp.Benchmark.Infrastructure/Mcp.Benchmark.Infrastructure.csproj", "Mcp.Benchmark.Infrastructure/"]
COPY ["Mcp.Benchmark.Tests/Mcp.Benchmark.Tests.csproj", "Mcp.Benchmark.Tests/"]
COPY ["Directory.Build.props", "./"]

# Restore dependencies
RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Build and publish the CLI
WORKDIR "/src/Mcp.Benchmark.CLI"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Use the runtime image for the final stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "mcpval.dll"]
