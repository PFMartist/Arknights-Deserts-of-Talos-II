# 源码测试

这些夹具验证纯托管策略和原生程序集元数据，不启动 Unity，也不执行原生游戏方法。它们不包含任何游戏 DLL、BepInEx 二进制、模型或录音。

当前可公开的初始化策略回归：

```powershell
.\tests\clone59\Test-Clone59.ps1 -GameRoot 'X:\Games\Deserts of Kharak' -BepInExRoot 'X:\Tools\BepInEx' -RuntimeDll '.\out\EndfieldDoK.MapUnitLab.dll'
```

省略 `RuntimeDll` 时，仅执行生产作用域策略与原生方法签名检查；提供 Runtime 时还检查编译后 helper 的接线和 `finally` 清理。若另有离线 Patcher 工具生成的临时原生 Data DLL，可通过 `-PatchedDataDll` 检查其三处入口保护，不得把这种临时 DLL 提交到仓库或分发为游戏替换件。

该回归包含旧空字段初始化的负对照，以及准确类型/实例绑定、嵌套、异常清理和线程隔离。夹具使用仓库 `src/RhodesAssetCreationPolicy59.cs`，不会另写一份镜像策略代替生产实现。

其他测试应沿用相同安排：测试源码进入 Git，原生引用通过参数提供，临时 EXE/DLL 与结果写入忽略的输出目录。涉及模型、安装事务和游戏行为的检查需要独立资源或实际环境，不能仅凭此夹具通过便标记全部系统已验收。
