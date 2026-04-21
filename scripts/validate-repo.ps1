param(
    [switch]$SkipDotNet,
    [switch]$SkipNode,
    [switch]$SkipAudits,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Assert-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$CommandName)

    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $CommandName"
    }
}

function Get-HighOrCriticalNuGetVulnerabilities {
    param([Parameter(Mandatory = $true)][string]$AuditJsonPath)

    $payload = Get-Content $AuditJsonPath -Raw | ConvertFrom-Json
    $findings = [System.Collections.Generic.List[object]]::new()
    $severityRank = @{
        low = 1
        moderate = 2
        high = 3
        critical = 4
    }

    function Find-NodeVulnerabilities {
        param($Node, [string]$Project, [string]$Framework)

        if ($null -eq $Node) {
            return
        }

        if ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string]) -and -not ($Node.PSObject.Properties.Name -contains 'Count')) {
            foreach ($Item in $Node) {
                Find-NodeVulnerabilities -Node $Item -Project $Project -Framework $Framework
            }
            return
        }

        if ($Node.PSObject -and $Node.PSObject.Properties.Count -gt 0) {
            $currentProject = if ($Node.PSObject.Properties.Name -contains 'path' -and $Node.path) { [string]$Node.path } else { $Project }
            $currentFramework = if ($Node.PSObject.Properties.Name -contains 'framework' -and $Node.framework) { [string]$Node.framework } else { $Framework }

            if (($Node.PSObject.Properties.Name -contains 'name') -and ($Node.PSObject.Properties.Name -contains 'vulnerabilities') -and $Node.vulnerabilities) {
                foreach ($vulnerability in $Node.vulnerabilities) {
                    $severity = [string]$vulnerability.severity
                    if ($severityRank[$severity.ToLowerInvariant()] -ge 3) {
                        $findings.Add([pscustomobject]@{
                            Severity = $severity
                            Package = [string]$Node.name
                            Version = if ($Node.PSObject.Properties.Name -contains 'resolvedVersion' -and $Node.resolvedVersion) { [string]$Node.resolvedVersion } else { [string]$Node.requestedVersion }
                            Project = $currentProject
                            Framework = $currentFramework
                        }) | Out-Null
                    }
                }
            }

            foreach ($property in $Node.PSObject.Properties) {
                Find-NodeVulnerabilities -Node $property.Value -Project $currentProject -Framework $currentFramework
            }
        }
        elseif ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string])) {
            foreach ($Item in $Node) {
                Find-NodeVulnerabilities -Node $Item -Project $Project -Framework $Framework
            }
        }
    }

    Find-NodeVulnerabilities -Node $payload -Project $null -Framework $null
    return $findings
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Write-Host "Repository root: $repoRoot"
Push-Location $repoRoot

try {
    if (-not $SkipDotNet) {
        Assert-CommandAvailable dotnet
    }

    if (-not $SkipNode) {
        Assert-CommandAvailable node
        Assert-CommandAvailable npm
    }

    if (-not $SkipDotNet) {
        Write-Host '==> Restoring .NET dependencies'
        dotnet restore .\mcp-benchmark-validation.sln

        Write-Host "==> Building solution ($Configuration)"
        dotnet build .\mcp-benchmark-validation.sln --configuration $Configuration --no-restore

        Write-Host "==> Running .NET tests ($Configuration)"
        dotnet test .\mcp-benchmark-validation.sln --configuration $Configuration --no-build

        if (-not $SkipAudits) {
            Write-Host '==> Running NuGet audit'
            $auditJsonPath = Join-Path ([System.IO.Path]::GetTempPath()) ("mcpval-dotnet-audit-{0}.json" -f [guid]::NewGuid().ToString('N'))
            try {
                dotnet list .\mcp-benchmark-validation.sln package --vulnerable --include-transitive --format json | Set-Content -Path $auditJsonPath -Encoding UTF8
                $findings = Get-HighOrCriticalNuGetVulnerabilities -AuditJsonPath $auditJsonPath
                if ($findings.Count -gt 0) {
                    Write-Host 'High or critical NuGet vulnerabilities detected:' -ForegroundColor Red
                    foreach ($finding in $findings) {
                        Write-Host ("- {0}: {1} {2} ({3} / {4})" -f $finding.Severity, $finding.Package, $finding.Version, $finding.Project, $finding.Framework)
                    }
                    throw 'NuGet audit failed.'
                }

                Write-Host 'No high or critical NuGet vulnerabilities detected.'
            }
            finally {
                if (Test-Path $auditJsonPath) {
                    Remove-Item $auditJsonPath -Force
                }
            }
        }
    }

    if (-not $SkipNode) {
        Push-Location .\mcpval-mcp
        try {
            Write-Host '==> Installing npm dependencies'
            npm ci

            Write-Host '==> Building npm package'
            npm run build

            if (-not $SkipAudits) {
                Write-Host '==> Running npm audit'
                npm audit --audit-level=high
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host 'Repository validation passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}