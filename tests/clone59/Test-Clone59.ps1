[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$BepInExRoot='',
    [string]$RuntimeDll='',
    [string]$PatchedDataDll='',
    [string]$OutRoot=''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$game=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
if([string]::IsNullOrWhiteSpace($BepInExRoot)){$BepInExRoot=Join-Path $game 'BepInEx'}
$loader=[IO.Path]::GetFullPath($BepInExRoot).TrimEnd('\')
$cecil=$null
foreach($folder in @((Join-Path $loader 'BepInEx\core'),(Join-Path $loader 'core'),$loader)){
    $candidate=Join-Path $folder 'Mono.Cecil.dll'
    if(Test-Path -LiteralPath $candidate -PathType Leaf){$cecil=$candidate;break}
}
if($null -eq $cecil){throw 'Supply a local BepInEx core containing Mono.Cecil.dll'}
$native=Join-Path $game 'Data\Managed\BBI.Unity.Game.Data.dll'
if(-not(Test-Path -LiteralPath $native -PathType Leaf)){throw 'GameRoot must contain the locally supplied native Data/Managed references'}
if($PatchedDataDll -and -not$RuntimeDll){throw 'PatchedDataDll also requires RuntimeDll'}
if([string]::IsNullOrWhiteSpace($OutRoot)){$OutRoot=Join-Path $repository 'out\tests\clone59'}
$output=[IO.Path]::GetFullPath($OutRoot).TrimEnd('\')
foreach($protected in @($game,$loader,(Join-Path $repository 'src'),$PSScriptRoot)){
    if($output -ieq $protected -or $output.StartsWith($protected+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Test output must be separate from the game, references and source files'}
}
if($output -ieq $repository -or $output -ieq [IO.Path]::GetPathRoot($output).TrimEnd('\')){throw 'Use a separate local output directory'}
$null=[IO.Directory]::CreateDirectory($output)
# Copy only the developer's externally supplied Cecil dependency to ignored
# local test output. It is never part of the Git source tree.
Copy-Item -LiteralPath $cecil -Destination (Join-Path $output 'Mono.Cecil.dll') -Force
$compiler=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $output 'Clone59Fixture.exe'
& $compiler /nologo /warn:4 /warnaserror+ /target:exe ('/reference:'+$cecil) ('/out:'+$exe) (Join-Path $PSScriptRoot 'Clone59Fixture.cs') (Join-Path $repository 'src\RhodesAssetCreationPolicy59.cs')
if($LASTEXITCODE -ne 0){throw 'Clone fixture compilation failed'}
$arguments=@($native)
if($RuntimeDll){$arguments+=,[IO.Path]::GetFullPath($RuntimeDll)}
if($PatchedDataDll){$arguments+=,[IO.Path]::GetFullPath($PatchedDataDll)}
$result=& $exe @arguments
if($LASTEXITCODE -ne 0){throw 'Clone policy or metadata fixture failed'}
[IO.File]::WriteAllText((Join-Path $output 'result.json'),([string]$result+[Environment]::NewLine),(New-Object Text.UTF8Encoding($true)))
$result
