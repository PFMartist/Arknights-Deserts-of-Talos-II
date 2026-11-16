# 构建说明

当前源码修订为 **Hotfix64**，`src` 中包含 301 份 C# 文件。主构建生成 Runtime、预加载 Patcher 和编译记录，玩家启动器 EXE 使用单独的构建入口。

## 环境与外部依赖

- Windows x64，Windows PowerShell 5.1 或 Windows 上的 PowerShell 7。
- Windows .NET Framework 4.x 自带的 `csc.exe`。
- 自备《家园：卡拉克沙漠》安装的 `Data/Managed`。当前目标为 Steam build **12339551**、Unity **5.2.2f1**。
- BepInEx **5.4.23.4 Windows x64** 的 `core` 文件，至少包含 `BepInEx.dll`、`BepInEx.Preloader.dll` 和 `Mono.Cecil.dll`。

游戏编译引用与加载器文件由开发者在本地提供。依赖来源与许可见 [THIRD_PARTY.md](THIRD_PARTY.md)。

## 目录与命令

```text
repository/
  src/                 # Runtime 与预加载 Patcher 源码
  launcher/            # 玩家启动器脚本和 EXE 入口源码
    launcher-src/
  tests/               # 源码与元数据回归入口
  licenses/            # 第三方许可原文
  build.ps1
  out/                 # 本地生成，Git 忽略
```

从仓库根目录执行以下命令。

```powershell
$GameRoot = Read-Host '游戏安装目录'
$BepInExRoot = Read-Host 'BepInEx 解压目录'
.\build.ps1 -GameRoot $GameRoot -BepInExRoot $BepInExRoot -OutRoot '.\out'
```

| 参数 | 含义 |
| --- | --- |
| `GameRoot` | 游戏根目录，其下应存在 `Data\Managed`。必填。 |
| `BepInExRoot` | BepInEx 解压根目录、`BepInEx` 目录或 `core` 目录。省略时读取游戏根目录下的 `BepInEx`。 |
| `OutRoot` | 默认是仓库根目录的 `out`。选用位于游戏、源码和外部加载器目录之外的独立输出目录。 |

构建输出采用以下布局。

```text
out/
  EndfieldDoK.MapUnitLab.dll
  EndfieldDoK.MapUnitLab.Patcher.dll
  RuntimeCompileArguments.rsp
  PatcherCompileArguments.rsp
  build-info.json
```

响应文件包含本机编译输入路径，供本地复查，已由 `.gitignore` 排除。`build-info.json` 记录相对文件名、输入与输出哈希、源码修订和编译状态。旧编译器生成的 DLL 应使用对应构建记录核对源码与依赖。

## 编译与版本约定

Runtime 引用游戏的旧 Mono 标准程序集、Unity、Fabric、NGUI 和 BBI 程序集。Patcher 同时引用本地 BepInEx 与 Mono.Cecil。两个目标均启用 `/nostdlib+`、优化和 `/warnaserror+`。

构建脚本先核对受支持原生程序集的 SHA-256。Patcher 当前接线目标为以下程序集。

- `BBI.Unity.Game.dll`
- `BBI.Game.dll`
- `BBI.Unity.Game.Data.dll`

其中包括受控资产创建时的初始化顺序保护。调整目标列表、原生签名或支持版本时，应同步更新版本守卫、Runtime 接口与 Patcher 回归。

Hotfix63–64 延续当前 Patcher 接线。新增的车辆、飞机与母舰合批，以及 LOD 距离参考传递，位于 Runtime 的表现层。导航与物理占地继续由原有单位定义及原生模拟管理。

## 玩家启动器

当前玩家启动器版本为 **62.4**。它以 `RhodesMod.exe` 为主要入口，由 EXE 调用 Windows PowerShell 5.1 执行同目录脚本。目录选择、安装进度、成功提示、免费发布声明与各动作的输出由脚本管理。

启动器 EXE 的构建需要完整玩家包中的图标文件。从仓库根目录执行以下命令。

```powershell
$PlayerPackage = Read-Host '完整玩家包目录'
.\launcher\launcher-src\Build.ps1 -IconPath (Join-Path $PlayerPackage 'RhodesMod.ico')
```

该入口生成 `launcher/RhodesMod.exe`，并将指定图标复制到 `launcher/RhodesMod.ico`。这两个生成文件由 Git 忽略。对应 C# 入口与构建脚本保存在 `launcher/launcher-src`。

组装完整玩家包时，将 EXE、图标、4 份 PowerShell 文件和 6 份 CMD 入口放在包根目录，再配套相应的 `release.json`、完整 `payload`、使用说明和许可文件。具体布局见 [launcher/README.md](launcher/README.md)。

`release.json` 中的受管路径、SHA-256、原生版本和启动关键文件标记应与实际载荷一致。验包与部署逻辑位于 `launcher/RhodesPackage.psm1`。模型、贴图、动画、界面图集和音频来自对应的完整资源包，构建使用的游戏程序集与外部资源继续通过本地路径提供。

## 测试与验收

所有公开测试入口及外部输入要求见 [tests/README.md](tests/README.md)。其中 `lod64` 直接编译当前 `NativeUnitLodAdapter` 和仓库内托管替身，覆盖距离参考传递、原生阈值、强制档位与失败回滚。

```powershell
.\tests\lod64\Test-NativeLodBounds64.ps1
```

母舰原曲线、网格与材质的完整资源检查由维护者按版本单独记录。公开源码测试的结果按各自入口的范围说明解释。

记录验证结果时，应注明编译、源码策略或 IL、真实托管算法、安装事务、游戏初始化、操作行为、画面与用户验收。当前版本的 Unity GPU 蒙皮、镜头下的 LOD 交接、飞机起降、干员技能、残骸与第二局行为，依照实际测试记录确认。当前下载与发布状态以 [Releases](https://github.com/PFMartist/Arknights-Deserts-of-Talos-II/releases) 页面为准。
