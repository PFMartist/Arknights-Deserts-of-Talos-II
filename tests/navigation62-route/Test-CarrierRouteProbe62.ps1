param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$MapsTsv)
$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
$candidate=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$audit=Join-Path $candidate 'out\tests\navigation62-route'
New-Item -ItemType Directory -Force -Path $audit|Out-Null
$native=Join-Path $GameRoot 'Data\Managed'
$references=@('mscorlib.dll','System.dll','System.Core.dll','BBI.Core.dll','BBI.Game.dll','BBI.Game.Data.dll')|ForEach-Object {'/reference:'+(Join-Path $native $_)}
$binary=Join-Path $audit 'CarrierRouteProbe62.exe'
& (Get-LegacyCsc) /nologo /noconfig /nostdlib+ /target:exe /warnaserror+ ('/out:'+$binary) @references (Join-Path $PSScriptRoot 'CarrierRouteProbe62.cs')
if($LASTEXITCODE -ne 0){throw 'Route probe compilation failed'}
$raw=& $binary ([IO.Path]::GetFullPath($MapsTsv)) $native
if($LASTEXITCODE -ne 0){throw 'Native route probe failed'}
$result=$raw|ConvertFrom-Json
if($result.mapCount -ne 14 -or $result.totalPairs -ne 4176 -or $result.UnityExecuted -ne $false -or $result.gameStarted -ne $false){throw 'Unexpected route probe scope'}
if($result.totalRequestedGoalReached5 -ne 3679 -or $result.totalRequestedGoalReached305 -ne 1745){throw 'Original native fixture results differ; inspect before accepting a new baseline'}
$teeth=@($result.maps|Where-Object {$_.name -eq 'Navmesh_MP05'})
if($teeth.Count -ne 1 -or $teeth[0].requestedGoalReached5 -ne 300 -or $teeth[0].requestedGoalReached305 -ne 42){throw 'Teeth route result differs'}
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $audit 'native_14_maps_5_vs_305.json') -Encoding UTF8
@('BBI.Core.dll','BBI.Game.dll','BBI.Game.Data.dll')|ForEach-Object {Get-FileHash -LiteralPath (Join-Path $native $_) -Algorithm SHA256}|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $audit 'native_assembly_hashes.json') -Encoding UTF8
[pscustomobject]@{status='PASS_NATIVE_ROUTE_PROFILE_COMPARISON';maps=$result.mapCount;pairs=$result.totalPairs;requestedGoalReached5=$result.totalRequestedGoalReached5;requestedGoalReached305=$result.totalRequestedGoalReached305;teethGoalReached5=$teeth[0].requestedGoalReached5;teethGoalReached305=$teeth[0].requestedGoalReached305;UnityExecuted=$false;runtimeRouteAcceptance=$false}|ConvertTo-Json
