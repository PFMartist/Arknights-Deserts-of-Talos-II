[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Menu','Inspect','Discover','Check','Status','Select','Install','Start','Verify','Repair','Remove')][string]$Action='Menu',
    [string]$GameRoot,
    [string]$StateRoot,
    [switch]$NoPause
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
Import-Module (Join-Path $PSScriptRoot 'RhodesPackage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RhodesSteam.psm1') -Force
if(-not $StateRoot){$StateRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RhodesDoK\Launcher'}
$StateRoot=[IO.Path]::GetFullPath($StateRoot)
$settingsPath=Join-Path $StateRoot 'local-install.json'
$script:selectedGameRoot=$GameRoot
$script:interactive=($Action -eq 'Menu' -or -not $NoPause)
$script:lastLog=$null

function Save-RhodesSelection([string]$Path) {
    New-Item -ItemType Directory -Path $StateRoot -Force|Out-Null
    [IO.File]::WriteAllText($settingsPath,(@{gameRoot=$Path;schemaVersion=1}|ConvertTo-Json),(New-Object Text.UTF8Encoding($true)))
}
function Select-RhodesGameRoot([switch]$Reselect) {
    if(-not $Reselect -and $script:selectedGameRoot){return [IO.Path]::GetFullPath($script:selectedGameRoot.Trim('"')).TrimEnd('\')}
    if(-not $Reselect -and (Test-Path -LiteralPath $settingsPath)){
        try{
            $saved=Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8|ConvertFrom-Json
            if($saved.gameRoot -and (Test-Path -LiteralPath (Join-Path $saved.gameRoot 'DesertsOfKharak64.exe'))){$script:selectedGameRoot=[string]$saved.gameRoot;return $script:selectedGameRoot}
        }catch{Write-Warning '已保存的目录不可用，将重新查找。'}
    }
    $found=@(Get-RhodesSteamInstallations)
    if($found.Count -eq 1 -and -not $Reselect){$script:selectedGameRoot=$found[0].gameRoot;return $script:selectedGameRoot}
    if(-not $script:interactive){throw '未找到唯一可用的 Steam 安装目录。请运行双击菜单选择目录，或使用 -GameRoot 参数。'}
    Write-Host '选择 Steam 已安装的《卡拉克沙漠》目录：'
    for($i=0;$i -lt $found.Count;$i++){Write-Host (('{0}  {1}  [build {2}]' -f ($i+1),$found[$i].gameRoot,$found[$i].buildId))}
    Write-Host 'M  手动输入目录    0  取消'
    $choice=(Read-Host '选择').Trim()
    if($choice -eq '0'){throw '已取消，未修改游戏。'}
    $number=0
    if([int]::TryParse($choice,[ref]$number) -and $number -ge 1 -and $number -le $found.Count){$script:selectedGameRoot=$found[$number-1].gameRoot}
    elseif($choice -eq 'M'){$script:selectedGameRoot=(Read-Host '粘贴 Steam 游戏目录（不是开发隔离副本）').Trim().Trim('"')}
    else{throw '未选择有效目录。'}
    if([string]::IsNullOrWhiteSpace($script:selectedGameRoot)){throw '目录为空。'}
    $script:selectedGameRoot=[IO.Path]::GetFullPath($script:selectedGameRoot).TrimEnd('\')
    return $script:selectedGameRoot
}
function Write-RhodesLocalLog([string]$Operation,$Result,[string]$ErrorText) {
    try{
        $logs=Join-Path $StateRoot 'logs'
        New-Item -ItemType Directory -Path $logs -Force|Out-Null
        $path=Join-Path $logs ([DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff')+'_'+$Operation+'.json')
        $record=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');operation=$Operation;packageRoot=$PSScriptRoot;gameRoot=$script:selectedGameRoot;host=$PSVersionTable.PSVersion.ToString();result=$Result;error=$ErrorText;nativeAuthenticationTested=$false}
        [IO.File]::WriteAllText($path,($record|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($true)))
        $script:lastLog=$path
    }catch{Write-Warning ('无法保存启动器记录：'+$_.Exception.Message)}
}
function Get-RhodesReleaseSummary($Release) {
    $channel='unclassified'
    if($Release.PSObject.Properties.Name -contains 'releaseChannel'){$channel=[string]$Release.releaseChannel}
    $acceptance=$null
    if($Release.PSObject.Properties.Name -contains 'acceptance'){$acceptance=$Release.acceptance}
    $blockers=@()
    if($Release.PSObject.Properties.Name -contains 'blockers'){$blockers=@($Release.blockers)}
    [pscustomobject]@{version=$Release.version;releaseChannel=$channel;ready=[bool]$Release.ready;capabilities=$Release.capabilities;acceptance=$acceptance;payloadFiles=@($Release.files).Count;blockers=$blockers;gameStarted=$false;gameModified=$false}
}
function Invoke-RhodesOperation([string]$Operation) {
    $release=Read-RhodesRelease $PSScriptRoot
    switch($Operation){
        'Inspect' {return Get-RhodesReleaseSummary $release}
        'Discover' {return [pscustomobject]@{installations=@(Get-RhodesSteamInstallations);gameModified=$false;gameStarted=$false}}
        'Check' {
            $found=@(Get-RhodesSteamInstallations)
            $context=$null;$contextError=$null
            try{$context=Get-RhodesSteamLaunchStatus}catch{$contextError=$_.Exception.Message}
            return [pscustomobject]@{release=(Get-RhodesReleaseSummary $release);installations=$found;launchContext=$context;contextError=$contextError;gameModified=$false;gameStarted=$false}
        }
        'Status' {
            $game=Select-RhodesGameRoot
            $path=Resolve-RhodesChild $game 'BepInEx/MapUnitLab.Runtime.log'
            if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return [pscustomobject]@{status='NO_RUNTIME_LOG';path=$path;message='尚未发现运行日志；启动器没有据此推断游戏是否运行。'}}
            $lines=@(Get-Content -LiteralPath $path -Tail 400 -Encoding UTF8|Where-Object {$_ -match 'RHODES_SKIRMISH_'}|Select-Object -Last 12)
            return [pscustomobject]@{status='EXISTING_RUNTIME_LOG';modifiedUtc=(Get-Item -LiteralPath $path).LastWriteTimeUtc.ToString('o');path=$path;stages=$lines;message='这是现存日志，可能来自上一局；请核对时间。MENU_READY不代表遭遇战或第二局已经通过验收。'}
        }
        'Select' {
            $game=Select-RhodesGameRoot -Reselect
            Assert-RhodesNative $game $release
            Save-RhodesSelection $game
            return [pscustomobject]@{status='GAME_DIRECTORY_SAVED';gameRoot=$game;gameModified=$false;gameStarted=$false}
        }
    }
    $release=Read-RhodesRelease $PSScriptRoot -RequireReady
    $game=Select-RhodesGameRoot
    $script:selectedGameRoot=$game
    switch($Operation){
        'Install' {
            if($PSCmdlet.ShouldProcess($game,'Install verified Rhodes mod files')){
                $result=Install-RhodesPayload $PSScriptRoot $game $release
                Save-RhodesSelection $game
                return $result
            }
        }
        'Verify' {return Test-RhodesInstalled $game $release -Full}
        'Repair' {if($PSCmdlet.ShouldProcess($game,'Back up changed owned files and repair this release')){return Repair-RhodesPayload $PSScriptRoot $game $release}}
        'Remove' {if($PSCmdlet.ShouldProcess($game,'Remove only unchanged files owned by this release')){return Remove-RhodesPayload $game $release}}
        'Start' {
            $null=Test-RhodesInstalled $game $release
            Assert-RhodesStopped
            $context=Get-RhodesSteamLaunchStatus
            if(-not $context.canRequestSteamLaunch){throw $context.reason}
            if($PSCmdlet.ShouldProcess('Steam app 281610','Launch installed game through Steam')){
                # Steam supplies application/login context. Never direct-start an isolated
                # EXE, wait on stale PID metadata, or alter authentication code.
                Start-Process -FilePath 'steam://rungameid/281610' -WindowStyle Hidden
                Save-RhodesSelection $game
                return [pscustomobject]@{status='STEAM_LAUNCH_REQUESTED';expectedGameRoot=$game;authenticationVerified=$false;menuVerified=$false;matchVerified=$false;message='已交给 Steam 启动；如 Steam 要求登录，请正常登录。请求成功不等于游戏已进入菜单。'}
            }
        }
    }
}
function Show-RhodesMenu {
    $release=Read-RhodesRelease $PSScriptRoot
    $summary=Get-RhodesReleaseSummary $release
    Write-Host ''
    Write-Host '罗德岛 Mod 启动器' -ForegroundColor Yellow
    Write-Host ('版本：'+$release.version)
    if($summary.releaseChannel -eq 'skirmish_preview'){Write-Host '类型：遭遇战预览版（完整验收状态见“检查”）' -ForegroundColor Cyan}
    elseif($summary.releaseChannel -eq 'trial'){Write-Host '类型：试验包，不代表正式遭遇战支持' -ForegroundColor Cyan}
    else{Write-Host ('类型：'+$summary.releaseChannel)}
    if(-not $release.ready){Write-Host '当前包尚未包含可安装的已冻结运行文件；安装和启动保持关闭。' -ForegroundColor Yellow}
    Write-Host '1  安装 Mod（首次使用）'
    Write-Host '2  启动 Mod（Steam → 游戏菜单）'
    Write-Host '3  检查安装文件'
    Write-Host '4  修复 Mod 文件'
    Write-Host '5  卸载 Mod（保留游戏、设置与其他文件）'
    Write-Host '6  选择／更改 Steam 游戏目录'
    Write-Host '7  检查启动条件和版本状态（不启动）'
    Write-Host '8  查看运行阶段日志（可能是上次记录）'
    Write-Host '0  退出'
}

$exitCode=0
try{
    if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1){throw '请使用随包 CMD 入口，它会选择 Windows PowerShell 5.1。'}
    if($Action -eq 'Menu'){
        while($true){
            Show-RhodesMenu
            $choice=(Read-Host '请选择').Trim()
            if($choice -eq '0'){break}
            $actions=@{'1'='Install';'2'='Start';'3'='Verify';'4'='Repair';'5'='Remove';'6'='Select';'7'='Check';'8'='Status'}
            if(-not $actions.ContainsKey($choice)){Write-Host '请输入 0 至 8。';continue}
            $operation=$actions[$choice]
            try{
                $result=Invoke-RhodesOperation $operation
                Write-RhodesLocalLog $operation $result $null
                $result|ConvertTo-Json -Depth 8|Write-Host
            }catch{
                Write-RhodesLocalLog $operation $null $_.Exception.Message
                Write-Host ('未完成：'+$_.Exception.Message) -ForegroundColor Red
            }
            if($script:lastLog){Write-Host ('记录：'+$script:lastLog)}
            $null=Read-Host '按回车返回菜单'
        }
    }else{
        $result=Invoke-RhodesOperation $Action
        Write-RhodesLocalLog $Action $result $null
        $result|ConvertTo-Json -Depth 8
    }
}catch{
    Write-RhodesLocalLog $Action $null $_.Exception.Message
    Write-Host ('未完成：'+$_.Exception.Message) -ForegroundColor Red
    if($script:lastLog){Write-Host ('记录：'+$script:lastLog)}
    $exitCode=1
}finally{
    if($Action -ne 'Menu' -and -not $NoPause){$null=Read-Host '按回车关闭启动器'}
}
exit $exitCode
