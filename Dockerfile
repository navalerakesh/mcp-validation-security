# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0.419@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS build
ARG VERSION=0.0.0
ARG TARGETARCH
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

RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; esac && \
	dotnet publish "Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj" \
	-c Release \
	-r "$rid" \
	--self-contained true \
	--no-restore \
	-p:PublishSingleFile=true \
	-p:EnableCompressionInSingleFile=true \
	-p:UseAppHost=true \
	-p:PackAsTool=false \
	-p:Version="$VERSION" \
	-o /app/publish

# Canonical report formatting requires globalization data (ICU).
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0.25-jammy-chiseled-extra@sha256:ca7e499d84863de7426e5f3d0c90256b626a1b4431394d8e05bd38c8f71a0eb9 AS final
ARG VERSION=0.0.0
LABEL org.opencontainers.image.version="$VERSION"
WORKDIR /app

COPY --from=build /app/publish/mcpval /app/mcpval

USER 1654:1654

ENTRYPOINT ["/app/mcpval"]
