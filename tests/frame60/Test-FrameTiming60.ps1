param([string]$CandidateRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
$output=Join-Path $CandidateRoot 'out\tests\frame60'
[void][IO.Directory]::CreateDirectory($output)
$fixture=Join-Path $output 'FrameTiming60Fixture.exe'
& (Get-LegacyCsc) /nologo /target:exe /optimize+ /warn:4 /warnaserror+ ('/out:'+$fixture) (Join-Path $PSScriptRoot 'FrameTiming60Fixture.cs') (Join-Path $CandidateRoot 'src\RhodesSkirmishFrameTiming.cs') (Join-Path $CandidateRoot 'src\FrameWindowHistogram.cs')
if($LASTEXITCODE -ne 0){throw 'Frame-timing fixture compile failed'}
$audit=Join-Path $CandidateRoot 'out\tests\frame60'
New-Item -ItemType Directory -Path $audit -Force | Out-Null
$result=@(& $fixture 2>&1)
$code=$LASTEXITCODE
$result | Set-Content -LiteralPath (Join-Path $audit 'FRAME_TIMING60_FIXTURE.txt') -Encoding UTF8
$result
if($code -ne 0){throw 'Frame-timing fixture failed'}
$sourceHashes=@{}
foreach($name in @('RhodesSkirmishFrameTiming.cs','FrameWindowHistogram.cs')){
    $sourceHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $CandidateRoot ('src\'+$name)) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$report=[ordered]@{
    status='PASS_OFFLINE_FRAME_TIMING60_SOURCE_FIXTURE'
    sourceHashes=$sourceHashes
    checks=[int]([regex]::Match(($result -join [Environment]::NewLine),'checks=(\d+)').Groups[1].Value)
    testMethod='Production timing source compiled with controlled Unity clock and logging stubs'
    initialWindows=60
    initialWindowSeconds=10
    lateWindowSeconds=60
    continuedWindowsTested=101
    fixedStorageReused=$true
    defaultEnabled=$false
    qualitySettingsUnchanged=$true
    perFrameSectionTotalsAndCallCounts=$true
    noFpsMeasurement=$true
    gameStarted=$false
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $audit 'FRAME60_FINAL_BINDING.json') -Encoding UTF8
