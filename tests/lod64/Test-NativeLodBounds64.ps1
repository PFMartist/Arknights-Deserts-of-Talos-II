param(
    [string]$CandidateRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath($CandidateRoot)
$output = Join-Path $repository 'out\tests\lod64'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourcePaths = @(
    'src\NativeUnitLodAdapter.cs'
    'tests\lod64\LodStubs.cs'
    'tests\lod64\NativeLodBoundsFixture.cs'
    'tests\lod64\BaselineNativeUnitLodAdapter.cs'
)
$sourceHashes = [ordered]@{}
$sources = @()

if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'Windows .NET Framework 4.x compiler is required'
}
foreach ($relative in $sourcePaths) {
    $path = Join-Path $repository $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw ('Required fixture source is missing: ' + $relative)
    }
    $sources += $path
    $sourceHashes[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

[void][IO.Directory]::CreateDirectory($output)
$fixture = Join-Path $output 'NativeLodBoundsFixture.exe'
$compilerArguments = @(
    '/nologo'
    '/utf8output'
    '/target:exe'
    '/optimize+'
    '/warn:4'
    '/warnaserror+'
    ('/out:' + $fixture)
) + $sources
$compileOutput = @(& $compiler @compilerArguments 2>&1)
$compileExit = $LASTEXITCODE
[IO.File]::WriteAllLines((Join-Path $output 'compile.log'), [string[]]$compileOutput, (New-Object Text.UTF8Encoding($true)))
if ($compileExit -ne 0) {
    throw 'Native LOD fixture compilation failed'
}

$result = @(& $fixture 2>&1)
$resultExit = $LASTEXITCODE
[IO.File]::WriteAllLines((Join-Path $output 'result.log'), [string[]]$result, (New-Object Text.UTF8Encoding($true)))
$result

foreach ($relative in $sourcePaths) {
    $actualHash = (Get-FileHash -LiteralPath (Join-Path $repository $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $sourceHashes[$relative]) {
        throw ('Fixture source changed during execution: ' + $relative)
    }
}

$text = $result -join [Environment]::NewLine
$counts = [regex]::Match($text, 'PASS cases=(\d+) checks=(\d+)')
$negativeControl = [regex]::Match($text, 'NEGATIVE_CONTROL oldSize=([\d.]+) authoredDistanceSize=([\d.]+)')
$passed = $resultExit -eq 0 -and $counts.Success -and $negativeControl.Success
$report = [ordered]@{
    status = $(if ($passed) { 'PASS' } else { 'FAIL' })
    testMethod = 'Production NativeUnitLodAdapter with managed Unity and BBI substitutes'
    productionSource = 'src/NativeUnitLodAdapter.cs'
    baselineSource = 'tests/lod64/BaselineNativeUnitLodAdapter.cs'
    baselineRevision = 'hotfix63r2'
    sourceHashes = $sourceHashes
    fixtureExitCode = $resultExit
    cases = $(if ($counts.Success) { [int]$counts.Groups[1].Value } else { 0 })
    checks = $(if ($counts.Success) { [int]$counts.Groups[2].Value } else { 0 })
    oldRendererSphereSize = $(if ($negativeControl.Success) { [double]::Parse($negativeControl.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { $null })
    authoredDistanceSize = $(if ($negativeControl.Success) { [double]::Parse($negativeControl.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture) } else { $null })
    warningsAsErrors = $true
    gameAssetsRequired = $false
    unityGpuValidation = $false
    gameStarted = $false
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'REPORT.json') -Encoding UTF8
if (-not $passed) {
    throw 'Native LOD source fixture failed'
}
