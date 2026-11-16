# 玩家启动器源码

当前版本为 **62.4**。玩家使用完整包根目录中的 `RhodesMod.exe`，由它调用 Windows PowerShell 5.1 运行同目录的菜单脚本。源码目录提供菜单、安装事务和 EXE 入口的实现。

## 文件

| 文件 | 职责 |
| --- | --- |
| `RhodesMod.ps1` | 交互菜单、进度、成功提示、免费发布声明与诊断记录 |
| `RhodesPackage.psm1` | 清单读取、版本及哈希检查、安装、修复和卸载 |
| `RhodesProcess.psm1` | 游戏进程与运行阶段检测 |
| `RhodesSteam.psm1` | Steam 安装发现、目录选择与启动条件检查 |
| `launcher-src/RhodesModEntry.cs` | 带图标的 EXE 入口，传递控制台输入、输出和退出码 |
| `launcher-src/Build.ps1` | 使用外部 ICO 编译 EXE |
| `RhodesMod.cmd` | 交互菜单备用入口 |
| `InstallMod.cmd` | 安装入口 |
| `StartMod.cmd` | 经 Steam 启动游戏 |
| `VerifyMod.cmd` | 安装文件校验 |
| `RepairMod.cmd` | 同版受管文件修复 |
| `RemoveMod.cmd` | 卸载受管文件 |

菜单与直接交互入口显示以下声明，并附上官方 GitHub 地址。

> 本mod只在github发布，且完全免费，如果您是通过付费渠道获取，请注意维权。

命令行自动化可使用 `-NoPause` 获取 JSON 结果。

## 构建 EXE

从仓库根目录执行以下命令，选择完整玩家包中提供的图标。

```powershell
$PlayerPackage = Read-Host '完整玩家包目录'
.\launcher\launcher-src\Build.ps1 -IconPath (Join-Path $PlayerPackage 'RhodesMod.ico')
```

脚本生成 `launcher/RhodesMod.exe` 和 `launcher/RhodesMod.ico`，Git 忽略这两个生成文件。EXE 的构建依赖 Windows .NET Framework 4.x 编译器，图标通过 `-IconPath` 提供。

## 组装位置

完整玩家包采用以下布局。

```text
player-package/
  RhodesMod.exe
  RhodesMod.ico
  RhodesMod.cmd
  RhodesMod.ps1
  RhodesPackage.psm1
  RhodesProcess.psm1
  RhodesSteam.psm1
  InstallMod.cmd / StartMod.cmd / VerifyMod.cmd / RepairMod.cmd / RemoveMod.cmd
  release.json
  payload/
  README / licenses / third-party notices
```

把上述入口与模块放到玩家包根目录，相对模块导入和 CMD 的 `%~dp0` 以此解析路径。使用相应版本的发行清单与完整载荷，清单中的路径和文件哈希由安装器检查。首次安装、跨版本更新和卸载遵循完整包的使用说明。

在已组装的完整玩家包目录中，可运行以下命令检查清单摘要。

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\RhodesMod.ps1 -Action Inspect -NoPause
```

当前版本已检查 EXE 菜单输入 `0` 退出、直接交互 `Inspect` 和 `Inspect -NoPause` 的 JSON 输出。游戏启动、安装与实际游玩按各动作及游戏版本的记录验证。

主构建与依赖说明见 [BUILDING.md](../BUILDING.md)，许可与资源来源见 [THIRD_PARTY.md](../THIRD_PARTY.md)。
