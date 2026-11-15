Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'RhodesProcess.psm1') -Force

function Read-RhodesVdfObject($Tokens,[ref]$Index,[bool]$Nested) {
    $result=@{}
    while($Index.Value -lt $Tokens.Count) {
        $key=$Tokens[$Index.Value];$Index.Value++
        if($key -eq '}'){if(-not $Nested){throw 'Unexpected closing VDF brace'};return $result}
        if($key -eq '{' -or $Index.Value -ge $Tokens.Count){throw 'Invalid VDF key/value pair'}
        $value=$Tokens[$Index.Value];$Index.Value++
        if($value -eq '{'){$value=Read-RhodesVdfObject $Tokens $Index $true}
        elseif($value -eq '}'){throw 'Missing VDF value'}
        if($result.ContainsKey($key)){throw ('Duplicate VDF key: '+$key)}
        $result[$key]=$value
    }
    if($Nested){throw 'Unclosed VDF object'}
    return $result
}
function ConvertFrom-RhodesVdf([string]$Text) {
    $tokens=New-Object System.Collections.Generic.List[string]
    foreach($m in [regex]::Matches($Text,'//[^\r\n]*|"((?:\\.|[^"\\])*)"|([{}])')) {
        if($m.Value.StartsWith('//')){continue}
        if($m.Groups[2].Success){$tokens.Add($m.Value)}
        else {$tokens.Add($m.Groups[1].Value.Replace('\\','\').Replace('\"','"'))}
    }
    $index=0
    return Read-RhodesVdfObject $tokens ([ref]$index) $false
}
function Get-RhodesSteamRoots([string[]]$SeedRoots=@(),[switch]$NoSystem) {
    $paths=New-Object System.Collections.Generic.List[string]
    foreach($root in $SeedRoots){if($root){$paths.Add($root)}}
    if(-not $NoSystem){
        foreach($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam','HKLM:\SOFTWARE\Valve\Steam')){
            $reg=Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
            if($null -eq $reg){continue}
            foreach($name in @('SteamPath','InstallPath')){if($reg.PSObject.Properties.Name -contains $name -and $reg.$name){$paths.Add([string]$reg.$name)}}
            if($reg.PSObject.Properties.Name -contains 'SteamExe' -and $reg.SteamExe){$paths.Add([IO.Path]::GetDirectoryName([string]$reg.SteamExe))}
        }
        foreach($process in @(Get-Process -Name steam -ErrorAction SilentlyContinue)){
            try{if($process.Path){$paths.Add([IO.Path]::GetDirectoryName($process.Path))}}catch{}
        }
        foreach($base in @(${env:ProgramFiles(x86)},$env:ProgramFiles)){if($base){$paths.Add((Join-Path $base 'Steam'))}}
    }
    $seen=@{}
    foreach($path in $paths){
        try{$full=[IO.Path]::GetFullPath($path).TrimEnd('\','/')}catch{continue}
        if($seen.ContainsKey($full) -or -not(Test-Path -LiteralPath $full -PathType Container)){continue}
        $seen[$full]=$true
        $full
    }
}
function Get-RhodesSteamInstallations([string[]]$SeedRoots=@(),[switch]$NoSystem) {
    $libraries=@{}
    foreach($steamRoot in @(Get-RhodesSteamRoots -SeedRoots $SeedRoots -NoSystem:$NoSystem)){
        $libraries[$steamRoot]=$steamRoot
        foreach($vdf in @((Join-Path $steamRoot 'steamapps\libraryfolders.vdf'),(Join-Path $steamRoot 'config\libraryfolders.vdf'))){
            if(-not(Test-Path -LiteralPath $vdf -PathType Leaf)){continue}
            try{
                $parsed=ConvertFrom-RhodesVdf ([IO.File]::ReadAllText($vdf))
                if(-not $parsed.ContainsKey('libraryfolders')){continue}
                foreach($key in $parsed['libraryfolders'].Keys){
                    if($key -notmatch '^\d+$'){continue}
                    $entry=$parsed['libraryfolders'][$key]
                    if($entry -is [Collections.IDictionary]){if(-not $entry.ContainsKey('path')){continue};$path=[string]$entry['path']}
                    else {$path=[string]$entry}
                    $full=[IO.Path]::GetFullPath($path).TrimEnd('\','/')
                    $libraries[$full]=$steamRoot
                }
            }catch{Write-Verbose ('Ignored unreadable Steam library list: '+$vdf+'; '+$_.Exception.Message)}
        }
    }
    $seen=@{}
    foreach($library in @($libraries.Keys|Sort-Object)){
        $acf=Join-Path $library 'steamapps\appmanifest_281610.acf'
        if(-not(Test-Path -LiteralPath $acf -PathType Leaf)){continue}
        try{
            $parsed=ConvertFrom-RhodesVdf ([IO.File]::ReadAllText($acf))
            $app=$parsed['AppState']
            if($null -eq $app -or [string]$app['appid'] -ne '281610' -or -not $app.ContainsKey('installdir')){continue}
            $dir=[string]$app['installdir']
            if([IO.Path]::IsPathRooted($dir) -or $dir.Contains(':') -or $dir -match '(^|[\\/])\.\.([\\/]|$)'){continue}
            $game=[IO.Path]::GetFullPath((Join-Path (Join-Path $library 'steamapps\common') $dir)).TrimEnd('\')
            if($seen.ContainsKey($game) -or -not(Test-Path -LiteralPath (Join-Path $game 'DesertsOfKharak64.exe') -PathType Leaf)){continue}
            $seen[$game]=$true
            [pscustomobject]@{gameRoot=$game;steamRoot=$libraries[$library];libraryRoot=$library;manifestPath=$acf;buildId=[string]$app['buildid'];appId=281610;discovery='Steam appmanifest'}
        }catch{Write-Verbose ('Ignored unreadable Steam game manifest: '+$acf+'; '+$_.Exception.Message)}
    }
}
function Get-RhodesSteamLaunchStatus {
    $shell=Get-RhodesProcessContext -ProcessId $PID
    $steam=@(Get-Process -Name steam -ErrorAction SilentlyContinue|Where-Object {$_.SessionId -eq $shell.SessionId})
    $status=[ordered]@{shellIntegrity=$shell.IntegrityLabel;steamRunning=($steam.Count -gt 0);steamInstances=$steam.Count;canRequestSteamLaunch=$false;reason='';registration='NOT_REQUIRED_FOR_STEAM_URI';authenticationVerified=$false}
    if($shell.IntegrityRid -ne 0x2000){$status.reason='Run normally, not as administrator. This launcher never elevates itself.';return [pscustomobject]$status}
    if($steam.Count -gt 1){$status.reason='Multiple Steam instances in this session; close the extra instance yourself.';return [pscustomobject]$status}
    if($steam.Count -eq 1){
        $context=Get-RhodesProcessContext -ProcessId $steam[0].Id
        if($context.IntegrityRid -ne 0x2000 -or $context.UserSid -ne $shell.UserSid){$status.reason='Steam must run as the same user with normal permissions; restart Steam normally.';return [pscustomobject]$status}
        $status.canRequestSteamLaunch=$true;$status.reason='Steam is running in this user session with normal permissions.'
    }else{
        foreach($root in @(Get-RhodesSteamRoots)){
            $exe=Join-Path $root 'steam.exe'
            foreach($key in @('HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers','HKLM:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers')){
                $reg=Get-Item -LiteralPath $key -ErrorAction SilentlyContinue
                if($null -ne $reg -and [string]$reg.GetValue($exe) -match '(?i)RUNASADMIN'){$status.reason='Steam.exe has a Run as administrator compatibility flag; remove it before starting.';return [pscustomobject]$status}
            }
        }
        $status.canRequestSteamLaunch=$true;$status.reason='Steam is not running; the Steam URI will open it and may ask you to log in.'
    }
    return [pscustomobject]$status
}
Export-ModuleMember -Function ConvertFrom-RhodesVdf,Get-RhodesSteamRoots,Get-RhodesSteamInstallations,Get-RhodesSteamLaunchStatus
