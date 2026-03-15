param (
    [string]$Runtime = "win-x64"
)

Write-Host "Publishing MCP Validator (mcpval) — self-contained single-file executable for $Runtime..." -ForegroundColor Cyan

# Define paths
$projectPath = "Mcp.Benchmark.CLI/Mcp.Benchmark.CLI.csproj"
$outputDir = "publish/$Runtime"

# Clean
if (Test-Path $outputDir) {
    Remove-Item -Recurse -Force $outputDir
}

# Publish — self-contained, trimmed, single-file, compressed
# Settings are in the csproj (PublishSingleFile, SelfContained, PublishTrimmed, etc.)
# -r specifies the target runtime (win-x64, linux-x64, osx-x64, osx-arm64)

Write-Host "Publishing to $outputDir..." -ForegroundColor Yellow
dotnet publish $projectPath -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "Publish successful!" -ForegroundColor Green
    
    $exeName = "mcpval"
    if ($Runtime.StartsWith("win")) { $exeName += ".exe" }
    
    $publishedExe = Join-Path $outputDir $exeName

    if (-not (Test-Path $publishedExe)) {
        Write-Host "Executable created in: $(Resolve-Path $outputDir)" -ForegroundColor Cyan
        return
    }

    Write-Host "Executable location: $(Resolve-Path $publishedExe)" -ForegroundColor Cyan

    $toolsDir = Join-Path $env:USERPROFILE ".dotnet\\tools"
    if (-not (Test-Path $toolsDir)) {
        Write-Host "Creating $toolsDir" -ForegroundColor Yellow
        New-Item -ItemType Directory -Path $toolsDir | Out-Null
    }

    $targetExe = Join-Path $toolsDir $exeName
    Copy-Item -Path $publishedExe -Destination $targetExe -Force
    Write-Host "Installed CLI to $targetExe" -ForegroundColor Green

    $processPathEntries = ($env:PATH -split ';') | Where-Object { $_ -and $_.Trim().Length -gt 0 }
    $pathNeedsUpdate = -not ($processPathEntries -contains $toolsDir)
    if ($pathNeedsUpdate) {
        $env:PATH = "$toolsDir;" + $env:PATH
        Write-Host "Added $toolsDir to current session PATH" -ForegroundColor Yellow
    }

    $userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    $userPathEntries = @()
    if ($null -ne $userPath -and $userPath.Length -gt 0) {
        $userPathEntries = ($userPath -split ';') | Where-Object { $_ -and $_.Trim().Length -gt 0 }
    }

    if (-not ($userPathEntries -contains $toolsDir)) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $toolsDir } else { "$userPath;$toolsDir" }
        [Environment]::SetEnvironmentVariable("PATH", $newUserPath, "User")
        Write-Host "Added $toolsDir to User PATH. Open a new terminal to pick up the change." -ForegroundColor Yellow
    }

    Write-Host "You can now run 'mcpval validate -?' from any terminal." -ForegroundColor Cyan
} else {
    Write-Host "Publish failed." -ForegroundColor Red
}
