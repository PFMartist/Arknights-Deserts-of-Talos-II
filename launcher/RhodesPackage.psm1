Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:ProgressReporter=$null

function Set-RhodesProgressReporter([scriptblock]$Reporter) {
    $script:ProgressReporter=$Reporter
}
function Write-RhodesPackageProgress([string]$Stage,[int]$Completed=0,[int]$Total=0,[string]$Detail='') {
    if($null -ne $script:ProgressReporter){
        try{& $script:ProgressReporter $Stage $Completed $Total $Detail}
        catch{Write-Warning ('启动器进度回调失败。'+$_.Exception.Message)}
    }
}

function Get-RhodesHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Get-RhodesStartupCritical($Entry) {
    $property=$Entry.PSObject.Properties['startupCritical']
    # Older payload entries may omit this hint. Hash them on startup so a
    # missing optional field cannot disable verification or prevent launch.
    if($null -eq $property){return $true}
    if($property.Value -isnot [bool]){throw ('Invalid startupCritical flag for payload entry: '+$Entry.path+'; expected a Boolean')}
    return $property.Value
}
function Resolve-RhodesChild([string]$Root,[string]$Relative) {
    if([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or ($Relative -split '[\\/]' | Where-Object {$_ -eq '..'})) {throw 'Unsafe package-relative path'}
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    $path=[IO.Path]::GetFullPath([IO.Path]::Combine($base+'\',$Relative))
    if(-not $path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Package path escaped its owner'}
    $cursor=$path
    while($cursor -and ($cursor -eq $base -or $cursor.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase))) {
        try{
            if(([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse points are not supported'}
        }catch [IO.FileNotFoundException]{}
        catch [IO.DirectoryNotFoundException]{}
        $cursor=[IO.Path]::GetDirectoryName($cursor)
    }
    return $path
}
function Read-RhodesRelease([string]$PackageRoot,[switch]$RequireReady) {
    Write-RhodesPackageProgress '读取发布包清单'
    $path=Resolve-RhodesChild $PackageRoot 'release.json'
    $m=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
    if($m.schemaVersion -ne 1 -or $m.product -ne 'RhodesDoK' -or $m.appId -ne 281610){throw 'Unsupported Rhodes release manifest'}
    if($RequireReady -and (-not $m.ready -or -not $m.capabilities.menuFirst -or -not $m.capabilities.skirmishFactionSelection -or $m.capabilities.laboratoryAutostart)){throw 'This is a development scaffold, not an installable menu/skirmish release. Runtime integration and acceptance are still required.'}
    $seen=@{}
    $completed=0
    $total=@($m.files).Count
    Write-RhodesPackageProgress '检查文件清单' 0 $total
    foreach($f in $m.files){
        $relative=([string]$f.path).Replace('\','/')
        $null=Get-RhodesStartupCritical $f
        if($seen.ContainsKey($relative) -or $f.sha256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Invalid or duplicate payload entry'}
        if($relative -notin @('winhttp.dll','doorstop_config.ini','.doorstop_version') -and -not $relative.StartsWith('BepInEx/',[StringComparison]::OrdinalIgnoreCase)){throw 'Payload may contain only loader and mod files, never native game binaries'}
        if($relative -match '(?i)MapUnitLab\.(autoprobe|unitsonly|lifecycle|hubregion|humanoid|rhodescarrier|menupreview)$'){throw 'Lab mode flags cannot be shipped in a skirmish release'}
        $null=Resolve-RhodesChild (Join-Path $PackageRoot 'payload') $relative
        $seen[$relative]=$true
        $completed++
        Write-RhodesPackageProgress '检查文件清单' $completed $total $relative
    }
    if($RequireReady -and @($m.files).Count -eq 0){throw 'Ready package has no payload'}
    return $m
}
function Assert-RhodesNative([string]$GameRoot,$Manifest) {
    $game=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    $completed=0
    $total=@($Manifest.nativeFiles).Count
    Write-RhodesPackageProgress '核对游戏版本' 0 $total $game
    foreach($entry in $Manifest.nativeFiles){
        $path=Resolve-RhodesChild $game $entry.path
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-RhodesHash $path) -ne $entry.sha256){throw ('Unsupported native game file: '+$entry.path)}
        $completed++
        Write-RhodesPackageProgress '核对游戏版本' $completed $total $entry.path
    }
    $apps=Split-Path -Parent (Split-Path -Parent $game)
    $acf=Join-Path $apps ('appmanifest_'+$Manifest.appId+'.acf')
    if(-not(Test-Path -LiteralPath $acf -PathType Leaf)){throw 'Select the actual Steam library game directory; Steam URI cannot launch an arbitrary isolated copy'}
    $text=[IO.File]::ReadAllText($acf)
    $dir=[regex]::Match($text,'"installdir"\s+"([^"\r\n]+)"')
    $build=[regex]::Match($text,'"buildid"\s+"([0-9]+)"')
    if(-not $dir.Success -or -not $build.Success -or $build.Groups[1].Value -ne [string]$Manifest.supportedSteamBuild){throw 'Unsupported or unreadable Steam app manifest'}
    $installed=[IO.Path]::GetFullPath((Join-Path (Join-Path $apps 'common') $dir.Groups[1].Value)).TrimEnd('\')
    if($installed -ne $game){throw 'Steam app manifest points to a different game directory'}
}
function Assert-RhodesStopped {
    $games=@(Get-Process -Name DesertsOfKharak64,DesertsOfKharak32,DesertsOfKharak -ErrorAction SilentlyContinue)
    if($games.Count){throw 'Close Deserts of Kharak before installing, repairing or removing the mod. No process will be stopped automatically.'}
}
function Assert-RhodesPayload([string]$PackageRoot,$Manifest) {
    $completed=0
    $total=@($Manifest.files).Count
    Write-RhodesPackageProgress '校验发布包文件' 0 $total
    foreach($file in $Manifest.files){
        $path=Resolve-RhodesChild (Join-Path $PackageRoot 'payload') $file.path
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-RhodesHash $path) -ne $file.sha256){throw ('Package payload mismatch: '+$file.path)}
        $completed++
        Write-RhodesPackageProgress '校验发布包文件' $completed $total $file.path
    }
}
function Read-RhodesReceipt([string]$GameRoot) {
    $path=Resolve-RhodesChild $GameRoot 'RhodesMod.install.json'
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'No Rhodes installation receipt in this game directory'}
    $r=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
    if($r.product -ne 'RhodesDoK' -or [IO.Path]::GetFullPath($r.gameRoot).TrimEnd('\') -ne [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')){throw 'Installation receipt owner does not match the selected game directory'}
    foreach($f in $r.files){$null=Resolve-RhodesChild $GameRoot $f.path}
    return $r
}
function Test-RhodesOwnedFiles([string]$GameRoot,$Manifest,[switch]$Full) {
    Write-RhodesPackageProgress '读取安装记录'
    $r=Read-RhodesReceipt $GameRoot
    if($r.version -ne $Manifest.version){throw 'Installed version differs from this package; use the matching launcher or update explicitly'}
    $receiptFiles=@{}
    foreach($f in $r.files){if($receiptFiles.ContainsKey($f.path)){throw 'Duplicate receipt entry'};$receiptFiles[$f.path]=$f.sha256}
    if($receiptFiles.Count -ne @($Manifest.files).Count){throw 'Installed manifest count differs'}
    $completed=0
    $total=@($Manifest.files).Count
    Write-RhodesPackageProgress '检查已安装文件' 0 $total
    foreach($file in $Manifest.files){
        if(-not $receiptFiles.ContainsKey($file.path) -or $receiptFiles[$file.path] -ne $file.sha256){throw 'Installed manifest content differs'}
        $path=Resolve-RhodesChild $GameRoot $file.path
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Installed file missing: '+$file.path)}
        # Normal start checks bootstrap binaries/config; full verification is explicit.
        if($Full -or (Get-RhodesStartupCritical $file)){if((Get-RhodesHash $path) -ne $file.sha256){throw ('Installed file changed: '+$file.path)}}
        $completed++
        Write-RhodesPackageProgress '检查已安装文件' $completed $total $file.path
    }
    return $r
}
function Assert-RhodesNoLabFlags([string]$GameRoot) {
    foreach($flag in @('autoprobe','unitsonly','lifecycle','hubregion','humanoid','rhodescarrier','menupreview')){
        if(Test-Path -LiteralPath (Resolve-RhodesChild $GameRoot ('BepInEx/MapUnitLab.'+$flag))){throw ('Legacy laboratory flag remains: '+$flag+'; use a clean installation instead of mixing packages')}
    }
}
function Test-RhodesInstalled([string]$GameRoot,$Manifest,[switch]$Full) {
    Assert-RhodesNative $GameRoot $Manifest
    $r=Test-RhodesOwnedFiles $GameRoot $Manifest -Full:$Full
    Assert-RhodesNoLabFlags $GameRoot
    return [pscustomobject]@{status='INSTALLED_FILES_VERIFIED';version=$r.version;full=[bool]$Full;runtimeVerified=$false;gameStarted=$false}
}
function Install-RhodesPayload([string]$PackageRoot,[string]$GameRoot,$Manifest) {
    Write-RhodesPackageProgress '检查安装条件' 0 0 $GameRoot
    Assert-RhodesStopped
    Assert-RhodesNative $GameRoot $Manifest
    Assert-RhodesNoLabFlags $GameRoot
    if(Test-Path -LiteralPath (Resolve-RhodesChild $GameRoot 'RhodesMod.install.json')){throw 'Installation already exists. Verify it, or remove the owned package before changing versions.'}
    Write-RhodesPackageProgress '检查目标文件冲突'
    foreach($file in $Manifest.files){if(Test-Path -LiteralPath (Resolve-RhodesChild $GameRoot $file.path)){throw ('Unowned existing mod/loader file: '+$file.path+'. Installer will not overwrite another mod.')}}
    Assert-RhodesPayload $PackageRoot $Manifest
    $created=New-Object System.Collections.Generic.List[object]
    $total=@($Manifest.files).Count
    try {
        Write-RhodesPackageProgress '复制并校验文件' 0 $total
        foreach($file in $Manifest.files){
            $target=Resolve-RhodesChild $GameRoot $file.path
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
            Copy-Item -LiteralPath (Resolve-RhodesChild (Join-Path $PackageRoot 'payload') $file.path) -Destination $target -ErrorAction Stop
            $created.Add([pscustomobject]@{path=$file.path;sha256=$file.sha256})
            if((Get-RhodesHash $target) -ne $file.sha256){throw ('Post-copy hash mismatch: '+$file.path)}
            Write-RhodesPackageProgress '复制并校验文件' $created.Count $total $file.path
        }
        $r=[ordered]@{product='RhodesDoK';version=$Manifest.version;gameRoot=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\');installedUtc=[DateTime]::UtcNow.ToString('o');files=@($created.ToArray());nativeGameFilesModified=$false;settingsModified=$false}
        Write-RhodesPackageProgress '保存安装记录'
        [IO.File]::WriteAllText((Resolve-RhodesChild $GameRoot 'RhodesMod.install.json'),($r|ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding($true)))
    } catch {
        # Roll back only exact files created by this invocation; never recurse-delete.
        foreach($f in $created){$p=Resolve-RhodesChild $GameRoot $f.path;if((Test-Path -LiteralPath $p -PathType Leaf) -and (Get-RhodesHash $p) -eq $f.sha256){Remove-Item -LiteralPath $p}}
        throw
    }
    return Test-RhodesInstalled $GameRoot $Manifest -Full
}
function Remove-RhodesPayload([string]$GameRoot,$Manifest) {
    Write-RhodesPackageProgress '检查卸载条件'
    Assert-RhodesStopped
    # Uninstallation must remain available after a Steam update. Validate only
    # this package's receipt and exact owned files, never the current game build.
    $r=Test-RhodesOwnedFiles $GameRoot $Manifest -Full
    $completed=0
    $total=@($r.files).Count
    Write-RhodesPackageProgress '卸载受管文件' 0 $total
    foreach($file in $r.files){
        Remove-Item -LiteralPath (Resolve-RhodesChild $GameRoot $file.path)
        $completed++
        Write-RhodesPackageProgress '卸载受管文件' $completed $total $file.path
    }
    Remove-Item -LiteralPath (Resolve-RhodesChild $GameRoot 'RhodesMod.install.json')
    return [pscustomobject]@{status='OWNED_MOD_FILES_REMOVED';nativeGameFilesModified=$false;settingsModified=$false;otherModFilesRemoved=$false}
}
function Repair-RhodesPayload([string]$PackageRoot,[string]$GameRoot,$Manifest) {
    Write-RhodesPackageProgress '检查修复条件'
    Assert-RhodesStopped
    Assert-RhodesNative $GameRoot $Manifest
    Assert-RhodesPayload $PackageRoot $Manifest
    Assert-RhodesNoLabFlags $GameRoot
    $r=Read-RhodesReceipt $GameRoot
    if($r.version -ne $Manifest.version -or @($r.files).Count -ne @($Manifest.files).Count){throw 'Use the matching release to repair this installation'}
    $owned=@{}
    foreach($file in $r.files){if($owned.ContainsKey($file.path)){throw 'Duplicate receipt entry'};$owned[$file.path]=$file.sha256}
    foreach($file in $Manifest.files){if(-not $owned.ContainsKey($file.path) -or $owned[$file.path] -ne $file.sha256){throw 'Receipt does not match the release'}}
    $backupRelative='RhodesModBackups/'+[DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff')
    $repaired=0
    $completed=0
    $total=@($Manifest.files).Count
    Write-RhodesPackageProgress '检查并修复文件' 0 $total
    foreach($file in $Manifest.files){
        $target=Resolve-RhodesChild $GameRoot $file.path
        $completed++
        Write-RhodesPackageProgress '检查并修复文件' $completed $total $file.path
        if((Test-Path -LiteralPath $target -PathType Leaf) -and (Get-RhodesHash $target) -eq $file.sha256){continue}
        if(Test-Path -LiteralPath $target){
            if(-not(Test-Path -LiteralPath $target -PathType Leaf)){throw 'An installed file has been replaced by a directory'}
            $backup=Resolve-RhodesChild $GameRoot ($backupRelative+'/'+$file.path)
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force|Out-Null
            Copy-Item -LiteralPath $target -Destination $backup -ErrorAction Stop
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
        Copy-Item -LiteralPath (Resolve-RhodesChild (Join-Path $PackageRoot 'payload') $file.path) -Destination $target -Force -ErrorAction Stop
        if((Get-RhodesHash $target) -ne $file.sha256){throw 'Repaired file hash mismatch'}
        $repaired++
    }
    $null=Test-RhodesInstalled $GameRoot $Manifest -Full
    return [pscustomobject]@{status='OWNED_MOD_FILES_REPAIRED';repaired=$repaired;backupRelative=$backupRelative;nativeGameFilesModified=$false}
}
Export-ModuleMember -Function *-Rhodes*
