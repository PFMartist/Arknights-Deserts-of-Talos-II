# 玩家启动器源码

这里保存目前玩家包使用的启动器源文件，不包含 `payload`、资源素材、游戏程序库、维护者安装收据或发行清单。

## 文件

| 文件 | 职责 |
| --- | --- |
| `RhodesMod.ps1` | 交互菜单及各动作的入口、状态与诊断记录 |
| `RhodesPackage.psm1` | 清单读取、版本及哈希检查、安装、修复和卸载 |
| `RhodesProcess.psm1` | 游戏进程与运行阶段检测 |
| `RhodesSteam.psm1` | Steam 安装发现与启动条件检查 |
| `RhodesMod.cmd` | 交互菜单入口 |
| `InstallMod.cmd` | 首次安装入口 |
| `StartMod.cmd` | 经 Steam 启动游戏 |
| `VerifyMod.cmd` | 安装文件校验 |
| `RepairMod.cmd` | 同版受管文件修复 |
| `RemoveMod.cmd` | 卸载受管文件 |

这些4份 PowerShell 文件与6份 `.cmd` 源文件保持当前已验证玩家包的内容；不是额外引入的一套安装器。Steam 库和游戏目录从本机状态或用户选择解析，源码没有维护者专属目录。

## 组装位置

完整玩家包应采用以下布局：

```text
player-package/
  RhodesMod.cmd
  RhodesMod.ps1
  RhodesPackage.psm1
  RhodesProcess.psm1
  RhodesSteam.psm1
  InstallMod.cmd / StartMod.cmd / VerifyMod.cmd / RepairMod.cmd / RemoveMod.cmd
  release.json          # 与此玩家包内容匹配的发行清单
  payload/              # 完整受管文件、合法分发的加载器及独立资源
  README / licenses / third-party notices
```

组装时把源文件放到玩家包根目录，而不是把 `launcher` 作为额外的运行时子目录。相对模块导入和 `.cmd` 的 `%~dp0` 路径都依赖这种布局。

**仅运行源码目录下的 `.cmd` 无法游玩。** 启动器要求匹配的清单和完整资源，并验证实际游戏目录。不要为了让它继续而跳过清单、版本或哈希检查。

编译及资源分发边界见 [BUILDING.md](../BUILDING.md)，第三方范围见 [THIRD_PARTY.md](../THIRD_PARTY.md)。
