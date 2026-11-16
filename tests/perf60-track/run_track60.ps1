param([Parameter(Mandatory=$true)][string]$AssetRoot)
$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
Import-Module Microsoft.PowerShell.Utility
$candidateRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$audit=Join-Path $candidateRoot 'out\tests\perf60-track'
New-Item -ItemType Directory -Path $audit -Force | Out-Null
$loader=Get-Content -LiteralPath (Join-Path $candidateRoot 'src\FactionTrackLoader.cs') -Raw
$start=$loader.IndexOf('    public sealed class FactionTrackLiveBelt',[StringComparison]::Ordinal)
if($start -lt 0){throw 'Exact production live rig marker not found'}
# Compile the unchanged production live classes with setter-counting Unity
# stubs. Only the Unity-dependent mesh import/template half is excluded.
$subset='using System; using System.Collections.Generic; using EndfieldDoK.ModelIntake; using UnityEngine; namespace EndfieldDoK.MapUnitLab {'+[Environment]::NewLine+$loader.Substring($start)
$subsetPath=Join-Path $audit 'ProductionTrackLiveSubset60.cs'
[IO.File]::WriteAllText($subsetPath,$subset,(New-Object Text.UTF8Encoding($false)))
$fixture=Join-Path $audit 'TrackTierFixture60.exe'
$sources=@($subsetPath,(Join-Path $candidateRoot 'src\TrackAnimationPolicy60.cs'),(Join-Path $candidateRoot 'src\FactionTrackPackage.cs'),(Join-Path $candidateRoot 'src\RhodesModelPackage.cs'),(Join-Path $candidateRoot 'src\RhodesResourceContent.cs'),(Join-Path $PSScriptRoot 'UnityTrackWriteStubs.cs'),(Join-Path $PSScriptRoot 'TrackTierFixture60.cs'))
& (Get-LegacyCsc) /nologo /utf8output /warn:4 /warnaserror+ ("/out:"+$fixture) @sources
if($LASTEXITCODE -ne 0){throw 'Track tier fixture compile failed'}
$packages=@('RI_U05_SUPPRESSION','RI_U06_RAILGUN','RI_U07_AIR_DEFENCE','RI_U09_ARTILLERY')|ForEach-Object {Join-Path (Join-Path $AssetRoot $_) 'faction_tracks.ftk'}
& $fixture @packages 2>&1 | Tee-Object -FilePath (Join-Path $audit 'TRACK60_RESULT.txt')
if($LASTEXITCODE -ne 0){throw 'Track tier fixture failed'}
@($packages+(Join-Path $candidateRoot 'src\FactionTrackLoader.cs')+(Join-Path $candidateRoot 'src\TrackAnimationPolicy60.cs')+(Join-Path $candidateRoot 'src\FactionUnitModels.cs')+(Join-Path $candidateRoot 'out\EndfieldDoK.MapUnitLab.dll')) | Where-Object {Test-Path -LiteralPath $_} | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $audit 'VERIFIED_TRACK_FILES_SHA256.json') -Encoding UTF8
