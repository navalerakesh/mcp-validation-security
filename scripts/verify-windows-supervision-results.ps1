param(
    [string]$Path,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-BoundedTrx([string]$Text) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 8MB
    $reader = [Xml.XmlReader]::Create([IO.StringReader]::new($Text), $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return ,$document
    } finally { $reader.Dispose() }
}

function Assert-NativeResults([Xml.XmlDocument]$Document) {
    $namespaces = [Xml.XmlNamespaceManager]::new($Document.NameTable)
    $namespaces.AddNamespace('trx', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $summaries = $Document.SelectNodes('/trx:TestRun/trx:ResultSummary', $namespaces)
    if ($summaries.Count -ne 1 -or $summaries[0].GetAttribute('outcome') -notin @('Completed', 'Passed')) {
        throw 'TRX must contain one successful run summary.'
    }
    $counters = $Document.SelectNodes('/trx:TestRun/trx:ResultSummary/trx:Counters', $namespaces)
    if ($counters.Count -ne 1) { throw 'TRX counters are missing or ambiguous.' }
    $counts = @{}
    foreach ($attribute in $counters[0].Attributes) {
        if ($attribute.Value -notmatch '^\d+$') { throw 'TRX counters must be nonnegative integers.' }
        $counts[$attribute.Name] = [long]$attribute.Value
        if ($attribute.Name -notin @('total', 'executed', 'passed') -and [long]$attribute.Value -ne 0) {
            throw "Native qualification rejects nonzero $($attribute.Name)."
        }
    }
    foreach ($required in @('total', 'executed', 'passed', 'failed', 'notExecuted', 'error')) {
        if (-not $counts.ContainsKey($required)) { throw "TRX is missing $required." }
    }
    if ($counts.total -le 0 -or $counts.executed -ne $counts.total -or $counts.passed -ne $counts.total) {
        throw 'Native qualification requires discovered > 0 and every discovered case executed and passed.'
    }
    $results = $Document.SelectNodes('/trx:TestRun/trx:Results/trx:UnitTestResult', $namespaces)
    if ($results.Count -ne $counts.total) { throw 'TRX result rows disagree with total discovery.' }
    $identities = [Collections.Generic.HashSet[string]]::new()
    foreach ($result in $results) {
        if ($result.GetAttribute('outcome') -ne 'Passed' -or
            -not $result.GetAttribute('testName').StartsWith('Mcp.Benchmark.Tests.Integration.WindowsSupervisorNativeTests.')) {
            throw 'Unexpected, failed, or skipped native test result.'
        }
        $identity = $result.GetAttribute('testId')
        if (-not $identity -or -not $identities.Add($identity)) { throw 'Missing or duplicate test identity.' }
    }
    return $counts.total
}

if ($SelfTest) {
    $valid = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testId="one" testName="Mcp.Benchmark.Tests.Integration.WindowsSupervisorNativeTests.Probe" outcome="Passed" /></Results><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" error="0" /></ResultSummary></TestRun>'
    if ((Assert-NativeResults (Read-BoundedTrx $valid)) -ne 1) { throw 'Valid evidence was rejected.' }
    $invalid = @(
        '', '<TestRun />', '<broken',
        $valid.Replace('total="1"', 'total="0"'),
        $valid.Replace('executed="1"', 'executed="0"'),
        $valid.Replace('notExecuted="0"', 'notExecuted="1"'),
        $valid.Replace('failed="0"', 'failed="1"'),
        $valid.Replace('error="0"', 'error="1"'),
        $valid.Replace('outcome="Passed"', 'outcome="NotExecuted"'),
        $valid.Replace('outcome="Completed"', 'outcome="Failed"'),
        $valid.Replace('total="1"', 'total="-1"'),
        $valid.Replace('total="1"', 'total="2"'),
        $valid.Replace(' error="0"', ''),
        $valid.Replace('WindowsSupervisorNativeTests.', 'UnrelatedTests.'),
        $valid.Replace('testId="one"', 'testId=""'),
        ('<!DOCTYPE TestRun [<!ENTITY probe "forbidden">]>' + $valid)
    )
    foreach ($candidate in $invalid) {
        $rejected = $false
        try { $null = Assert-NativeResults (Read-BoundedTrx $candidate) } catch { $rejected = $true }
        if (-not $rejected) { throw "Verifier accepted invalid evidence: $candidate" }
    }
    Write-Output "TRX verifier self-test: 1 valid and $($invalid.Count) invalid cases passed."
    return
}

if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Required native TRX evidence is missing.' }
$file = Get-Item -LiteralPath $Path
if ($file.Length -le 0 -or $file.Length -gt 8MB) { throw 'TRX size is outside its evidence budget.' }
$total = Assert-NativeResults (Read-BoundedTrx ([IO.File]::ReadAllText($file.FullName)))
Write-Output "Windows native qualification: discovered=$total passed=$total failed=0 skipped=0."