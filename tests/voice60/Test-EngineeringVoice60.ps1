param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [Parameter(Mandatory=$true)][string]$VoiceRoot,
 [Parameter(Mandatory=$true)][string]$BaselineRoutes,
 [string]$RuntimeDll=''
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runtime=Get-TestRuntime $root $RuntimeDll
$native=Join-Path $GameRoot 'Data\Managed'
$output=Join-Path $root 'out\tests\voice60'
[void][IO.Directory]::CreateDirectory($output)
$fixture=Join-Path $output 'EngineeringProduction60Fixture.exe'
& (Get-LegacyCsc) /nologo /utf8output /target:exe /optimize+ /warn:4 /warnaserror+ ('/out:'+$fixture) (Join-Path $PSScriptRoot 'EngineeringProduction60Fixture.cs')
if($LASTEXITCODE -ne 0){throw 'Engineering voice fixture compile failed'}
& $fixture $runtime $native (Join-Path $VoiceRoot 'routes.tsv') $BaselineRoutes $VoiceRoot 2>&1 | Tee-Object -FilePath (Join-Path $output 'VOICE60_RESULT.txt')
if($LASTEXITCODE -ne 0){throw 'Engineering voice fixture failed'}
