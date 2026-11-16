param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [string]$RuntimeDll='',
 [string]$CarrierIni=''
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'TestSupport.psm1') -Force
Import-Module Microsoft.PowerShell.Utility
$candidateRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$audit=Join-Path $candidateRoot 'out\tests\power60'
New-Item -ItemType Directory -Path $audit -Force | Out-Null
$mod=Get-TestRuntime $candidateRoot $RuntimeDll
$NativeManaged=Join-Path $GameRoot 'Data\Managed'
$Reference59=Join-Path $PSScriptRoot 'reference59'
if(-not(Test-Path -LiteralPath $mod)){throw 'Root must compile the complete candidate before this native fixture runs.'}
$compiler=(Get-LegacyCsc)
$baseline=Join-Path $audit 'Power59Reference.dll'
& $compiler /nologo /utf8output /target:library /warn:4 /warnaserror+ ("/out:"+$baseline) (Join-Path $Reference59 'RhodesPowerProfile.cs') (Join-Path $Reference59 'RhodesThermalProfile.cs')
if($LASTEXITCODE -ne 0){throw 'Frozen Hotfix59 reference compile failed'}
$fixture=Join-Path $audit 'NativeCarrierBalanceFixture60.exe'
& $compiler /nologo /utf8output /warn:4 /warnaserror+ ("/out:"+$fixture) (Join-Path $PSScriptRoot 'NativeCarrierBalanceFixture60.cs')
if($LASTEXITCODE -ne 0){throw 'Native carrier balance fixture compile failed'}
[IO.File]::WriteAllText(($fixture+'.config'),'<configuration><runtime><loadFromRemoteSources enabled="true" /></runtime></configuration>',(New-Object Text.UTF8Encoding($false)))
$iniArgument=if($CarrierIni){$CarrierIni}else{'-'}
& $fixture $NativeManaged $mod $iniArgument $baseline 2>&1 | Tee-Object -FilePath (Join-Path $audit 'NATIVE_POWER60_RESULT.txt')
if($LASTEXITCODE -ne 0){throw 'Native carrier balance fixture FAILED'}
$files=@($mod,(Join-Path $candidateRoot 'src\RhodesPowerProfile.cs'),(Join-Path $candidateRoot 'src\RhodesCarrierUnits.Power.cs'),(Join-Path $PSScriptRoot 'NativeCarrierBalanceFixture60.cs'))
if($CarrierIni){$files+=$CarrierIni}
$hashes=@($files | Where-Object {Test-Path -LiteralPath $_} | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
$hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $audit 'VERIFIED_FILES_SHA256.json') -Encoding UTF8
$hashes
