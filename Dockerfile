# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:4b1cdaa57eed2cecabcf29bdb9bce11e8ca1c287d39dfd2c8b534663ea94d493 AS build
WORKDIR /src

# Copy project metadata first for better restore caching.
COPY ["Directory.Build.props", "./"]
COPY ["mcp-benchmark-validation.sln", "./"]
COPY ["Mcp.Compliance.Spec/Mcp.Compliance.Spec.csproj", "Mcp.Compliance.Spec/"]
COPY ["Mcp.Compliance.Spec/packages.lock.json", "Mcp.Compliance.Spec/"]
COPY ["Mcp.Benchmark.Core/Mcp.Benchmark.Core.csproj", "Mcp.Benchmark.Core/"]
COPY ["Mcp.Benchmark.Core/packages.lock.json", "Mcp.Benchmark.Core/"]
COPY ["Mcp.Benchmark.ClientProfiles/Mcp.Benchmark.ClientProfiles.csproj", "Mcp.Benchmark.ClientProfiles/"]
COPY ["Mcp.Benchmark.ClientProfiles/packages.lock.json", "Mcp.Benchmark.ClientProfiles/"]
COPY ["Mcp.Benchmark.Infrastructure/Mcp.Benchmark.Infrastructure.csproj", "Mcp.Benchmark.Infrastructure/"]
COPY ["Mcp.Benchmark.Infrastructure/packages.lock.json", "Mcp.Benchmark.Infrastructure/"]
COPY ["Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj", "Mcp.Benchmark.CLI/"]
COPY ["Mcp.Benchmark.CLI/packages.lock.json", "Mcp.Benchmark.CLI/"]

RUN dotnet restore "Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj" --locked-mode

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

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy-chiseled@sha256:c0a1e27d3ece495f1acad0aa7fa265a709f762c5e9c391a6f50575ff9429b670 AS final
WORKDIR /app

COPY --from=build /app/publish/mcpval /app/mcpval

USER 1654:1654

ENTRYPOINT ["/app/mcpval"]
