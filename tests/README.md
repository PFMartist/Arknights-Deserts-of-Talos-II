# 源码测试

仓库收录 Hotfix59–64 的源码与元数据回归入口。部分夹具使用仓库内托管替身，部分需要开发者本地提供的原生程序集或资源，具体输入见下表。

所有新增夹具从相对的`src`读取实际生产helper或方法体；输出写入Git忽略的`out/tests`。本地输出可能含编译输入路径，不应作为源码提交。

## 不需要游戏资源的夹具

在Windows上，提供系统.NET Framework 4.x编译器；atlas夹具另需Python 3：

```powershell
.\tests\frame60\Test-FrameTiming60.ps1
.\tests\hud61\run_hud61.ps1
.\tests\operatorlayout61\run_operatorlayout61.ps1
.\tests\lod64\Test-NativeLodBounds64.ps1
python .\tests\atlas61\test_atlas61.py
```

- `frame60`：真实生产统计窗口与计时器，受控时钟，后期继续记录；不测实机FPS。
- `hud61`：真实颜色图快照、完整原生slot颜色分类适配类，以及提取的图标映射方法。覆盖预算截断、池化颜色／atlas重置、引用替换、权限与第二局上下文。
- `operatorlayout61`：真实排队／延迟布局方法及布局入口guard前缀。覆盖隐藏页、同帧合并、首建、显页和resize；几何写入用计数替身替代。
- `atlas61`：真实正向缓存helper与提取的MapAtlas／Needs方法，用受控NGUI查询和构建替身验证幂等、晚注册及sprite列表变化。

### Hotfix64 原生 LOD 距离参考

`lod64` 编译实际 `src/NativeUnitLodAdapter.cs`，配合仓库内 Unity 与 BBI 托管替身，并保留 Hotfix63r2 旧版方法作为回归对照。

- 覆盖父级位移、旋转和正的统一缩放下的距离参考点及尺寸转换。
- 核对原生阈值、渐变、组身份、强制档位与 1–8 个 LOD 档位。
- 验证非法归属、参数和包围范围在写入前被拒绝。
- 注入部分写入失败，核对两个 LOD 组恢复后可成功重试。

该入口当前通过 138 个场景、3542 项检查。输出位于 `out/tests/lod64`，包含编译日志、执行结果及带源码哈希的 `REPORT.json`。测试范围为托管变换和 LODGroup 行为，GPU 渲染及实际相机下的分档由实机验证覆盖。

## 需要开发者本地依赖的夹具

先按[构建说明](../BUILDING.md)编译，或为脚本提供`-RuntimeDll`。以下变量通过本地输入取得：

```powershell
$gameRoot = Read-Host '游戏根目录'
$loaderRoot = Read-Host 'BepInEx目录'
.\build.ps1 -GameRoot $gameRoot -BepInExRoot $loaderRoot -OutRoot '.\out'
.\tests\power60\run_power60.ps1 -GameRoot $gameRoot
.\tests\clone59\Test-Clone59.ps1 -GameRoot $gameRoot -BepInExRoot $loaderRoot -RuntimeDll '.\out\EndfieldDoK.MapUnitLab.dll'
```

| 目录 | 额外参数／本地输入 | 验证范围 |
|---|---|---|
| `clone59` | 可选`-RuntimeDll`及隔离生成的`-PatchedDataDll` | 生产初始化策略、原生ABI和编译后接线；不得提交临时原生DLL |
| `power60` | `-GameRoot`；可选`-CarrierIni` | 真实原生类型／实例buff、六档属性、UI、升降档及与仓库内59纯源码基线比较；不提供INI时明确跳过该项 |
| `perf60-sensor` | `-CecilPath`，可选`-AssemblyPath` | 编译后IL分支可达性，地面保留原生传感器图标策略 |
| `perf60-track` | `-AssetRoot`指向自行提供的四车型FTK目录 | 生产Advance／ApplyNative、真实FTK数据及setter计数；不用模型渲染替代，也不把setter数当成FPS |
| `voice60` | `-GameRoot`、`-VoiceRoot`、`-BaselineRoutes` | 工程支援车生产事件与目录路由、59旧路由保持、既有WAV哈希；不播放或合成音频 |
| `navigation62` | `-GameRoot`、`-NativeCarrierJson` | 实际运行时DLL导航参数、原生Fixed64序列化、四个原版donor保持；JSON由开发者从自备原始资源提取，不随仓库分发 |
| `navigation62-route` | `-GameRoot`、`-MapsTsv` | 原生托管路径缓存与5／305两profile的离线比较；需要自行提供受支持14张地图缓存 |

`navigation62-route`的外部TSV每行三列：地图标签、缓存文件路径、SHA256，以tab分隔。相对文件路径按TSV所在目录解析。仓库不提供TSV或游戏缓存，也不自动下载、提取它们。其固定结果用于同一受支持数据集的回归，换数据前应先检查来源与哈希。

`voice60`的`BaselineRoutes`是加入工程支援车独立路由前的59目录文本；当前路由与WAV通过`VoiceRoot`读取。二者均由开发者自行提供，不进入源码仓库。

编译、纯策略／IL、真实托管算法、隔离安装事务、游戏初始化、操作行为、画面和用户验收是不同层级。所有离线夹具通过也不表示实际路径、多人联机、完整对局、第二局或语音听感已经验收。

完整母舰资源上的曲线、顶点、显隐和材质检查由维护者另行记录。运行各公开测试时，按本页列出的依赖准备输入，并记录对应源码修订。
