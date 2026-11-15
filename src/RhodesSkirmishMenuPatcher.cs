// Compile into EndfieldDoK.MapUnitLab.Patcher.dll beside Patcher.cs.
// Call Apply(module, runtimeReference) only for BBI.Unity.Game in Patcher.Patch.
// No on-disk game assembly is modified by this helper. Ranked automatch is
// deliberately excluded; custom lobbies keep their native metadata protocol.
using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EndfieldDoK.MapUnitLab.Preloader
{
    public static class RhodesSkirmishMenuPatcher
    {
        const string CallbackType = "EndfieldDoK.MapUnitLab.RhodesSkirmishFaction";
        const string DescriptionType = "BBI.Unity.Game.Utility.CommanderDescription";
        const string SelectionType = "BBI.Unity.Game.UI.PlayerFactionSelection";
        const string ChoicesType = "System.Collections.Generic.Dictionary`2<System.String,BBI.Unity.Game.UI.PlayerFactionSelection>";
        const string SelectionsType = "System.Collections.Generic.Dictionary`2<BBI.Game.Simulation.CommanderID,BBI.Unity.Game.World.PlayerSelection>";

        public static void Apply(ModuleDefinition module, AssemblyNameReference runtime)
        {
            if (module == null || module.Assembly.Name.Name != "BBI.Unity.Game")
                throw new InvalidOperationException("Skirmish hooks require the native BBI.Unity.Game module");
            TypeReference owner = new TypeReference("EndfieldDoK.MapUnitLab", "RhodesSkirmishFaction", module, runtime);
            MethodDefinition initialize = Exact(module, "BBI.Unity.Game.World.LevelManager", "Initialize", false, "System.Void",
                "BBI.Game.Data.CommanderAttributes[]", "BBI.Game.Data.CommanderAttributes[]", "BBI.Unity.Core.Utility.ICoroutineHost",
                "BBI.Unity.Game.Localize.IGameLocalization", "BBI.Unity.Game.DLC.DLCManager");
            MethodDefinition enable = Exact(module, "BBI.Unity.Game.UI.SkirmishSetupPanel", "OnEnable", false, "System.Void");
            MethodDefinition disable = Exact(module, "BBI.Unity.Game.UI.SkirmishSetupPanel", "OnDisable", false, "System.Void");
            MethodDefinition multiplayerEnable=Exact(module,"BBI.Unity.Game.UI.MultiplayerMissionPanel","OnEnable",false,"System.Void");
            MethodDefinition multiplayerLeave=Exact(module,"BBI.Unity.Game.UI.MultiplayerMissionPanel","LeaveLobbyPanel",false,"System.Void","BBI.Core.Network.PlayerGroupLeaveReason");
            MethodDefinition multiplayerSelection=Exact(module,"BBI.Unity.Game.UI.Frontend.Helpers.LobbySetupPanelBase","SetFactionSelectionForLocalPlayer",false,"System.Void",SelectionType,"System.Boolean");
            MethodDefinition options = Exact(module, "BBI.Unity.Game.UI.GameLobbyView", "GetFactionOptions", true, ChoicesType,
                "BBI.Unity.Game.World.LevelManager", "BBI.Unity.Game.DLC.DLCManager", "BBI.Game.Data.PlayerType", "System.Boolean");
            MethodDefinition random = Exact(module, "BBI.Unity.Game.UI.GameLobbyView", "RollForRandomFaction", true, "System.Boolean",
                ChoicesType, SelectionType + "&");
            MethodDefinition changed = Exact(module, "BBI.Unity.Game.UI.SkirmishSetupPanel", "OnFactionChanged", false, "System.Void",
                "BBI.Game.Simulation.CommanderID", SelectionType);
            MethodDefinition colours = Exact(module, "BBI.Unity.Game.UI.SkirmishSetupPanel", "OnCustomiziationSettingsApplied", false, "System.Void",
                "BBI.Game.Data.UnitColors", "BBI.Core.Network.DLCPackID", "BBI.Unity.Game.CustomizationFactionSetting", "System.Boolean");
            MethodDefinition selections = Exact(module, "BBI.Unity.Game.Utility.SpawnAssigner", "GeneratePlayerSelections", true, SelectionsType,
                "BBI.Game.Data.CommanderAttributes[]", "System.Collections.Generic.List`1<BBI.Unity.Game.Utility.CommanderDescription>",
                "System.Collections.Generic.List`1<BBI.Unity.Game.Utility.CommanderSpawnInfo>", "BBI.Unity.Game.DLC.DLCManager");

            // Validate all methods before altering any of them. Fail closed on ABI drift.
            MethodDefinition[] touched = { initialize, enable, disable, options, random, changed, colours, selections,multiplayerEnable,multiplayerLeave,multiplayerSelection };
            foreach (MethodDefinition method in touched)
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    MethodReference call = instruction.Operand as MethodReference;
                    if (call != null && call.DeclaringType.FullName == CallbackType)
                        throw new InvalidOperationException("Duplicate skirmish hook: " + method.FullName);
                }
            }
            VariableDefinition descChanged = DescriptionLocal(changed);
            VariableDefinition descColours = DescriptionLocal(colours);
            FieldReference factionField = ReferencedField(changed, DescriptionType, "FactionIndex");
            Instruction persistenceStore = UniqueFieldInstruction(changed, OpCodes.Stfld, "BBI.Unity.Game.CustomizationSettings", "FactionIndex");
            Instruction persistenceStart = persistenceStore;
            for (int i = 0; i < 4; i++) persistenceStart = persistenceStart.Previous;
            if (persistenceStart == null || persistenceStart.OpCode != OpCodes.Call ||
                !(((MethodReference)persistenceStart.Operand).Name == "get_UserSettings"))
                throw new InvalidOperationException("Unexpected native faction persistence preamble");
            Instruction changedReturn = SingleReturn(changed);
            Instruction colourStore = UniqueFieldInstruction(colours, OpCodes.Stfld, DescriptionType, "UnitColors");
            Instruction selectionCtor = null, updateDesc = null;
            foreach (Instruction instruction in colours.Body.Instructions)
            {
                MethodReference call = instruction.Operand as MethodReference;
                if (call == null) continue;
                if (call.DeclaringType.FullName == SelectionType && call.Name == ".ctor")
                {
                    if (selectionCtor != null) throw new InvalidOperationException("Ambiguous native colour selection construction");
                    selectionCtor = instruction;
                }
                if (call.Name == "UpdateFactionSelectionForCommanderDescription")
                {
                    if (updateDesc != null) throw new InvalidOperationException("Ambiguous native colour selection update");
                    updateDesc = instruction;
                }
            }
            if (selectionCtor == null || updateDesc == null || colourStore.Offset >= selectionCtor.Offset || selectionCtor.Offset >= updateDesc.Offset || updateDesc.Next == null)
                throw new InvalidOperationException("Unexpected native colour selection path");
            Instruction colourSelectionStart = colourStore.Next;
            Instruction colourCommitStart = updateDesc.Next;

            MethodReference afterInitialize = Callback(module, owner, "AfterLevelInitialize", module.TypeSystem.Void, module.TypeSystem.Object);
            MethodReference enter = Callback(module, owner, "EnterSetup", module.TypeSystem.Void, module.TypeSystem.Object);
            MethodReference leave = Callback(module, owner, "LeaveSetup", module.TypeSystem.Void, module.TypeSystem.Object);
            MethodReference filter = Callback(module, owner, "FilterFactionOptions", options.ReturnType,
                options.ReturnType, module.TypeSystem.Object, options.Parameters[2].ParameterType,module.TypeSystem.Boolean);
            MethodReference filterRandom = Callback(module, owner, "FilterRandomOptions", random.Parameters[0].ParameterType, random.Parameters[0].ParameterType);
            MethodReference isRhodes = Callback(module, owner, "IsRhodesIndex", module.TypeSystem.Boolean, module.TypeSystem.Int32);
            MethodReference prepare = Callback(module, owner, "PrepareSelections", selections.ReturnType, selections.ReturnType);
            MethodReference networkSelection=Callback(module,owner,"FilterNetworkLocalFactionSelection",multiplayerSelection.Parameters[0].ParameterType,
                multiplayerSelection.Parameters[0].ParameterType,module.TypeSystem.Object,module.TypeSystem.Boolean);
            MethodReference persistSelection=Callback(module,owner,"PersistLocalFactionSelection",module.TypeSystem.Boolean,
                module.TypeSystem.Boolean,multiplayerSelection.Parameters[0].ParameterType);

            VoidInstancePostfix(initialize, afterInitialize);
            Prefix(enable, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, enter) });
            VoidInstancePostfix(disable, leave);
            Prefix(multiplayerEnable,new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,enter)});
            VoidInstancePostfix(multiplayerLeave,leave);
            // Keep the native shared-lobby metadata update. Filter only the
            // incoming identity and local preference persistence arguments.
            Prefix(multiplayerSelection,new[]{
                Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_2),
                Instruction.Create(OpCodes.Call,networkSelection),Instruction.Create(OpCodes.Starg_S,multiplayerSelection.Parameters[0]),
                Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,persistSelection),
                Instruction.Create(OpCodes.Starg_S,multiplayerSelection.Parameters[1])});
            // Idempotent registration retry: a rebuilt manager array must be prepared
            // before native enumeration, not only after its returned dictionary exists.
            Prefix(options, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, afterInitialize) });
            ReturnFilter(options, filter, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Ldarg_3) });
            Prefix(random, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, filterRandom), Instruction.Create(OpCodes.Starg_S, random.Parameters[0]) });

            // Native list selection is already updated here. Keep private RI choices
            // out of CustomizationSettings.FactionIndex and its fixed two-slot skin array.
            InsertBefore(changed, persistenceStart, new[] {
                Instruction.Create(OpCodes.Ldloca, descChanged), Instruction.Create(OpCodes.Ldfld, factionField),
                Instruction.Create(OpCodes.Call, isRhodes), Instruction.Create(OpCodes.Brtrue, changedReturn) });
            // Keep native colour assignment, list write and lobby refresh; skip only
            // the incoming two-faction preview choice when the current slot is RI.
            InsertBefore(colours, colourSelectionStart, new[] {
                Instruction.Create(OpCodes.Ldloca, descColours), Instruction.Create(OpCodes.Ldfld, factionField),
                Instruction.Create(OpCodes.Call, isRhodes), Instruction.Create(OpCodes.Brtrue, colourCommitStart) });
            ReturnFilter(selections, prepare, new Instruction[0]);
            foreach (MethodDefinition method in touched)
            {
                WidenBranches(method);
                method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 8);
            }
        }

        static MethodDefinition Exact(ModuleDefinition module, string typeName, string name, bool isStatic, string returns, params string[] parameters)
        {
            TypeDefinition type = module.GetType(typeName);
            if (type == null) throw new InvalidOperationException("Missing native type " + typeName);
            MethodDefinition found = null;
            foreach (MethodDefinition method in type.Methods)
            {
                if (method.Name != name || method.IsStatic != isStatic || method.ReturnType.FullName != returns || method.Parameters.Count != parameters.Length) continue;
                bool matches = true;
                for (int i = 0; i < parameters.Length; i++) if (method.Parameters[i].ParameterType.FullName != parameters[i]) matches = false;
                if (!matches) continue;
                if (found != null || !method.HasBody) throw new InvalidOperationException("Ambiguous native method " + typeName + "::" + name);
                found = method;
            }
            if (found == null) throw new InvalidOperationException("Native ABI mismatch " + typeName + "::" + name);
            return found;
        }

        static MethodReference Callback(ModuleDefinition module, TypeReference owner, string name, TypeReference returns, params TypeReference[] args)
        {
            MethodReference method = new MethodReference(name, returns, owner);
            method.HasThis = false;
            foreach (TypeReference arg in args) method.Parameters.Add(new ParameterDefinition(arg));
            return method;
        }

        static VariableDefinition DescriptionLocal(MethodDefinition method)
        {
            VariableDefinition result = null;
            foreach (VariableDefinition item in method.Body.Variables) if (item.VariableType.FullName == DescriptionType)
            {
                if (result != null) throw new InvalidOperationException("Multiple native commander locals");
                result = item;
            }
            if (result == null) throw new InvalidOperationException("Missing native commander local");
            return result;
        }

        static FieldReference ReferencedField(MethodDefinition method, string type, string name)
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                FieldReference field = instruction.Operand as FieldReference;
                if (field != null && field.DeclaringType.FullName == type && field.Name == name) return field;
            }
            throw new InvalidOperationException("Missing native field " + type + "::" + name);
        }

        static Instruction UniqueFieldInstruction(MethodDefinition method, OpCode op, string type, string name)
        {
            Instruction found = null;
            foreach (Instruction instruction in method.Body.Instructions)
            {
                FieldReference field = instruction.Operand as FieldReference;
                if (instruction.OpCode != op || field == null || field.DeclaringType.FullName != type || field.Name != name) continue;
                if (found != null) throw new InvalidOperationException("Ambiguous field write " + method.FullName);
                found = instruction;
            }
            if (found == null) throw new InvalidOperationException("Missing field write " + method.FullName);
            return found;
        }

        static Instruction SingleReturn(MethodDefinition method)
        {
            Instruction found = null;
            foreach (Instruction instruction in method.Body.Instructions) if (instruction.OpCode == OpCodes.Ret)
            {
                if (found != null) throw new InvalidOperationException("Expected one return in " + method.FullName);
                found = instruction;
            }
            if (found == null) throw new InvalidOperationException("Missing return in " + method.FullName);
            return found;
        }

        static void Prefix(MethodDefinition method, Instruction[] instructions)
        { InsertBefore(method, method.Body.Instructions[0], instructions); }
        static void InsertBefore(MethodDefinition method, Instruction before, Instruction[] instructions)
        {
            ILProcessor il = method.Body.GetILProcessor();
            foreach (Instruction instruction in instructions) il.InsertBefore(before, instruction);
        }
        static void VoidInstancePostfix(MethodDefinition method, MethodReference callback)
        {
            List<Instruction> returns = new List<Instruction>();
            foreach (Instruction instruction in method.Body.Instructions) if (instruction.OpCode == OpCodes.Ret) returns.Add(instruction);
            ILProcessor il = method.Body.GetILProcessor();
            foreach (Instruction instruction in returns)
            {
                instruction.OpCode = OpCodes.Ldarg_0; instruction.Operand = null;
                Instruction call = Instruction.Create(OpCodes.Call, callback);
                il.InsertAfter(instruction, call); il.InsertAfter(call, Instruction.Create(OpCodes.Ret));
            }
        }
        static void ReturnFilter(MethodDefinition method, MethodReference callback, Instruction[] extras)
        {
            // These native methods each have one return, so extra instructions
            // retain a single owner and branch targets still land on stloc.
            Instruction ret = SingleReturn(method);
            VariableDefinition temp = new VariableDefinition(method.ReturnType);
            method.Body.Variables.Add(temp); method.Body.InitLocals = true;
            ret.OpCode = OpCodes.Stloc; ret.Operand = temp;
            ILProcessor il = method.Body.GetILProcessor();
            Instruction cursor = ret, next = Instruction.Create(OpCodes.Ldloc, temp);
            il.InsertAfter(cursor, next); cursor = next;
            foreach (Instruction extra in extras) { il.InsertAfter(cursor, extra); cursor = extra; }
            next = Instruction.Create(OpCodes.Call, callback); il.InsertAfter(cursor, next); cursor = next;
            il.InsertAfter(cursor, Instruction.Create(OpCodes.Ret));
        }
        static void WidenBranches(MethodDefinition method)
        {
            foreach (Instruction i in method.Body.Instructions)
            {
                if (i.OpCode == OpCodes.Br_S) i.OpCode = OpCodes.Br;
                else if (i.OpCode == OpCodes.Brfalse_S) i.OpCode = OpCodes.Brfalse;
                else if (i.OpCode == OpCodes.Brtrue_S) i.OpCode = OpCodes.Brtrue;
                else if (i.OpCode == OpCodes.Beq_S) i.OpCode = OpCodes.Beq;
                else if (i.OpCode == OpCodes.Bge_S) i.OpCode = OpCodes.Bge;
                else if (i.OpCode == OpCodes.Bge_Un_S) i.OpCode = OpCodes.Bge_Un;
                else if (i.OpCode == OpCodes.Bgt_S) i.OpCode = OpCodes.Bgt;
                else if (i.OpCode == OpCodes.Bgt_Un_S) i.OpCode = OpCodes.Bgt_Un;
                else if (i.OpCode == OpCodes.Ble_S) i.OpCode = OpCodes.Ble;
                else if (i.OpCode == OpCodes.Ble_Un_S) i.OpCode = OpCodes.Ble_Un;
                else if (i.OpCode == OpCodes.Blt_S) i.OpCode = OpCodes.Blt;
                else if (i.OpCode == OpCodes.Blt_Un_S) i.OpCode = OpCodes.Blt_Un;
                else if (i.OpCode == OpCodes.Bne_Un_S) i.OpCode = OpCodes.Bne_Un;
                else if (i.OpCode == OpCodes.Leave_S) i.OpCode = OpCodes.Leave;
            }
        }
    }
}
