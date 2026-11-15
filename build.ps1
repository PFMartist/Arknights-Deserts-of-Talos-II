[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$BepInExRoot='',
    [string]$OutRoot=''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop

# PFMartist; AI collaboration: GPT (Codex).
# Compile against local, legally supplied game/loader references. Never copy
# those inputs into the repository, install output, or start/stop the game.
function Full-Path([string]$Path){
    $full=[IO.Path]::GetFullPath($Path)
    if($full.Length -gt [IO.Path]::GetPathRoot($full).Length){return $full.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)}
    return $full
}
function Hash-File([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Require-File([string]$Path){if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){throw ('Required local reference or source missing: '+$Path)}}
function Require-PlainOutput([string]$Path){
    $cursor=[IO.Path]::GetFullPath($Path)
    while(-not[string]::IsNullOrEmpty($cursor)){
        if(Test-Path -LiteralPath $cursor){
            if(((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Output through a reparse point is not supported'}
        }
        $parent=[IO.Path]::GetDirectoryName($cursor)
        if($parent -eq $cursor){break};$cursor=$parent
    }
}
function Write-Response([string]$Path,[string[]]$Arguments){
    $lines=@($Arguments|ForEach-Object {if($_.Contains('"')){throw 'Unexpected quote in a compiler argument'};'"'+$_+'"'})
    [IO.File]::WriteAllLines($Path,[string[]]$lines,(New-Object Text.UTF8Encoding($true)))
}

if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not[Environment]::Is64BitOperatingSystem){throw 'This build requires Windows x64'}
$repository=Full-Path $PSScriptRoot
$game=Full-Path $GameRoot
$managed=Join-Path $game 'Data\Managed'
$source=Join-Path $repository 'src'
if([string]::IsNullOrWhiteSpace($BepInExRoot)){$BepInExRoot=Join-Path $game 'BepInEx'}
$loader=Full-Path $BepInExRoot
$core=$null
foreach($candidate in @((Join-Path $loader 'BepInEx\core'),(Join-Path $loader 'core'),$loader)){
    if(Test-Path -LiteralPath (Join-Path $candidate 'BepInEx.Preloader.dll') -PathType Leaf){$core=Full-Path $candidate;break}
}
if($null -eq $core){throw 'BepInExRoot must contain BepInEx/core, core, or the core DLLs directly'}
if([string]::IsNullOrWhiteSpace($OutRoot)){$OutRoot=Join-Path $repository 'out'}
$output=Full-Path $OutRoot
foreach($protected in @($game,$managed,$source,$core)){
    if($output.Equals($protected,[StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($protected+'\',[StringComparison]::OrdinalIgnoreCase)){
        throw 'OutRoot must be separate from the game, sources, and external loader references'
    }
}
if($output.Equals($repository,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a separate output directory, not the repository root'}
if($output.Equals([IO.Path]::GetPathRoot($output),[StringComparison]::OrdinalIgnoreCase)){throw 'OutRoot must not be a drive root'}
Require-PlainOutput $output
foreach($name in @('Runtime.cs','Patcher.cs','RhodesSkirmishMenuPatcher.cs')){Require-File (Join-Path $source $name)}

$windowsDirectory=[Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
$compiler=Join-Path $windowsDirectory 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Require-File $compiler
$standardNames=@('mscorlib.dll','System.dll','System.Core.dll')
$runtimeNames=@('UnityEngine.dll','Fabric.Core.dll','NGUI.dll','BBI.Core.dll','BBI.Game.dll','BBI.Game.Data.dll','BBI.Unity.Core.dll','BBI.Unity.Core.Data.dll','BBI.Unity.Game.Data.dll','BBI.Unity.Game.dll')
$patcherNames=@('Mono.Cecil.dll','BepInEx.dll','BepInEx.Preloader.dll')
$inputHashes=[ordered]@{}
foreach($name in @($standardNames+$runtimeNames)){$path=Join-Path $managed $name;Require-File $path;$inputHashes['game/Data/Managed/'+$name]=Hash-File $path}
foreach($name in $patcherNames){$path=Join-Path $core $name;Require-File $path;$inputHashes['loader/core/'+$name]=Hash-File $path}
$supportedNative=@{
    'BBI.Game.dll'='1216502eff7958a99fe305b439f81709bf340610c782ce449929d13b36ae45cc'
    'BBI.Game.Data.dll'='24a3fbf19e5435ec24295554e12f0ad2451d722623daaccc03bea280b33e9d40'
    'BBI.Unity.Game.dll'='b4c2c7bdbf9737140ac18d6191e50ddd27030a669f050374e8f7e3da8c6ec0e5'
    'BBI.Unity.Game.Data.dll'='f3a60df1ab8377492451181a957d1f1df7e3d47265bc0221e1dee83107e14c40'
}
foreach($name in $supportedNative.Keys){if($inputHashes['game/Data/Managed/'+$name] -cne $supportedNative[$name]){throw ('Unsupported native build: '+$name)}}
$sources=@(Get-ChildItem -LiteralPath $source -Filter '*.cs' -File | Sort-Object Name)
$sourceHashes=[ordered]@{};foreach($item in $sources){$sourceHashes[$item.Name]=Hash-File $item.FullName}
$runtimeSources=@($sources|Where-Object Name -NotIn @('Patcher.cs','RhodesSkirmishMenuPatcher.cs')|ForEach-Object FullName)
$patcherSources=@((Join-Path $source 'Patcher.cs'),(Join-Path $source 'RhodesSkirmishMenuPatcher.cs'))
if($runtimeSources.Count -lt 1){throw 'No Runtime source files were found'}
$null=[IO.Directory]::CreateDirectory($output)
$common=@('/nologo','/utf8output','/nostdlib+','/target:library','/optimize+','/warn:4','/warnaserror+')
$standard=@($standardNames|ForEach-Object {'/reference:'+(Join-Path $managed $_)})
$runtimeRefs=@($runtimeNames|ForEach-Object {'/reference:'+(Join-Path $managed $_)})
$patcherRefs=@($patcherNames|ForEach-Object {'/reference:'+(Join-Path $core $_)})
$runtimeDll=Join-Path $output 'EndfieldDoK.MapUnitLab.dll'
$patcherDll=Join-Path $output 'EndfieldDoK.MapUnitLab.Patcher.dll'
$runtimeResponse=Join-Path $output 'RuntimeCompileArguments.rsp'
$patcherResponse=Join-Path $output 'PatcherCompileArguments.rsp'
foreach($target in @($runtimeDll,$patcherDll,$runtimeResponse,$patcherResponse,(Join-Path $output 'build-info.json'))){Require-PlainOutput $target}
Write-Response $runtimeResponse ($common+$standard+$runtimeRefs+@('/out:'+$runtimeDll)+$runtimeSources)
Write-Response $patcherResponse ($common+$standard+$patcherRefs+@('/out:'+$patcherDll)+$patcherSources)
# /noconfig belongs on the command line; old csc ignores it in response files.
& $compiler /noconfig ('@'+$runtimeResponse)
if($LASTEXITCODE -ne 0){throw 'Runtime compilation failed'}
& $compiler /noconfig ('@'+$patcherResponse)
if($LASTEXITCODE -ne 0){throw 'Patcher compilation failed'}
foreach($item in $sources){if((Hash-File $item.FullName) -cne $sourceHashes[$item.Name]){throw 'A source file changed during compilation; rebuild from one stable snapshot'}}
foreach($name in @($standardNames+$runtimeNames)){
    if((Hash-File (Join-Path $managed $name)) -cne $inputHashes['game/Data/Managed/'+$name]){throw 'A game compiler reference changed during compilation'}
}
foreach($name in $patcherNames){if((Hash-File (Join-Path $core $name)) -cne $inputHashes['loader/core/'+$name]){throw 'A loader compiler reference changed during compilation'}}
$info=[ordered]@{
    product='Arknights: Deserts of Talos Ⅱ'
    status='PASS_STRICT_SOURCE_BUILD_ONLY'
    author='PFMartist'
    aiCollaboration='GPT (Codex)'
    builtUtc=[DateTime]::UtcNow.ToString('o')
    supportedSteamBuild='12339551'
    unityVersion='5.2.2f1'
    legacyGameStandardReferences=$true
    warningsAsErrors=$true
    runtimeSourceFiles=$runtimeSources.Count
    patcherSourceFiles=$patcherSources.Count
    patchTargets=@('BBI.Unity.Game.dll','BBI.Game.dll','BBI.Unity.Game.Data.dll')
    sourceSha256=$sourceHashes
    inputSha256=$inputHashes
    outputSha256=[ordered]@{'EndfieldDoK.MapUnitLab.dll'=(Hash-File $runtimeDll);'EndfieldDoK.MapUnitLab.Patcher.dll'=(Hash-File $patcherDll)}
    assetsIncluded=$false
    installed=$false
    gameStarted=$false
    gameplayOrVisualAcceptance=$false
}
[IO.File]::WriteAllText((Join-Path $output 'build-info.json'),($info|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($true)))
Write-Output ('Built Runtime and Patcher in '+$output)
Write-Output 'PASS_STRICT_SOURCE_BUILD_ONLY: resources, installation and gameplay require separate validation.'
