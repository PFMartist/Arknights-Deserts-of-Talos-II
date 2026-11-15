# 构建说明

## 环境与外部依赖

- Windows x64，Windows PowerShell 5.1 或 Windows 上的 PowerShell 7。
- Windows .NET Framework 4.x 自带的 `csc.exe`。脚本使用游戏自己的旧 Mono 标准程序集作为目标引用，而不是宿主 .NET 运行库。
- 自备的《家园：卡拉克沙漠》受支持安装，当前目标为 Steam build **12339551**、Unity **5.2.2f1**。
- BepInEx **5.4.23.4 Windows x64** 解压目录或其 `BepInEx`／`core` 目录，至少包含 `BepInEx.dll`、`BepInEx.Preloader.dll`、`Mono.Cecil.dll`。来源与许可见 [THIRD_PARTY.md](THIRD_PARTY.md)。

这些游戏和加载器二进制不包含在源码仓库内，也不应提交到 PR。请使用来源可靠的原版安装或上游发行包，不能通过重命名文件绕过版本检查。

## 目录与命令

```text
repository/
  src/                 # Runtime 与预加载 Patcher 源码
  launcher/            # 玩家启动器及安装/校验/卸载脚本源码
  tests/               # 可公开的纯策略／元数据测试
  licenses/            # 保留的第三方许可原文
  build.ps1
  out/                 # 本地生成，Git 忽略
```

从仓库根目录执行：

```powershell
.\build.ps1 -GameRoot 'X:\Games\Deserts of Kharak' -BepInExRoot 'X:\Tools\BepInEx' -OutRoot '.\out'
```

参数说明：

| 参数 | 含义 |
| --- | --- |
| `GameRoot` | 游戏根目录，其下应存在 `Data\Managed`。必填。 |
| `BepInExRoot` | BepInEx 解压根目录、`BepInEx` 目录或 `core` 目录。省略时读取游戏根目录下的 `BepInEx`。 |
| `OutRoot` | 编译输出目录。默认是仓库根目录的 `out`，不能指向游戏目录或源码目录。 |

输出：

```text
out/
  EndfieldDoK.MapUnitLab.dll
  EndfieldDoK.MapUnitLab.Patcher.dll
  RuntimeCompileArguments.rsp
  PatcherCompileArguments.rsp
  build-info.json
```

两个响应文件含本机绝对路径，仅用于本地复查，已由 `.gitignore` 排除。构建记录包含输入/输出哈希和相对文件名，不记录本机游戏路径。旧编译器不提供现代确定性构建开关，因此相同源码的独立构建不承诺生成逐字节相同的 DLL。

## 编译约定

Runtime 引用游戏的 `mscorlib.dll`、`System.dll`、`System.Core.dll`、Unity、Fabric、NGUI 和 BBI 程序集。Patcher 使用相同标准程序集及本地 BepInEx／Mono.Cecil 引用。均启用 `/nostdlib+`、优化和 `/warnaserror+`；不得通过移除这些参数掩盖不兼容警告。

Patcher 当前接线的目标为：

- `BBI.Unity.Game.dll`
- `BBI.Game.dll`
- `BBI.Unity.Game.Data.dll`

最后一项包含受控资产创建时的初始化顺序保护。修改目标列表、原生签名或支持版本时，需要同步检查对应的 SHA-256 守卫、Runtime 接口和离线 Patcher 测试。新 Runtime 与旧 Patcher 不能随意混搭。

脚本不会修改、安装、启动或结束游戏进程。若在已运行的游戏旁构建，新 DLL 也不会自动进入当前进程。

## 素材与本地运行

源码编译不会产生模型、贴图、动画、图集或音频。要进行游戏测试，还需与当前代码匹配的完整资源包和玩家启动器。资源包的发布、哈希与许可范围应单独记录；不要把第三方资源复制进 `src` 或测试夹具来让 CI 通过。

使用完整玩家包进行本地测试时，应先退出游戏，保留已知可用版本及回滚记录，再按该包的更新说明部署匹配的 Runtime／Patcher。构建输出本身不是完整安装器。

### 玩家包中的启动器

[launcher](launcher/README.md) 包含启动器脚本源码。组装玩家包时，将其中4份 PowerShell 源文件和6份 `.cmd` 入口复制到玩家包根目录，与该版本生成的 `release.json`、`payload` 及使用说明放在一起。缺少清单或完整资源时，这些入口不能当作可玩发行包使用。

`release.json` 的受管文件路径应相对包根目录，SHA-256、原生版本及启动关键文件标记必须与实际 `payload` 一致；验包逻辑见 `launcher/RhodesPackage.psm1`。源码构建脚本不自动生成或下载资源包，也不会借用维护者本机的清单和目录。

## 测试与验收

可公开测试的运行方式见 [tests/README.md](tests/README.md)。纯策略测试不运行 Unity；元数据测试读取开发者本地提供的原生 DLL，但不会执行其中的游戏方法。

提交验证结果时请明确层级：编译、离线策略/IL、隔离安装事务、实际初始化、操作行为、画面和用户验收。发布前的实际测试应覆盖进入菜单、罗德岛遭遇战、生产科研、飞机起降、干员技能与收纳、残骸、结束后再开第二局。没有实际执行的项目保持待测。
