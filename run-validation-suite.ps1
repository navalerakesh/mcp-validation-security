$servers = @(
    "https://api.githubcopilot.com/mcp/"
    )

foreach ($server in $servers) {
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "Validating: $server" -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------`n" -ForegroundColor Cyan
    
    & .\publish\win-x64\mcpval.exe validate --server $server --verbose --output reports --interactive --access authenticated
}


$servers = @(
    "https://learn.microsoft.com/api/mcp"
)

foreach ($server in $servers) {
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "Validating: $server" -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------`n" -ForegroundColor Cyan
    
    & .\publish\win-x64\mcpval.exe validate --server $server --verbose --output reports --access public
}
