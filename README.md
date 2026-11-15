# Arknights: Deserts of Talos Ⅱ

《家园：卡拉克沙漠》的非官方《明日方舟》／《明日方舟：终末地》同人 Mod。

作者：**PFMartist**  
AI 协作：**GPT（Codex）**

项目将罗德岛作为玩家阵营接入遭遇战，包含母舰航空与炮战改装、配套载具和舰载机、干员搭载与技能、科研、能量与温度系统，以及界面和语音改编。当前以**玩家操控罗德岛、对抗原版 AI 的单机遭遇战 Beta**为主要测试范围；数值和操作体验仍在调整。

当前源码包含 Hotfix59 修复，验证范围见 [更新记录](CHANGELOG.md)。

## 游玩与源码的区别

**本仓库只提供源码和开发说明，不包含完整可玩资源。** 模型、贴图、动画、界面图集、加载插图、音频、游戏 DLL、BepInEx 二进制及游戏本体不进入源码树。仅克隆本仓库或编译两个 DLL，不能代替完整安装包。

普通玩家应使用 [Releases](https://github.com/PFMartist/Arknights-Deserts-of-Talos-II/releases) 中实际发布的完整玩家包，并按随包说明安装。若该页还没有附件，表示这里尚未提供可下载的完整玩家包。

完整包要求玩家自备受支持的 Windows x64 游戏安装。当前源码针对 Steam build **12339551**、Unity **5.2.2f1**；Patcher 会验证目标原生程序集，不能将任意版本视为兼容版本。安装、更新和卸载前应正常退出游戏，启动后先进入菜单，再选择遭遇战中的罗德岛阵营。

## 开发构建

构建需要合法游戏安装的 `Data/Managed` 编译引用和 BepInEx 5.4.23.4 的 `core` 文件。它们由开发者在本地提供，不随 Git 仓库分发。

```powershell
.\build.ps1 -GameRoot 'X:\Games\Deserts of Kharak' -BepInExRoot 'X:\Tools\BepInEx' -OutRoot '.\out'
```

脚本只在输出目录生成 Runtime、Patcher 和本地构建记录，不安装或启动游戏。详细依赖、目录与验证边界见 [BUILDING.md](BUILDING.md)。

## 实现概览

- **Runtime**：克隆并私有化原生单位、科研和能力数据，注册罗德岛定义，通过原生模拟执行移动、生产、战斗和大部分能力。
- **Patcher**：在 BepInEx 预加载阶段对受支持的原生程序集做内存中的 IL 接线，使 Runtime 能参与注册、模拟边界、UI 与生命周期。不以发布原生 DLL 替换件的方式分发游戏代码。
- **表现资源**：从另行分发的资源包读取转换后的模型、动画、材质与音频，适配旧 Unity API；源码仓库不包含这些资源。
- **玩家启动器**：[launcher](launcher/README.md) 保存现有安装、启动、校验、修复和卸载脚本源码。组装玩家包时放在包根目录，必须同时具有匹配的 `release.json` 与完整 `payload`，不能直接从源码目录启动游玩。
- **验证**：编译、纯策略与元数据检查、安装事务检查、游戏运行记录和实际画面验收分别记录，不能互相替代。

源码中保留部分开发实验模块，正式遭遇战路径会明确分流。存在实验代码不表示该功能属于已完成的玩家玩法。

## 反馈与贡献

欢迎提交 [Issue](https://github.com/PFMartist/Arknights-Deserts-of-Talos-II/issues) 或 PR。反馈请包含版本、地图、阵营、操作步骤和复现方式；分享日志前请检查其中的个人路径或账号信息。贡献约定见 [CONTRIBUTING.md](CONTRIBUTING.md)。

联机 PvP/PvE、电脑操控罗德岛及所有地图组合不作为已验收能力。某个系统成功初始化或日志中出现 READY，不代表其玩法、视觉或完整对局已经通过测试。

## 署名与许可

代码许可保留现有 [MIT LICENSE](LICENSE) 原文。该许可不重新授权原作游戏、角色、美术、录音、生成语音或其他第三方内容。来源和适用范围见 [THIRD_PARTY.md](THIRD_PARTY.md) 与 [CREDITS.md](CREDITS.md)。本项目与相关原作发行方无隶属或官方合作关系。
