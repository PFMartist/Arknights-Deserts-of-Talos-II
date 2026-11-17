param([string]$SourceRoot,[string]$OutputRoot)
$ErrorActionPreference='Stop'
$root65=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(!$SourceRoot){$SourceRoot=Join-Path $root65 'src'}
if(!$OutputRoot){$OutputRoot=Join-Path $root65 'out\tests\hangar65'}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$compiler65=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $compiler65)){$compiler65=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$production65=Join-Path $SourceRoot 'RhodesHangarLayout.cs'
$stubs65=Join-Path $PSScriptRoot 'HangarStubs65.cs'
$fixture65=Join-Path $PSScriptRoot 'HangarFixture65.cs'
$executable65=Join-Path $OutputRoot 'HangarLifecycle65.exe'
& $compiler65 /nologo /utf8output /warn:4 /warnaserror+ ("/out:"+$executable65) $production65 $stubs65 $fixture65 2>&1 | Tee-Object -FilePath (Join-Path $OutputRoot 'BUILD.txt')
if($LASTEXITCODE -ne 0){throw 'Hangar lifecycle fixture compilation failed'}
& $executable65 2>&1 | Tee-Object -FilePath (Join-Path $OutputRoot 'RESULT.txt')
if($LASTEXITCODE -ne 0){throw 'Hangar lifecycle fixture failed'}
@($production65,$stubs65,$fixture65) | ForEach-Object {Get-FileHash -LiteralPath $_ -Algorithm SHA256} | Select-Object Path,Hash | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'SOURCE_SHA256.json') -Encoding UTF8
