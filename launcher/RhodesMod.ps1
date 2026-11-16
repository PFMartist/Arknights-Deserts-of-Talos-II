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
$script:launcherVersion='62.4-20261117'
$script:tracePath=$null
$script:logOperation=$null
$script:operationStarted=[DateTime]::UtcNow
$script:lastProgressStage=''
$script:lastProgressUtc=[DateTime]::MinValue
$script:operationActive=$false

function Start-RhodesOperationLog([string]$Operation) {
    $script:operationActive=$true
    $script:operationStarted=[DateTime]::UtcNow
    $script:logOperation=$Operation
    $script:lastProgressStage='开始操作'
    $script:lastProgressUtc=[DateTime]::MinValue
    $script:lastLog=$null
    $script:tracePath=$null
    try{
        $logs=Join-Path $StateRoot 'logs'
        New-Item -ItemType Directory -Path $logs -Force|Out-Null
        $name=$script:operationStarted.ToString('yyyyMMdd_HHmmss_fff')+'_'+$Operation
        $script:lastLog=Join-Path $logs ($name+'.json')
        $script:tracePath=Join-Path $logs ($name+'.progress.jsonl')
    }catch{Write-Warning ('无法创建启动器日志目录。'+$_.Exception.Message)}
    Write-RhodesLocalLog $Operation $null $null -Status 'RUNNING'
    if($script:interactive -and $script:tracePath){Write-Host ('实时进度记录保存在 '+$script:tracePath)}
    Write-RhodesOperationProgress '开始操作' 0 0 $Operation
}
function Write-RhodesOperationProgress([string]$Stage,[int]$Completed=0,[int]$Total=0,[string]$Detail='') {
    if(-not $script:operationActive){return}
    $now=[DateTime]::UtcNow
    if($Stage -eq $script:lastProgressStage -and $Completed -ne $Total -and ($now-$script:lastProgressUtc).TotalSeconds -lt 1){return}
    $script:lastProgressStage=$Stage
    $script:lastProgressUtc=$now
    $count=if($Total -gt 0){' '+$Completed+'/'+$Total}else{''}
    if($script:interactive){Write-Host ('['+[DateTime]::Now.ToString('HH:mm:ss')+'] '+$Stage+$count+' '+$Detail)}
    if(-not $script:tracePath){return}
    try{
        $record=[ordered]@{utc=$now.ToString('o');operation=$script:logOperation;stage=$Stage;completed=$Completed;total=$Total;detail=$Detail;gameRoot=$script:selectedGameRoot}
        [IO.File]::AppendAllText($script:tracePath,($record|ConvertTo-Json -Compress)+[Environment]::NewLine,(New-Object Text.UTF8Encoding($true)))
    }catch{Write-Warning ('无法追加启动器进度记录。'+$_.Exception.Message)}
}

function Save-RhodesSelection([string]$Path) {
    New-Item -ItemType Directory -Path $StateRoot -Force|Out-Null
    [IO.File]::WriteAllText($settingsPath,(@{gameRoot=$Path;schemaVersion=1}|ConvertTo-Json),(New-Object Text.UTF8Encoding($true)))
}
function Show-RhodesFolderDialog([string]$InitialPath) {
    if([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA'){
        throw '目录窗口需要 STA 模式，请使用随包 CMD 入口。'
    }
    Add-Type -AssemblyName System.Windows.Forms
    $dialog=New-Object System.Windows.Forms.FolderBrowserDialog
    $owner=New-Object System.Windows.Forms.Form
    try{
        $dialog.Description='选择《卡拉克沙漠》游戏文件夹，文件夹内应有 DesertsOfKharak64.exe。'
        $dialog.ShowNewFolderButton=$false
        $dialog.RootFolder=[Environment+SpecialFolder]::MyComputer
        if($InitialPath -and [IO.Directory]::Exists($InitialPath)){$dialog.SelectedPath=$InitialPath}
        $owner.ShowInTaskbar=$false
        $owner.TopMost=$true
        $owner.Opacity=0
        $owner.StartPosition=[System.Windows.Forms.FormStartPosition]::CenterScreen
        $owner.Size=New-Object Drawing.Size(1,1)
        $owner.Show()
        $owner.Activate()
        if($dialog.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK){return $dialog.SelectedPath}
        return $null
    }finally{$dialog.Dispose();$owner.Close();$owner.Dispose()}
}
function Select-RhodesGameRoot([switch]$Reselect) {
    if(-not $script:interactive -and $GameRoot){return [IO.Path]::GetFullPath($GameRoot.Trim('"')).TrimEnd('\')}
    if(-not $Reselect -and $script:selectedGameRoot){return [IO.Path]::GetFullPath($script:selectedGameRoot.Trim('"')).TrimEnd('\')}
    $savedRoot=$null
    if(Test-Path -LiteralPath $settingsPath){
        try{
            $saved=Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8|ConvertFrom-Json
            if($saved.gameRoot -and [IO.File]::Exists([IO.Path]::Combine([string]$saved.gameRoot,'DesertsOfKharak64.exe'))){$savedRoot=[string]$saved.gameRoot}
        }catch{Write-Warning '已保存的目录不可用，将重新查找。'}
    }
    if(-not $Reselect -and $savedRoot){return $savedRoot}
    $found=@()
    # Explicit reselection opens the picker without depending on Steam discovery.
    if(-not $Reselect -and -not $script:interactive){
        Write-RhodesOperationProgress '查找 Steam 游戏目录'
        try{$found=@(Get-RhodesSteamInstallations)}
        catch{Write-Warning ('自动查找 Steam 目录失败，可手动选择游戏文件夹。'+$_.Exception.Message)}
    }
    if($found.Count -eq 1){return $found[0].gameRoot}
    if(-not $script:interactive){throw '未找到唯一可用的 Steam 安装目录。请运行双击菜单选择目录，或使用 -GameRoot 参数。'}
    $initialRoot=$script:selectedGameRoot
    if(-not $initialRoot){$initialRoot=$savedRoot}
    if(-not $initialRoot -and $found.Count -gt 0){$initialRoot=$found[0].gameRoot}
    Write-RhodesOperationProgress '正在打开目录选择窗口' 0 0 '请在文件夹窗口中选择游戏目录。'
    try{$candidate=Show-RhodesFolderDialog $initialRoot}
    catch{
        Write-Warning ('目录窗口无法打开，请粘贴目录。'+$_.Exception.Message)
        $candidate=(Read-Host '粘贴包含 DesertsOfKharak64.exe 的 Steam 游戏目录（直接回车取消）').Trim().Trim('"')
    }
    if([string]::IsNullOrWhiteSpace($candidate)){throw '已取消目录选择。'}
    $candidate=[IO.Path]::GetFullPath($candidate).TrimEnd('\')
    if(-not [IO.File]::Exists([IO.Path]::Combine($candidate,'DesertsOfKharak64.exe'))){throw '所选文件夹中没有 DesertsOfKharak64.exe，请选择游戏安装文件夹。'}
    return $candidate
}
function Write-RhodesLocalLog([string]$Operation,$Result,[string]$ErrorText,[string]$Status) {
    try{
        $logs=Join-Path $StateRoot 'logs'
        New-Item -ItemType Directory -Path $logs -Force|Out-Null
        $path=$script:lastLog
        if(-not $path -or $script:logOperation -ne $Operation){$path=Join-Path $logs ([DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff')+'_'+$Operation+'.json')}
        if(-not $Status){$Status=if($ErrorText){'FAILED'}else{'COMPLETED'}}
        $record=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');operation=$Operation;status=$Status;launcherVersion=$script:launcherVersion;lastStage=$script:lastProgressStage;elapsedSeconds=[Math]::Round(([DateTime]::UtcNow-$script:operationStarted).TotalSeconds,3);progressLog=$script:tracePath;packageRoot=$PSScriptRoot;gameRoot=$script:selectedGameRoot;host=$PSVersionTable.PSVersion.ToString();result=$Result;error=$ErrorText;nativeAuthenticationTested=$false}
        [IO.File]::WriteAllText($path,($record|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($true)))
        $script:lastLog=$path
    }catch{Write-Warning ('无法保存启动器记录：'+$_.Exception.Message)}
    finally{if($Status -ne 'RUNNING'){$script:operationActive=$false}}
}
function Show-RhodesOperationResult([string]$Operation,$Result) {
    if($null -eq $Result){return}
    if($Result.PSObject.Properties.Name -notcontains 'status'){$Result|ConvertTo-Json -Depth 8|Write-Host;return}
    Write-Host ''
    switch([string]$Result.status){
        'INSTALLED_FILES_VERIFIED' {
            if($Operation -eq 'Install'){
                Write-Host '【安装成功】' -ForegroundColor Green
                Write-Host 'Mod 文件已安装，并通过完整校验。'
                Write-Host '按回车返回菜单，选择 2 启动 Mod。' -ForegroundColor Cyan
            }else{Write-Host '【文件校验通过】' -ForegroundColor Green}
            Write-Host ('游戏目录 '+$script:selectedGameRoot)
            Write-Host ('Mod 版本 '+$Result.version)
        }
        'GAME_DIRECTORY_SAVED' {
            Write-Host '【游戏目录已保存】' -ForegroundColor Green
            Write-Host $Result.gameRoot
        }
        'OWNED_MOD_FILES_REPAIRED' {
            Write-Host '【修复完成】' -ForegroundColor Green
            Write-Host ('已修复 '+$Result.repaired+' 个文件，并通过完整校验。')
        }
        'OWNED_MOD_FILES_REMOVED' {Write-Host '【卸载完成】' -ForegroundColor Green}
        'STEAM_LAUNCH_REQUESTED' {
            Write-Host '【已交给 Steam 启动】' -ForegroundColor Green
            Write-Host '请查看 Steam 和游戏窗口，等待进入主菜单。'
        }
        default {$Result|ConvertTo-Json -Depth 8|Write-Host}
    }
    Write-Host ''
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
    $requireReady=$Operation -in @('Install','Start','Verify','Repair','Remove')
    $release=Read-RhodesRelease $PSScriptRoot -RequireReady:$requireReady
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
            $script:selectedGameRoot=$game
            $path=Resolve-RhodesChild $game 'BepInEx/MapUnitLab.Runtime.log'
            if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return [pscustomobject]@{status='NO_RUNTIME_LOG';path=$path;message='尚未发现运行日志；启动器没有据此推断游戏是否运行。'}}
            $lines=@(Get-Content -LiteralPath $path -Tail 400 -Encoding UTF8|Where-Object {$_ -match 'RHODES_SKIRMISH_'}|Select-Object -Last 12)
            return [pscustomobject]@{status='EXISTING_RUNTIME_LOG';modifiedUtc=(Get-Item -LiteralPath $path).LastWriteTimeUtc.ToString('o');path=$path;stages=$lines;message='这是现存日志，可能来自上一局；请核对时间。MENU_READY不代表遭遇战或第二局已经通过验收。'}
        }
        'Select' {
            $game=Select-RhodesGameRoot -Reselect
            Assert-RhodesNative $game $release
            Save-RhodesSelection $game
            $script:selectedGameRoot=$game
            return [pscustomobject]@{status='GAME_DIRECTORY_SAVED';gameRoot=$game;gameModified=$false;gameStarted=$false}
        }
    }
    $game=Select-RhodesGameRoot
    $script:selectedGameRoot=$game
    Write-RhodesOperationProgress '游戏目录已确定' 0 0 $game
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
function Show-RhodesDistributionNotice {
    Write-Host '【免费发布声明】' -ForegroundColor Yellow
    Write-Host '本mod只在github发布，且完全免费，如果您是通过付费渠道获取，请注意维权。' -ForegroundColor Yellow
    Write-Host '发布地址：https://github.com/PFMartist/Arknights-Deserts-of-Talos-II' -ForegroundColor Cyan
    Write-Host ''
}

function Show-RhodesMenu {
    $release=Read-RhodesRelease $PSScriptRoot
    $summary=Get-RhodesReleaseSummary $release
    Write-Host ''
    Write-Host '罗德岛 Mod 启动器' -ForegroundColor Yellow
    Write-Host ('版本：'+$release.version)
    Write-Host ('启动器版本 '+$script:launcherVersion)
    if($summary.releaseChannel -eq 'skirmish_preview'){Write-Host '类型：遭遇战预览版（完整验收状态见“检查”）' -ForegroundColor Cyan}
    elseif($summary.releaseChannel -eq 'trial'){Write-Host '类型：试验包，不代表正式遭遇战支持' -ForegroundColor Cyan}
    else{Write-Host ('类型：'+$summary.releaseChannel)}
    Show-RhodesDistributionNotice
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
    Write-Host ('启动器日志目录 '+(Join-Path $StateRoot 'logs')) -ForegroundColor DarkGray
}

$exitCode=0
try{
    if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1){throw '请使用随包 CMD 入口，它会选择 Windows PowerShell 5.1。'}
    Set-RhodesProgressReporter {param($Stage,$Completed,$Total,$Detail) Write-RhodesOperationProgress $Stage $Completed $Total $Detail}
    if($Action -eq 'Menu'){
        while($true){
            Show-RhodesMenu
            $choice=(Read-Host '请选择').Trim()
            if($choice -eq '0'){break}
            $actions=@{'1'='Install';'2'='Start';'3'='Verify';'4'='Repair';'5'='Remove';'6'='Select';'7'='Check';'8'='Status'}
            if(-not $actions.ContainsKey($choice)){Write-Host '请输入 0 至 8。';continue}
            $operation=$actions[$choice]
            try{
                Start-RhodesOperationLog $operation
                $result=Invoke-RhodesOperation $operation
                Write-RhodesLocalLog $operation $result $null
                Show-RhodesOperationResult $operation $result
            }catch{
                Write-RhodesLocalLog $operation $null $_.Exception.Message
                Write-Host ('未完成：'+$_.Exception.Message) -ForegroundColor Red
            }
            if($script:lastLog){Write-Host ('记录：'+$script:lastLog)}
            $null=Read-Host '按回车返回菜单'
        }
    }else{
        if($script:interactive){Show-RhodesDistributionNotice}
        Start-RhodesOperationLog $Action
        $result=Invoke-RhodesOperation $Action
        Write-RhodesLocalLog $Action $result $null
        if($script:interactive){Show-RhodesOperationResult $Action $result}
        else{$result|ConvertTo-Json -Depth 8}
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
