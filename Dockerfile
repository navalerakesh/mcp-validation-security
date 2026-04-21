# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project metadata first for better restore caching.
COPY ["Directory.Build.props", "./"]
COPY ["mcp-benchmark-validation.sln", "./"]
COPY ["Mcp.Compliance.Spec/Mcp.Compliance.Spec.csproj", "Mcp.Compliance.Spec/"]
COPY ["Mcp.Benchmark.Core/Mcp.Benchmark.Core.csproj", "Mcp.Benchmark.Core/"]
COPY ["Mcp.Benchmark.Infrastructure/Mcp.Benchmark.Infrastructure.csproj", "Mcp.Benchmark.Infrastructure/"]
COPY ["Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj", "Mcp.Benchmark.CLI/"]

RUN dotnet restore "Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj"

COPY . .

RUN dotnet publish "Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj" \
	-c Release \
	-r linux-x64 \
	--self-contained true \
	-p:PublishSingleFile=true \
	-p:EnableCompressionInSingleFile=true \
	-p:UseAppHost=true \
	-p:PackAsTool=false \
	-o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy-chiseled AS final
WORKDIR /app

COPY --from=build /app/publish/mcpval /app/mcpval

USER 1654:1654

ENTRYPOINT ["/app/mcpval"]
