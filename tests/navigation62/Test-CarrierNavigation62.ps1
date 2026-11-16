param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$NativeCarrierJson,[string]$RuntimeDll)
$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
$candidate=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if([string]::IsNullOrEmpty($RuntimeDll)){$RuntimeDll=Join-Path $candidate 'out\EndfieldDoK.MapUnitLab.dll'}
$native=Join-Path $GameRoot 'Data\Managed'
$audit=Join-Path $candidate 'out\tests\navigation62'
[void][IO.Directory]::CreateDirectory($audit)
$fixture=Join-Path $audit 'CarrierNavigation62Fixture.exe'
$refs=@('mscorlib.dll','System.dll','System.Core.dll','BBI.Core.dll','BBI.Game.dll','BBI.Game.Data.dll','BBI.Unity.Game.Data.dll','UnityEngine.dll') | ForEach-Object {'/reference:'+(Join-Path $native $_)}
$refs+=('/reference:'+(Join-Path (Split-Path (Get-LegacyCsc) -Parent) 'System.Web.Extensions.dll'))
& (Get-LegacyCsc) /nologo /utf8output /noconfig /nostdlib+ /target:exe /warnaserror+ ('/out:'+$fixture) @refs (Join-Path $PSScriptRoot 'CarrierNavigation62Fixture.cs')
if($LASTEXITCODE -ne 0){throw 'Native navigation62 fixture compile failed'}
[IO.File]::WriteAllText(($fixture+'.config'),'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup><runtime><loadFromRemoteSources enabled="true"/></runtime></configuration>',[Text.UTF8Encoding]::new($false))
$original=[IO.Path]::GetFullPath($NativeCarrierJson)
$result=@(& $fixture $native $RuntimeDll $original 2>&1)
$code=$LASTEXITCODE
$result | Set-Content -LiteralPath (Join-Path $audit 'ACTUAL_DLL_NAVIGATION62_RESULT.txt') -Encoding UTF8
$result
if($code -ne 0){throw 'Actual candidate DLL/native navigation62 fixture failed'}
$report=($result -join [Environment]::NewLine) | ConvertFrom-Json
if($report.status -ne 'PASS_ACTUAL_DLL_NATIVE_NAVIGATION62' -or $report.donors.Count -ne 4){throw 'Unexpected navigation62 fixture identity'}
$report | Add-Member -NotePropertyName runtimeSha256 -NotePropertyValue ((Get-FileHash -LiteralPath $RuntimeDll -Algorithm SHA256).Hash.ToLowerInvariant())
$report | Add-Member -NotePropertyName nativeSourceJsonSha256 -NotePropertyValue ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant())
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $audit 'ACTUAL_DLL_NAVIGATION62.json') -Encoding UTF8
