# 第三方来源与许可范围

此仓库是 **Arknights: Deserts of Talos Ⅱ** 的源码仓库，不包含完整玩家资源包、游戏本体或第三方编译库。现有 [MIT LICENSE](LICENSE) 原文保留，适用于其中声明的原创源码、工具及文档范围；它不会重新许可下列内容。项目署名见 [CREDITS.md](CREDITS.md)。

## 游戏与作品内容

《家园：卡拉克沙漠》及相关原生程序、标识、数据、界面、录音、模型、材质、动画和特效归 Blackbird Interactive、Gearbox 及相关权利人。开发者须自行提供受支持游戏安装的编译引用；本仓库不分发 `Data/Managed` 中的 BBI/Unity 程序库或游戏可执行文件。

《明日方舟》《明日方舟：终末地》的角色、世界观、美术、Logo、原生干员模型与动画、头像、技能纹样、城市插图、介绍文字及相关录音涉及鹰角网络及其他相关权利人。项目对舰体、载具、残骸和界面的制作或改编，不会把引用的第三方设计和素材转变为独立获得 MIT 授权的资产。

这些表现资源不进入本 Git 源码树。完整玩家资源包如另行分发，应随包附上与实际内容匹配的来源摘要、许可说明和文件哈希；源码仓库的存在本身不代表已取得全部第三方内容的独立再分发许可。

## 开发与运行依赖

以下库是相关构建／玩家加载器使用的外部依赖，**其二进制不在源码仓库中**。保留的许可原文用于说明来源，不代表这些依赖都被编译进本项目 Runtime。

| 组件 | 已核对版本／来源 | 许可原文 |
| --- | --- | --- |
| [BepInEx](https://github.com/BepInEx/BepInEx) | 5.4.23.4 Windows x64 | [MIT](licenses/BepInEx-LICENSE.txt) |
| [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop) | 4.4.1，玩家加载器使用 | [LGPL-2.1](licenses/UnityDoorstop-LICENSE.txt) |
| [Mono.Cecil](https://github.com/jbevain/cecil) | 0.10.4.0，预加载 IL 接线与元数据检查 | [MIT](licenses/Mono.Cecil-LICENSE.txt) |
| [MonoMod](https://github.com/MonoMod/MonoMod) | RuntimeDetour / Utils 22.1.29.1，加载器依赖 | [MIT](licenses/MonoMod-LICENSE.txt) |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | 2.9.0.0，加载器依赖 | [MIT](licenses/HarmonyX-LICENSE.txt) |
| [Harmony](https://github.com/pardeike/Harmony) | 兼容库 2.0.0.0 | [MIT](licenses/Harmony-LICENSE.txt) |
| BepInEx.Harmony / HarmonyXInterop | 2.0.0.0 / 1.0.0.0，加载器依赖 | [MIT](licenses/BepInEx.Harmony-LICENSE.txt)，并保留相关 Harmony / HarmonyX 通知 |

相关既有源码以 DoK_MR contributors 的许可头为准，保留原作者声明。新增贡献也应保留其真实来源，不以 PFMartist 或 AI 协作署名替换上游版权头。

## AI 合成语音

独立玩家资源包包含新增 AI 合成播报，与游戏原生录音和干员原生录音是不同来源。相关离线制作使用 IndexTTS 2.5 流程与既有角色参考录音；合成程序、参考录音及模型权重不包含在此源码仓库内。

这些生成音频不是原配音演员的新录音，不表示原作方、角色权利人或演员参与、认可或背书。参考录音、角色及相关音色权益不由本项目的 MIT LICENSE 授予。若分发语音资源，应随相应资源包保存准确说明，不能用源码许可替代素材许可。
