using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using BepInEx.Preloader;
using BepInEx.Preloader.Patching;

[assembly: AssemblyVersion("1.0.0.0")]
namespace EndfieldDoK.MapUnitLab.Preloader
{
    // Native local playground; preloader-only for this game's Unity 5.2 runtime.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get { yield return "BBI.Unity.Game.dll"; yield return "BBI.Game.dll"; yield return "BBI.Unity.Game.Data.dll"; } }
        public static void Initialize()
        {
            AssemblyPatcher.PatcherPlugins.RemoveAll(delegate(PatcherPlugin plugin) { return plugin.TypeName == "BepInEx.Chainloader"; });
        }
        public static void Finish()
        {
            using(StreamWriter writer=File.AppendText(Path.Combine(BepInEx.Paths.BepInExRootPath,"MapUnitLab.preloader.log")))
                foreach(BepInEx.Logging.LogEventArgs entry in PreloaderConsoleListener.LogEvents)writer.WriteLine(entry.ToString());
        }
        public static void Patch(AssemblyDefinition assembly)
        {
            bool sim=assembly.Name.Name=="BBI.Game";
            bool data=assembly.Name.Name=="BBI.Unity.Game.Data";
            if(!sim&&!data&&assembly.Name.Name!="BBI.Unity.Game")throw new InvalidOperationException("Unexpected native patch target");
            string expected=data?"f3a60df1ab8377492451181a957d1f1df7e3d47265bc0221e1dee83107e14c40":sim?"1216502eff7958a99fe305b439f81709bf340610c782ce449929d13b36ae45cc":"b4c2c7bdbf9737140ac18d6191e50ddd27030a669f050374e8f7e3da8c6ec0e5";
            using(SHA256 sha=SHA256.Create())using(FileStream stream=File.OpenRead(assembly.MainModule.FileName))
                if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=expected)throw new InvalidOperationException("Unsupported MapUnitLab game assembly");
            ModuleDefinition module=assembly.MainModule;
            foreach(AssemblyNameReference item in module.AssemblyReferences)if(item.Name=="EndfieldDoK.MapUnitLab")throw new InvalidOperationException("Duplicate playground hook");
            AssemblyNameReference reference=new AssemblyNameReference("EndfieldDoK.MapUnitLab",new Version(1,0,0,0));module.AssemblyReferences.Add(reference);
            if(data){PatchDeferredAssetEnable59(module,reference);return;}
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","Runtime",module,reference);
            TypeDefinition type=module.GetType(sim?"BBI.Game.Simulation.Sim":"BBI.Unity.Game.ShipbreakersMain");
            MethodDefinition tick=Find(type,sim?"Tick":"Update",sim?1:0);
            MethodReference hook=new MethodReference(sim?"SimTick":"Tick",module.TypeSystem.Void,owner);hook.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            if(!sim){
                RhodesSkirmishMenuPatcher.Apply(module,reference);
                PatchRhodesNetwork(module,reference);
                ILProcessor il=tick.Body.GetILProcessor();Instruction first=tick.Body.Instructions[0];
                il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(first,Instruction.Create(OpCodes.Call,hook));
                MethodDefinition registration=Find(type,"InitializeEntityManager",2);
                MethodReference after=new MethodReference("AfterEntityRegistration",module.TypeSystem.Void,owner);
                List<Instruction> exits=new List<Instruction>();foreach(Instruction i in registration.Body.Instructions)if(i.OpCode==OpCodes.Ret)exits.Add(i);
                ILProcessor registrationIL=registration.Body.GetILProcessor();
                foreach(Instruction i in exits){i.OpCode=OpCodes.Call;i.Operand=after;registrationIL.InsertAfter(i,Instruction.Create(OpCodes.Ret));}
                // Replace only the separately registered flat-lab level's map dependencies.
                TypeDefinition manager=module.GetType("BBI.Unity.Game.World.LevelManager");
                MethodDefinition terrain=Find(manager,"ProcessTerrainInfo",1);
                if(!terrain.IsStatic || terrain.ReturnType.FullName != "BBI.Core.Utility.DependencyContainerBase")
                    throw new InvalidOperationException("Unexpected native terrain dependency signature");
                TypeReference mapOwner=new TypeReference("EndfieldDoK.MapUnitLab","CustomFlatMap",module,reference);
                MethodReference replace=new MethodReference("ReplaceTerrainDependencies",terrain.ReturnType,mapOwner);
                replace.Parameters.Add(new ParameterDefinition(terrain.ReturnType));
                replace.Parameters.Add(new ParameterDefinition(terrain.Parameters[0].ParameterType));
                List<Instruction> terrainExits=new List<Instruction>();
                foreach(Instruction i in terrain.Body.Instructions)if(i.OpCode==OpCodes.Ret)terrainExits.Add(i);
                ILProcessor terrainIL=terrain.Body.GetILProcessor();
                foreach(Instruction i in terrainExits){i.OpCode=OpCodes.Ldarg_0;i.Operand=null;terrainIL.InsertAfter(i,Instruction.Create(OpCodes.Call,replace));terrainIL.InsertAfter(i.Next,Instruction.Create(OpCodes.Ret));}
                terrain.Body.MaxStackSize=Math.Max(terrain.Body.MaxStackSize,8);
                PatchHudTheme(module,reference);PatchNativeHangarUI(module,reference);PatchAviationEvents(module,reference);
                PatchSteamStartupTrace(module,reference);
                PatchUnitViewLifecycle(module,reference);
                PatchRhodesInfoPanel(module,reference);
                PatchRhodesExperienceTooltip(module,reference);
                PatchRhodesModeVoice(module,reference);
                PatchRhodesUI59(module,reference);
            }else{
                PatchPreparedNavigation59(module,reference);
                TypeReference eventsOwner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesSimulationEvents",module,reference);
                MethodReference prepareEvents=new MethodReference("BeforeNativeTick",module.TypeSystem.Void,eventsOwner);
                prepareEvents.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                ILProcessor beforeIL=tick.Body.GetILProcessor();Instruction tickFirst=tick.Body.Instructions[0];
                beforeIL.InsertBefore(tickFirst,Instruction.Create(OpCodes.Ldarg_0));
                beforeIL.InsertBefore(tickFirst,Instruction.Create(OpCodes.Call,prepareEvents));
                MethodDefinition commands=Find(module.GetType("BBI.Game.Commands.SimCommandBuffer"),"ProcessCommandFrame",2);
                if(commands.ReturnType.FullName!="System.Void"||commands.Parameters[0].ParameterType.FullName!="BBI.Game.Commands.SimFrameNumber"||commands.Parameters[1].ParameterType.FullName!="BBI.Core.Utility.Checksum&")
                    throw new InvalidOperationException("Unexpected native command-frame boundary");
                FieldDefinition simInstance=null;foreach(FieldDefinition field in type.Fields)if(field.Name=="Instance"&&field.IsStatic&&field.FieldType.FullName==type.FullName)simInstance=field;
                if(simInstance==null)throw new InvalidOperationException("Native simulation instance field differs");
                ILProcessor commandIL=commands.Body.GetILProcessor();Instruction commandFirst=commands.Body.Instructions[0];
                commandIL.InsertBefore(commandFirst,Instruction.Create(OpCodes.Ldsfld,simInstance));
                commandIL.InsertBefore(commandFirst,Instruction.Create(OpCodes.Call,prepareEvents));
                ILProcessor il=tick.Body.GetILProcessor();List<Instruction> exits=new List<Instruction>();foreach(Instruction i in tick.Body.Instructions)if(i.OpCode==OpCodes.Ret)exits.Add(i);
                foreach(Instruction i in exits){i.OpCode=OpCodes.Ldarg_0;il.InsertAfter(i,Instruction.Create(OpCodes.Call,hook));il.InsertAfter(i.Next,Instruction.Create(OpCodes.Ret));}
                // Restrict only the independently selected Hub test to the one
                // native navigation profile its actual BVM requires. Other modes
                // receive the same array reference and unmodified native body.
                MethodDefinition bake=Find(module.GetType("BBI.Game.Simulation.SimMap"),"BakeSpatialPartition",1);
                if(bake.IsStatic||bake.ReturnType.FullName!="System.Void"||bake.Parameters[0].ParameterType.FullName!="BBI.Game.Data.NavMeshAttributes[]")
                    throw new InvalidOperationException("Unexpected native nav-bake signature");
                TypeReference navOwner=new TypeReference("EndfieldDoK.MapUnitLab","HubRegionNavigation",module,reference);
                MethodReference filter=new MethodReference("FilterBake",bake.Parameters[0].ParameterType,navOwner);
                filter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));filter.Parameters.Add(new ParameterDefinition(bake.Parameters[0].ParameterType));
                MethodReference baked=new MethodReference("AfterBake",module.TypeSystem.Void,navOwner);baked.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                ILProcessor bakeIL=bake.Body.GetILProcessor();Instruction nativeFirst=bake.Body.Instructions[0];
                bakeIL.InsertBefore(nativeFirst,Instruction.Create(OpCodes.Ldarg_0));bakeIL.InsertBefore(nativeFirst,Instruction.Create(OpCodes.Ldarg_1));
                bakeIL.InsertBefore(nativeFirst,Instruction.Create(OpCodes.Call,filter));bakeIL.InsertBefore(nativeFirst,Instruction.Create(OpCodes.Starg_S,bake.Parameters[0]));
                List<Instruction> bakeExits=new List<Instruction>();foreach(Instruction i in bake.Body.Instructions)if(i.OpCode==OpCodes.Ret)bakeExits.Add(i);
                foreach(Instruction i in bakeExits){i.OpCode=OpCodes.Ldarg_0;i.Operand=null;bakeIL.InsertAfter(i,Instruction.Create(OpCodes.Call,baked));bakeIL.InsertAfter(i.Next,Instruction.Create(OpCodes.Ret));}
                bake.Body.MaxStackSize=Math.Max(bake.Body.MaxStackSize,8);
                // Native commands and movement stay authoritative. The private
                // humanoid basic gun may lower only its existing fire criterion
                // at this state-machine entry while its owner is moving.
                MethodDefinition fire=Find(module.GetType("BBI.Game.Simulation.WeaponFireOperation"),"ProcessWeaponFire",3);
                if(!fire.IsStatic||fire.ReturnType.FullName!="System.Void"||
                    fire.Parameters[0].ParameterType.FullName!="BBI.Core.ComponentModel.Entity"||
                    fire.Parameters[1].ParameterType.FullName!="BBI.Core.Utility.FixedPoint.Fixed64"||
                    fire.Parameters[2].ParameterType.FullName!="BBI.Game.Simulation.WeaponFireOperation")
                    throw new InvalidOperationException("Unexpected native weapon-fire signature");
                TypeReference gateOwner=new TypeReference("EndfieldDoK.MapUnitLab","HumanoidFireGate",module,reference);
                MethodReference gate=new MethodReference("BeforeProcess",module.TypeSystem.Void,gateOwner);
                gate.Parameters.Add(new ParameterDefinition(fire.Parameters[0].ParameterType));
                gate.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                ILProcessor fireIL=fire.Body.GetILProcessor();Instruction fireFirst=fire.Body.Instructions[0];
                fireIL.InsertBefore(fireFirst,Instruction.Create(OpCodes.Ldarg_0));
                fireIL.InsertBefore(fireFirst,Instruction.Create(OpCodes.Ldarg_2));
                fireIL.InsertBefore(fireFirst,Instruction.Create(OpCodes.Call,gate));
                fire.Body.MaxStackSize=Math.Max(fire.Body.MaxStackSize,8);
                PatchRhodesHangarLaunchStart(module,reference);
                PatchRhodesUnifiedRepair(module,reference);
                PatchManualTiltrotorSortie(module,reference);
                PatchOperatorProductionIdentity(module,reference);
                PatchRhodesPowerCommand(module,reference);
                PatchRhodesCarrierRecall(module,reference);
                PatchRhodesPowerCreationTrace(module,reference);
                PatchEmbarkedManualPriority(module,reference);
            }
            tick.Body.MaxStackSize=Math.Max(tick.Body.MaxStackSize,8);
        }
        static void PatchEmbarkedManualPriority(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition perform=Find(module.GetType("BBI.Game.Simulation.UseWeaponAbilityBehaviour"),"OnPerform",3);
            if(perform.IsStatic||perform.ReturnType.FullName!="System.Void"||
                perform.Parameters[0].ParameterType.FullName!="BBI.Game.Simulation.AbilityActivationRequest&"||
                perform.Parameters[1].ParameterType.FullName!="System.Boolean&"||perform.Parameters[2].ParameterType.FullName!="System.Boolean&")
                throw new InvalidOperationException("Unexpected native manual UseWeapon ability ABI");
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesEmbarkedOperatorSkills",module,runtime);
            MethodReference before=new MethodReference("BeforeManualAbility",module.TypeSystem.Void,owner);
            before.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ILProcessor il=perform.Body.GetILProcessor();Instruction first=perform.Body.Instructions[0];
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_1));il.InsertBefore(first,Instruction.Create(OpCodes.Ldind_Ref));
            il.InsertBefore(first,Instruction.Create(OpCodes.Call,before));perform.Body.MaxStackSize=Math.Max(perform.Body.MaxStackSize,8);
        }
        static void PatchRhodesPowerCommand(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition method=Find(module.GetType("BBI.Game.Commands.ModifyPowerSystemCommand"),"RunCommand",3);
            if(method.IsStatic||method.ReturnType.FullName!="System.Boolean"||
                method.Parameters[0].ParameterType.FullName!="BBI.Game.Simulation.Sim"||
                method.Parameters[1].ParameterType.FullName!="BBI.Game.Simulation.ICommanderInteractionProvider"||
                method.Parameters[2].ParameterType.FullName!="BBI.Game.Commands.SimFrameNumber"||method.Body.Variables.Count!=7||
                method.Body.Variables[0].VariableType.FullName!="BBI.Core.ComponentModel.Entity"||
                method.Body.Variables[1].VariableType.FullName!="BBI.Game.Simulation.PowerShunt"||
                method.Body.Variables[2].VariableType.FullName!="BBI.Game.Simulation.PowerSystem")
                throw new InvalidOperationException("Unexpected native per-entity power command ABI");
            Instruction lookup=null;
            foreach(Instruction instruction in method.Body.Instructions)
            {
                MethodReference target=instruction.Operand as MethodReference;
                if(target==null||target.FullName!="System.Boolean BBI.Game.Simulation.PowerShunt::TryGetPowerSystem(BBI.Game.Data.PowerSystemType,BBI.Game.Simulation.PowerSystem&)")continue;
                if(lookup!=null||instruction.OpCode!=OpCodes.Callvirt)throw new InvalidOperationException("Ambiguous native power-system lookup");
                lookup=instruction;
            }
            if(lookup==null||lookup.Next==null||lookup.Next.OpCode!=OpCodes.Brfalse||lookup.Next.Next==null)
                throw new InvalidOperationException("Missing native per-entity power-system checkpoint");
            Instruction first=lookup.Next.Next,continuation=lookup.Next.Operand as Instruction;
            if(first.OpCode!=OpCodes.Ldarg_0||first.Next==null||first.Next.OpCode!=OpCodes.Ldfld||
                ((FieldReference)first.Next.Operand).FullName!="BBI.Game.Commands.ModifyPowerSystemAction BBI.Game.Commands.ModifyPowerSystemCommand::Action"||
                first.Next.Next==null||first.Next.Next.Next==null||first.Next.Next.Next.Next==null||first.Next.Next.Next.Next.OpCode!=OpCodes.Switch||
                continuation==null||continuation.OpCode!=OpCodes.Ldloc_S||((VariableDefinition)continuation.Operand).Index!=4)
                throw new InvalidOperationException("Unexpected native power action/loop checkpoint");
            foreach(Instruction instruction in method.Body.Instructions)
            {
                if(object.ReferenceEquals(instruction.Operand,first))throw new InvalidOperationException("Alternate native power checkpoint entry");
                Instruction[] targets=instruction.Operand as Instruction[];
                if(targets!=null)foreach(Instruction target in targets)if(object.ReferenceEquals(target,first))throw new InvalidOperationException("Alternate native power checkpoint switch entry");
            }
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesPowerGate",module,runtime);
            MethodReference gate=new MethodReference("BeforePowerChange",module.TypeSystem.Boolean,owner);
            gate.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            gate.Parameters.Add(new ParameterDefinition(method.Body.Variables[0].VariableType));
            gate.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));gate.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            gate.Parameters.Add(new ParameterDefinition(method.Parameters[2].ParameterType));
            ILProcessor il=method.Body.GetILProcessor();
            foreach(Instruction instruction in new Instruction[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldloc_1),Instruction.Create(OpCodes.Ldloc_2),Instruction.Create(OpCodes.Ldarg_3),
                Instruction.Create(OpCodes.Call,gate),Instruction.Create(OpCodes.Brfalse,continuation)})il.InsertBefore(first,instruction);
            WidenLifecycleBranches(method);method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void PatchRhodesPowerCreationTrace(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition method=Find(module.GetType("BBI.Game.Simulation.PowerShunt"),"Create",2);
            if(!method.IsStatic||method.ReturnType.FullName!="BBI.Game.Simulation.PowerShunt"||
                method.Parameters[0].ParameterType.FullName!="BBI.Core.ComponentModel.Entity"||
                method.Parameters[1].ParameterType.FullName!="BBI.Game.Data.PowerShuntAttributes"||
                method.Body.ExceptionHandlers.Count!=0||method.Body.Variables.Count!=0||method.Body.Instructions.Count!=4)
                throw new InvalidOperationException("Unexpected native PowerShunt factory ABI");
            Mono.Collections.Generic.Collection<Instruction> body=method.Body.Instructions;
            MethodReference constructor=body[2].Operand as MethodReference;
            if(body[0].OpCode!=OpCodes.Ldarg_0||body[1].OpCode!=OpCodes.Ldarg_1||body[2].OpCode!=OpCodes.Newobj||
                constructor==null||constructor.FullName!="System.Void BBI.Game.Simulation.PowerShunt::.ctor(BBI.Core.ComponentModel.Entity,BBI.Game.Data.PowerShuntAttributes)"||body[3].OpCode!=OpCodes.Ret)
                throw new InvalidOperationException("Native PowerShunt factory body differs");
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesPowerGate",module,runtime);
            MethodReference trace=new MethodReference("AfterNativeCreate",module.TypeSystem.Void,owner);
            trace.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            trace.Parameters.Add(new ParameterDefinition(method.Parameters[0].ParameterType));
            trace.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ILProcessor il=method.Body.GetILProcessor();Instruction exit=body[3];
            // Leave the exact native factory result on the stack for ret. The
            // bounded observer only reads creation state; no refill or reset.
            foreach(Instruction instruction in new Instruction[]{Instruction.Create(OpCodes.Dup),Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,trace)})il.InsertBefore(exit,instruction);
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,4);
        }
        static void PatchRhodesCarrierRecall(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition method=Find(module.GetType("BBI.Game.Simulation.DockAllLaunchedAbilityBehaviour"),"OnPerform",3);
            if(method.IsStatic||method.ReturnType.FullName!="System.Void"||
                method.Parameters[0].ParameterType.FullName!="BBI.Game.Simulation.AbilityActivationRequest&"||
                method.Parameters[1].ParameterType.FullName!="System.Boolean&"||method.Parameters[2].ParameterType.FullName!="System.Boolean&"||
                method.Body.Variables.Count!=12||method.Body.Variables[1].VariableType.FullName!="BBI.Core.ComponentModel.Entity"||
                method.Body.Variables[3].VariableType.FullName!="BBI.Core.ComponentModel.Entity"||
                method.Body.Variables[9].VariableType.FullName!="System.Collections.Generic.IEnumerator`1<BBI.Core.ComponentModel.Entity>")
                throw new InvalidOperationException("Unexpected native carrier recall ABI");
            Instruction valid=null,move=null;
            foreach(Instruction instruction in method.Body.Instructions)
            {
                MethodReference target=instruction.Operand as MethodReference;if(target==null)continue;
                if(target.FullName=="System.Boolean BBI.Game.Simulation.EntityComponentExtensions::IsValid(BBI.Core.ComponentModel.Entity)")
                {if(valid!=null)throw new InvalidOperationException("Ambiguous recall validity checkpoint");valid=instruction;}
                if(target.FullName=="System.Boolean System.Collections.IEnumerator::MoveNext()")
                {if(move!=null)throw new InvalidOperationException("Ambiguous recall aircraft iterator");move=instruction;}
            }
            Instruction first=valid==null||valid.Next==null?null:valid.Next.Next,continuation=move==null?null:move.Previous;
            if(valid==null||valid.Previous==null||valid.Previous.OpCode!=OpCodes.Ldloc_3||
                valid.Next.OpCode!=OpCodes.Brfalse||first==null||first.OpCode!=OpCodes.Ldloc_3||
                continuation==null||continuation.OpCode!=OpCodes.Ldloc_S||((VariableDefinition)continuation.Operand).Index!=9)
                throw new InvalidOperationException("Unexpected recall aircraft loop layout");
            foreach(Instruction instruction in method.Body.Instructions)
            {
                if(object.ReferenceEquals(instruction.Operand,first))throw new InvalidOperationException("Alternate recall checkpoint entry");
                Instruction[] targets=instruction.Operand as Instruction[];
                if(targets!=null)foreach(Instruction target in targets)if(object.ReferenceEquals(target,first))throw new InvalidOperationException("Alternate recall switch entry");
            }
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesCarrierRecallPolicy",module,runtime);
            MethodReference filter=new MethodReference("ShouldRecall",module.TypeSystem.Boolean,owner);
            filter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            filter.Parameters.Add(new ParameterDefinition(method.Body.Variables[1].VariableType));
            filter.Parameters.Add(new ParameterDefinition(method.Body.Variables[3].VariableType));
            ILProcessor il=method.Body.GetILProcessor();
            // Skip only U08 for the private Rhodes recall command. The original
            // iterator, native F01/B01 return goals, and request list stay intact.
            foreach(Instruction instruction in new Instruction[]{Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldind_Ref),
                Instruction.Create(OpCodes.Ldloc_1),Instruction.Create(OpCodes.Ldloc_3),Instruction.Create(OpCodes.Call,filter),
                Instruction.Create(OpCodes.Brfalse,continuation)})il.InsertBefore(first,instruction);
            WidenLifecycleBranches(method);method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void PatchOperatorProductionIdentity(ModuleDefinition module,AssemblyNameReference runtime)
        {
            TypeDefinition type=module.GetType("BBI.Game.Simulation.ProduceUnitAbilityBehaviour");
            MethodDefinition frame=Find(type,"OnValidatePrereqsAgainstFrame",2),sim=Find(type,"OnValidatePrereqsAgainstSim",1);
            if(frame.IsStatic||sim.IsStatic||frame.ReturnType.FullName!="System.Boolean"||sim.ReturnType.FullName!="System.Boolean"||
                frame.Parameters[0].ParameterType.FullName!="BBI.Game.Commands.SimStateFrame"||
                frame.Parameters[1].ParameterType.FullName!="BBI.Game.Simulation.AbilityActivationRequestForCommander&"||
                sim.Parameters[0].ParameterType.FullName!="BBI.Game.Simulation.AbilityActivationRequest&")
                throw new InvalidOperationException("Unexpected native production prerequisite ABI");
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesOperatorProductionPolicy",module,runtime);
            MethodReference frameGate=new MethodReference("FilterFrame",module.TypeSystem.Void,owner);
            frameGate.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));frameGate.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            MethodReference simGate=new MethodReference("FilterSim",module.TypeSystem.Void,owner);simGate.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ILProcessor frameIL=frame.Body.GetILProcessor();Instruction frameFirst=frame.Body.Instructions[0];
            frameIL.InsertBefore(frameFirst,Instruction.Create(OpCodes.Ldarg_1));
            frameIL.InsertBefore(frameFirst,Instruction.Create(OpCodes.Ldarg_2));
            frameIL.InsertBefore(frameFirst,Instruction.Create(OpCodes.Ldind_Ref));
            frameIL.InsertBefore(frameFirst,Instruction.Create(OpCodes.Call,frameGate));
            ILProcessor simIL=sim.Body.GetILProcessor();Instruction simFirst=sim.Body.Instructions[0];
            simIL.InsertBefore(simFirst,Instruction.Create(OpCodes.Ldarg_1));
            simIL.InsertBefore(simFirst,Instruction.Create(OpCodes.Ldind_Ref));
            simIL.InsertBefore(simFirst,Instruction.Create(OpCodes.Call,simGate));
            frame.Body.MaxStackSize=Math.Max(frame.Body.MaxStackSize,8);sim.Body.MaxStackSize=Math.Max(sim.Body.MaxStackSize,8);
        }
        static void PatchManualTiltrotorSortie(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition process=Find(module.GetType("BBI.Game.Simulation.AirSortieProcessor"),"ProcessAirSortie",1);
            if(!process.IsStatic||process.ReturnType.FullName!="System.Void"||process.Parameters[0].ParameterType.FullName!="BBI.Core.ComponentModel.Entity")
                throw new InvalidOperationException("Unexpected native air-sortie processing ABI");
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","Runtime",module,runtime);
            MethodReference gate=new MethodReference("ShouldProcessAirSortie",module.TypeSystem.Boolean,owner);
            gate.Parameters.Add(new ParameterDefinition(process.Parameters[0].ParameterType));
            ILProcessor il=process.Body.GetILProcessor();Instruction first=process.Body.Instructions[0];
            // Keep component32 and every native instruction. Manual U08 uses
            // normal Move/Attack/Dock goals; native fixed-wing sortie upkeep
            // would otherwise add idle targets and compulsory ammo return.
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first,Instruction.Create(OpCodes.Call,gate));
            il.InsertBefore(first,Instruction.Create(OpCodes.Brtrue,first));
            il.InsertBefore(first,Instruction.Create(OpCodes.Ret));
            process.Body.MaxStackSize=Math.Max(process.Body.MaxStackSize,8);
        }
        static void PatchRhodesModeVoice(ModuleDefinition module,AssemblyNameReference runtime)
        {
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesModeVoice",module,runtime);
            MethodDefinition load=null;
            TypeDefinition loader=module.GetType("BBI.Unity.Game.Audio.AudioAssetBundleLoader");
            foreach(MethodDefinition method in loader.Methods)
                if(method.Name=="LoadAssetBundle" && method.Parameters.Count==1 &&
                    method.Parameters[0].ParameterType.FullName=="BBI.Unity.Core.Utility.StreamableAssetContainer")load=method;
            if(load==null || load.IsStatic || load.ReturnType.FullName!="System.Void")
                throw new InvalidOperationException("Unexpected native speech hierarchy loader");
            MethodReference before=new MethodReference("BeforeBankLoad",module.TypeSystem.Void,owner);
            before.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            before.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ILProcessor loadIL=load.Body.GetILProcessor();Instruction loadFirst=load.Body.Instructions[0];
            loadIL.InsertBefore(loadFirst,Instruction.Create(OpCodes.Ldarg_0));
            loadIL.InsertBefore(loadFirst,Instruction.Create(OpCodes.Ldarg_1));
            loadIL.InsertBefore(loadFirst,Instruction.Create(OpCodes.Call,before));
            load.Body.MaxStackSize=Math.Max(load.Body.MaxStackSize,8);
            MethodDefinition voice=Find(module.GetType("BBI.Unity.Game.Audio.VOController"),"ExecuteVO",2);
            if(voice.IsStatic || voice.ReturnType.FullName!="System.Boolean" ||
                voice.Parameters[0].ParameterType.FullName!="BBI.Game.Events.VoiceOverEvent" ||
                voice.Parameters[1].ParameterType.FullName!="System.Boolean")
                throw new InvalidOperationException("Unexpected native voice playback signature");
            MethodReference route=new MethodReference("PrepareEvent",module.TypeSystem.Object,owner);
            route.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            route.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            ILProcessor voiceIL=voice.Body.GetILProcessor();Instruction first=voice.Body.Instructions[0];
            voiceIL.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_1));
            voiceIL.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_2));
            voiceIL.InsertBefore(first,Instruction.Create(OpCodes.Call,route));
            voiceIL.InsertBefore(first,Instruction.Create(OpCodes.Castclass,voice.Parameters[0].ParameterType));
            voiceIL.InsertBefore(first,Instruction.Create(OpCodes.Starg_S,voice.Parameters[0]));
            voice.Body.MaxStackSize=Math.Max(voice.Body.MaxStackSize,8);
        }

        static void PatchRhodesUnifiedRepair(ModuleDefinition module,AssemblyNameReference runtime)
        {
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesUnifiedRepair",module,runtime);
            MethodDefinition perform=Find(module.GetType("BBI.Game.Simulation.RepairTargetAbilityBehaviour"),"OnPerform",3);
            if(perform.IsStatic||perform.ReturnType.FullName!="System.Void"||perform.Parameters[0].ParameterType.FullName!="BBI.Game.Simulation.AbilityActivationRequest&"||
                perform.Parameters[1].ParameterType.FullName!="System.Boolean&"||perform.Parameters[2].ParameterType.FullName!="System.Boolean&"||
                perform.Body.Variables.Count<3||perform.Body.Variables[1].VariableType.FullName!="BBI.Core.ComponentModel.Entity"||
                perform.Body.Variables[2].VariableType.FullName!="BBI.Game.Simulation.Ability")
                throw new InvalidOperationException("Unexpected native RepairTarget OnPerform ABI");
            Instruction lookup=null;
            foreach(Instruction instruction in perform.Body.Instructions)
            {
                MethodReference target=instruction.Operand as MethodReference;
                if(target==null||target.FullName!="BBI.Game.Simulation.Ability BBI.Game.Simulation.AbilityHelper::GetAbilityInstanceForUnit(BBI.Core.ComponentModel.Entity,System.String)")continue;
                if(lookup!=null||instruction.OpCode!=OpCodes.Call||target.HasThis)throw new InvalidOperationException("Ambiguous native repair ability lookup");
                lookup=instruction;
            }
            if(lookup==null||lookup.Previous==null||lookup.Previous.OpCode!=OpCodes.Ldfld||
                ((FieldReference)lookup.Previous.Operand).FullName!="System.String BBI.Game.Simulation.AbilityActivationRequest::AbilityTypeName"||
                lookup.Next==null||lookup.Next.OpCode!=OpCodes.Stloc_2)
                throw new InvalidOperationException("Unexpected native repair ability lookup stack/local layout");
            foreach(Instruction instruction in perform.Body.Instructions)
            {
                if(object.ReferenceEquals(instruction.Operand,lookup.Next))throw new InvalidOperationException("Native repair lookup result has alternate stack entry");
                Instruction[] targets=instruction.Operand as Instruction[];
                if(targets!=null)foreach(Instruction target in targets)if(object.ReferenceEquals(target,lookup.Next))
                    throw new InvalidOperationException("Native repair lookup result has alternate switch entry");
            }
            MethodReference route=new MethodReference("ResolveManualAbility",module.TypeSystem.Object,owner);
            route.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            route.Parameters.Add(new ParameterDefinition(perform.Body.Variables[1].VariableType));
            route.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            // Keep the native lookup result as arg0. Resolve separately for
            // each existing source iteration; native request is only read.
            ILProcessor il=perform.Body.GetILProcessor();Instruction after=lookup;
            foreach(Instruction instruction in new Instruction[]{Instruction.Create(OpCodes.Ldloc_1),Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Ldind_Ref),Instruction.Create(OpCodes.Call,route),
                Instruction.Create(OpCodes.Castclass,perform.Body.Variables[2].VariableType)})
            {il.InsertAfter(after,instruction);after=instruction;}
            WidenLifecycleBranches(perform);perform.Body.MaxStackSize=Math.Max(perform.Body.MaxStackSize,8);

            MethodDefinition autocast=Find(module.GetType("BBI.Game.Simulation.Ability"),"SetAutocastEnabled",2);
            if(autocast.IsStatic||autocast.ReturnType.FullName!="System.Void"||autocast.Parameters[0].ParameterType.FullName!="System.Boolean"||autocast.Parameters[1].ParameterType.FullName!="System.Boolean")
                throw new InvalidOperationException("Unexpected native SetAutocastEnabled ABI");
            Instruction exit=null;foreach(Instruction instruction in autocast.Body.Instructions)if(instruction.OpCode==OpCodes.Ret)
            {if(exit!=null)throw new InvalidOperationException("Unexpected native autocast return inventory");exit=instruction;}
            if(exit==null)throw new InvalidOperationException("Native autocast method has no return");
            MethodReference mirror=new MethodReference("MirrorAutocast",module.TypeSystem.Void,owner);
            mirror.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            // Retain native ret identity: unchanged-state early branches also
            // pass through the event-only mirror. Hidden-key calls no-op.
            ILProcessor toggleIL=autocast.Body.GetILProcessor();exit.OpCode=OpCodes.Ldarg_0;exit.Operand=null;
            Instruction call=Instruction.Create(OpCodes.Call,mirror);toggleIL.InsertAfter(exit,call);toggleIL.InsertAfter(call,Instruction.Create(OpCodes.Ret));
            WidenLifecycleBranches(autocast);autocast.Body.MaxStackSize=Math.Max(autocast.Body.MaxStackSize,8);
        }
        static void PatchRhodesHangarLaunchStart(ModuleDefinition module,AssemblyNameReference runtime)
        {
            PatchRhodesHangarDecision(module,runtime,"TickUndocking","CanUndockNextIdle","FilterCanStart",4);
            PatchRhodesHangarDecision(module,runtime,"TickDocking","CanDockNextIdle","FilterCanReceive",6);
        }
        static void PatchRhodesHangarDecision(ModuleDefinition module,AssemblyNameReference runtime,string tickName,string decisionName,string hookName,int queueIndex)
        {
            TypeDefinition hangar=module.GetType("BBI.Game.Simulation.UnitHangar");
            MethodDefinition method=Find(hangar,tickName,0);
            if(method.IsStatic||method.ReturnType.FullName!="System.Void"||hangar.IsValueType)
                throw new InvalidOperationException("Unexpected native hangar tick signature");
            Instruction decision=null;
            foreach(Instruction instruction in method.Body.Instructions)
            {
                MethodReference target=instruction.Operand as MethodReference;
                if(target==null||target.FullName!="System.Boolean BBI.Game.Simulation.DeployQueue::"+decisionName+"(System.Int32&)")continue;
                if(decision!=null)throw new InvalidOperationException("Ambiguous native hangar idle-start decision");
                if(instruction.OpCode!=OpCodes.Callvirt||!target.HasThis||target.Parameters.Count!=1||target.ReturnType.FullName!="System.Boolean")
                    throw new InvalidOperationException("Unexpected native hangar idle-start call ABI");
                decision=instruction;
            }
            if(decision==null||decision.Previous==null||decision.Previous.Previous==null||decision.Next==null)
                throw new InvalidOperationException("Missing native hangar idle-start decision");
            Instruction queueLoad=decision.Previous.Previous,slotLoad=decision.Previous,branch=decision.Next;
            VariableDefinition queue=queueLoad.Operand as VariableDefinition,slot=slotLoad.Operand as VariableDefinition;
            if((queueLoad.OpCode!=OpCodes.Ldloc&&queueLoad.OpCode!=OpCodes.Ldloc_S)||queue==null||queue.Index!=queueIndex||queue.VariableType.FullName!="BBI.Game.Simulation.DeployQueue"||
                (slotLoad.OpCode!=OpCodes.Ldloca&&slotLoad.OpCode!=OpCodes.Ldloca_S)||slot==null||slot.VariableType.FullName!="System.Int32"||
                (branch.OpCode!=OpCodes.Brfalse&&branch.OpCode!=OpCodes.Brfalse_S))
                throw new InvalidOperationException("Unexpected native hangar idle-start stack/local layout");
            foreach(Instruction instruction in method.Body.Instructions)
            {
                if(object.ReferenceEquals(instruction.Operand,branch))throw new InvalidOperationException("Native hangar start branch has an alternate stack entry");
                Instruction[] targets=instruction.Operand as Instruction[];
                if(targets!=null)foreach(Instruction target in targets)if(object.ReferenceEquals(target,branch))
                    throw new InvalidOperationException("Native hangar start branch has an alternate switch entry");
            }
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesDeploymentState",module,runtime);
            MethodReference filter=new MethodReference(hookName,module.TypeSystem.Boolean,owner);
            filter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            filter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            filter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            // Keep the native result on the stack as arg0. Only replace that
            // result before its original brfalse; queue pumping/finalization,
            // active request ages and all recovery code remain native-owned.
            ILProcessor il=method.Body.GetILProcessor();
            Instruction loadThis=Instruction.Create(OpCodes.Ldarg_0),loadQueue=Instruction.Create(OpCodes.Ldloc,queue),call=Instruction.Create(OpCodes.Call,filter);
            il.InsertAfter(decision,loadThis);il.InsertAfter(loadThis,loadQueue);il.InsertAfter(loadQueue,call);
            WidenLifecycleBranches(method);
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void PatchRhodesExperienceTooltip(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition method=Find(module.GetType("BBI.Unity.Game.UI.CommandPanelController"),"CreateExperienceTooltipData",4);
            const string tooltip="BBI.Unity.Game.UI.CommandPanelController/ExperienceTooltipData";
            if(!method.IsStatic||method.ReturnType.FullName!="System.Boolean"||
                method.Parameters[0].ParameterType.FullName!="BBI.Core.ComponentModel.Entity"||
                method.Parameters[1].ParameterType.FullName!="BBI.Unity.Game.Data.ExperienceViewAttributes"||
                method.Parameters[2].ParameterType.FullName!="BBI.Core.Localization.ILocalizationManager"||
                method.Parameters[3].ParameterType.FullName!=tooltip+"&")
                throw new InvalidOperationException("Unexpected native experience tooltip ABI");
            Instruction assignment=null;
            foreach(Instruction instruction in method.Body.Instructions)
            {
                FieldReference field=instruction.Operand as FieldReference;
                if(field==null||field.DeclaringType.FullName!=tooltip||field.Name!="Name"||instruction.OpCode!=OpCodes.Stfld)continue;
                if(assignment!=null||field.FieldType.FullName!="System.String")throw new InvalidOperationException("Ambiguous native experience tooltip name assignment");
                assignment=instruction;
            }
            FieldReference suffix=assignment==null||assignment.Previous==null?null:assignment.Previous.Operand as FieldReference;
            if(suffix==null||assignment.Previous.OpCode!=OpCodes.Ldfld||
                suffix.FullName!="System.String BBI.Game.Simulation.ExperienceState::NameSuffix"||
                assignment.Previous.Previous==null||assignment.Previous.Previous.OpCode!=OpCodes.Ldloc_0||
                assignment.Previous.Previous.Previous==null||assignment.Previous.Previous.Previous.OpCode!=OpCodes.Ldarg_3)
                throw new InvalidOperationException("Unexpected native experience tooltip name stack");
            foreach(Instruction instruction in method.Body.Instructions)
            {
                if(object.ReferenceEquals(instruction.Operand,assignment))throw new InvalidOperationException("Alternate native tooltip name assignment entry");
                Instruction[] targets=instruction.Operand as Instruction[];
                if(targets!=null)foreach(Instruction target in targets)if(object.ReferenceEquals(target,assignment))throw new InvalidOperationException("Alternate native tooltip name assignment switch entry");
            }
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesUnitIdentityPolicy",module,runtime);
            MethodReference resolve=new MethodReference("ResolveNativeTooltipName",module.TypeSystem.String,owner);
            resolve.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            resolve.Parameters.Add(new ParameterDefinition(method.Parameters[0].ParameterType));
            ILProcessor il=method.Body.GetILProcessor();
            il.InsertBefore(assignment,Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(assignment,Instruction.Create(OpCodes.Call,resolve));
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,4);
        }
        static void PatchRhodesInfoPanel(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition method=Find(module.GetType("BBI.Unity.Game.UI.InformationPanelController"),"UpdateDynamicDataForUnit",1);
            if(method.IsStatic||method.ReturnType.FullName!="System.Void"||method.Parameters[0].ParameterType.FullName!="BBI.Core.ComponentModel.Entity")
                throw new InvalidOperationException("Unexpected native information panel dynamic-unit ABI");
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesHudIcon",module,runtime);
            MethodReference hook=new MethodReference("InfoUpdated",module.TypeSystem.Void,owner);
            hook.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            hook.Parameters.Add(new ParameterDefinition(method.Parameters[0].ParameterType));
            List<Instruction> exits=new List<Instruction>();foreach(Instruction instruction in method.Body.Instructions)if(instruction.OpCode==OpCodes.Ret)exits.Add(instruction);
            if(exits.Count!=5)throw new InvalidOperationException("Unexpected native information panel return inventory");
            ILProcessor il=method.Body.GetILProcessor();
            foreach(Instruction exit in exits)
            {
                // Retain the original ret identity so native branches/EH that
                // reach an exit also pass through the post-native notification.
                exit.OpCode=OpCodes.Ldarg_0;exit.Operand=null;
                Instruction entity=Instruction.Create(OpCodes.Ldarg_1),call=Instruction.Create(OpCodes.Call,hook);
                il.InsertAfter(exit,entity);il.InsertAfter(entity,call);il.InsertAfter(call,Instruction.Create(OpCodes.Ret));
            }
            WidenLifecycleBranches(method);
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void PatchUnitViewLifecycle(ModuleDefinition module,AssemblyNameReference runtime)
        {
            TypeDefinition unit=module.GetType("BBI.Unity.Game.World.UnitView");
            MethodDefinition spawned=Find(unit,"OnSpawned",1),despawned=Find(unit,"OnDespawned",0),destroyed=Find(unit,"OnDestroy",0);
            if(spawned.IsStatic||spawned.ReturnType.FullName!="System.Void"||spawned.Parameters[0].ParameterType.FullName!="BBI.Unity.Game.World.UnitView/SpawnSettings"||
                despawned.IsStatic||despawned.ReturnType.FullName!="System.Void"||destroyed.IsStatic||destroyed.ReturnType.FullName!="System.Void")
                throw new InvalidOperationException("Unexpected native unit lifecycle signatures");
            TypeReference registry=new TypeReference("EndfieldDoK.MapUnitLab","UnitViewDiscovery",module,runtime);
            MethodReference add=new MethodReference("OnNativeSpawned",module.TypeSystem.Void,registry);add.Parameters.Add(new ParameterDefinition(unit));
            MethodReference remove=new MethodReference("OnNativeRemoved",module.TypeSystem.Void,registry);remove.Parameters.Add(new ParameterDefinition(unit));
            // Edit each original ret identity in place, so a branch directly to
            // that ret also passes through registration after native success.
            List<Instruction> returns=new List<Instruction>();foreach(Instruction instruction in spawned.Body.Instructions)if(instruction.OpCode==OpCodes.Ret)returns.Add(instruction);
            if(returns.Count!=1)throw new InvalidOperationException("Unexpected native unit spawn return inventory");
            ILProcessor il=spawned.Body.GetILProcessor();
            foreach(Instruction exit in returns){exit.OpCode=OpCodes.Ldarg_0;exit.Operand=null;Instruction call=Instruction.Create(OpCodes.Call,add);il.InsertAfter(exit,call);il.InsertAfter(call,Instruction.Create(OpCodes.Ret));}
            UnitLifecycleRemovalPrefix(despawned,remove);UnitLifecycleRemovalPrefix(destroyed,remove);
            WidenLifecycleBranches(spawned);WidenLifecycleBranches(despawned);WidenLifecycleBranches(destroyed);
            spawned.Body.MaxStackSize=Math.Max(spawned.Body.MaxStackSize,8);
        }
        static void UnitLifecycleRemovalPrefix(MethodDefinition method,MethodReference remove)
        {
            ILProcessor il=method.Body.GetILProcessor();Instruction first=method.Body.Instructions[0];
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(first,Instruction.Create(OpCodes.Call,remove));
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void WidenLifecycleBranches(MethodDefinition method)
        {
            foreach(Instruction instruction in method.Body.Instructions){
                if(instruction.OpCode.OperandType!=OperandType.ShortInlineBrTarget)continue;
                string code=instruction.OpCode.Code.ToString();FieldInfo field=typeof(OpCodes).GetField(code.Substring(0,code.Length-2),BindingFlags.Public|BindingFlags.Static);
                if(field==null)throw new InvalidOperationException("Unknown short lifecycle branch "+code);
                instruction.OpCode=(OpCode)field.GetValue(null);
            }
        }
        static void PatchRhodesNetwork(ModuleDefinition module,AssemblyNameReference runtime)
        {
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesNetworkCompatibility",module,runtime);
            MethodDefinition platform=Find(module.GetType("BBI.Unity.Game.ShipbreakersMain"),"EnableClientPlatform",0);
            Instruction buildStore=null;int registrars=0;
            foreach(Instruction instruction in platform.Body.Instructions){
                MethodReference call=instruction.Operand as MethodReference;
                if(call==null)continue;
                if(call.FullName=="System.String BBI.Core.Collections.OptionSet::get_Item(System.String)"&&instruction.Previous!=null&&instruction.Previous.OpCode==OpCodes.Ldstr&&(string)instruction.Previous.Operand=="BuildID"){
                    if(buildStore!=null||instruction.Next==null||instruction.Next.OpCode!=OpCodes.Stloc_0)throw new InvalidOperationException("Native lobby build local differs");
                    buildStore=instruction.Next;
                }
                if(call.FullName=="System.Void BBI.Steam.SteamNetworkRegistrar::Initialize(System.String,System.Boolean)"||call.FullName=="System.Void GG.EpicGames.EpicNetworkRegistrar::Initialize(System.String,System.Boolean)"){
                    if(instruction.Previous==null||instruction.Previous.OpCode!=OpCodes.Ldloc_1||instruction.Previous.Previous==null||instruction.Previous.Previous.OpCode!=OpCodes.Ldloc_0)
                        throw new InvalidOperationException("Native registrar arguments differ");
                    registrars++;
                }
            }
            if(buildStore==null||registrars!=2)throw new InvalidOperationException("Native lobby version registration differs");
            MethodReference decorate=new MethodReference("DecorateLobbyBuildId",module.TypeSystem.String,owner);
            decorate.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            platform.Body.GetILProcessor().InsertBefore(buildStore,Instruction.Create(OpCodes.Call,decorate));
            // Gate the derived override: returning from its base method would
            // still allow the override to unload previews and StartLoadingGame.
            MethodDefinition start=Find(module.GetType("BBI.Unity.Game.UI.MultiplayerMissionPanel"),"InitiateStartGame",0);
            if(start.ReturnType.FullName!="System.Void"||start.IsStatic)throw new InvalidOperationException("Native custom-lobby start ABI differs");
            MethodReference allow=new MethodReference("AllowStart",module.TypeSystem.Boolean,owner);
            allow.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ILProcessor il=start.Body.GetILProcessor();Instruction first=start.Body.Instructions[0];
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(first,Instruction.Create(OpCodes.Call,allow));
            il.InsertBefore(first,Instruction.Create(OpCodes.Brtrue,first));il.InsertBefore(first,Instruction.Create(OpCodes.Ret));
            start.Body.MaxStackSize=Math.Max(start.Body.MaxStackSize,8);
        }
        static void PatchSteamStartupTrace(ModuleDefinition module,AssemblyNameReference runtime)
        {
            // Observe original startup calls without replacing any Steam/EOS
            // call, branch, ticket, callback, authentication result or return.
            MethodDefinition awake=Find(module.GetType("BBI.Unity.Game.ShipbreakersMain"),"Awake",0);
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","SteamStartupTrace",module,runtime);
            MethodReference trace=new MethodReference("Record",module.TypeSystem.Void,owner);
            trace.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            trace.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            Instruction initialize=null,running=null,epic=null,login=null;
            foreach(Instruction instruction in awake.Body.Instructions){
                MethodReference method=instruction.Operand as MethodReference;
                if(method==null)continue;
                if(method.FullName=="System.Void BBI.Steam.SteamAPIIntegration::Initialize()")initialize=UniqueStartupCall(initialize,instruction);
                if(method.FullName=="System.Boolean BBI.Steam.SteamAPIIntegration::get_IsSteamRunning()")running=UniqueStartupCall(running,instruction);
                if(method.FullName=="System.Void GG.EpicGames.EpicAPIIntegration::Initialize(System.UInt64)")epic=UniqueStartupCall(epic,instruction);
                if(method.FullName=="System.Void BBI.Unity.Game.ShipbreakersMain::StartExternalConnectLoginWithLoginCallbackInfo()")login=UniqueStartupCall(login,instruction);
            }
            if(initialize==null||running==null||epic==null||login==null)throw new InvalidOperationException("Unexpected native Steam startup sequence");
            ILProcessor il=awake.Body.GetILProcessor();
            StartupStageBefore(il,initialize,trace,"BEFORE_STEAM_INITIALIZE");
            // Duplicate only the result already returned to the game. The
            // original boolean stays on the stack for its original branch.
            Instruction duplicate=Instruction.Create(OpCodes.Dup),label=Instruction.Create(OpCodes.Ldstr,"STEAM_RUNNING_RESULT"),record=Instruction.Create(OpCodes.Call,trace);
            il.InsertAfter(running,duplicate);il.InsertAfter(duplicate,label);il.InsertAfter(label,record);
            StartupStageBefore(il,epic,trace,"BEFORE_EPIC_INITIALIZE");
            StartupStageBefore(il,login,trace,"BEFORE_EXTERNAL_CONNECT_LOGIN");
            // Added observations can exceed a short branch's signed-byte
            // range. Preserve each target while widening its encoding.
            foreach(Instruction instruction in awake.Body.Instructions){
                if(instruction.OpCode.OperandType!=OperandType.ShortInlineBrTarget)continue;
                string code=instruction.OpCode.Code.ToString();
                FieldInfo field=typeof(OpCodes).GetField(code.Substring(0,code.Length-2),BindingFlags.Public|BindingFlags.Static);
                if(field==null)throw new InvalidOperationException("Unknown short startup branch "+code);
                instruction.OpCode=(OpCode)field.GetValue(null);
            }
            awake.Body.MaxStackSize=Math.Max(awake.Body.MaxStackSize,8);
        }
        static Instruction UniqueStartupCall(Instruction previous,Instruction current)
        {
            if(previous!=null)throw new InvalidOperationException("Ambiguous native Steam startup call");
            return current;
        }
        static void StartupStageBefore(ILProcessor il,Instruction target,MethodReference trace,string stage)
        {
            il.InsertBefore(target,Instruction.Create(OpCodes.Ldc_I4_0));
            il.InsertBefore(target,Instruction.Create(OpCodes.Ldstr,stage));
            il.InsertBefore(target,Instruction.Create(OpCodes.Call,trace));
        }
        static void PatchAviationEvents(ModuleDefinition module,AssemblyNameReference runtime)
        {
            TypeReference guide=new TypeReference("EndfieldDoK.MapUnitLab","RhodesAviationGuideDriver",module,runtime);
            TypeDefinition unit=module.GetType("BBI.Unity.Game.World.UnitView");
            MethodDefinition docking=Find(unit,"OnDockingUnitOperationEvent",1),hangar=Find(unit,"OnHangarOperationEvent",1);
            AviationEventPrefix(module,docking,guide,"OnDockingUnitOperation","BBI.Game.Events.DockingUnitOperationEvent");
            AviationEventPrefix(module,hangar,guide,"OnHangarOperation","BBI.Game.Events.HangarOperationEvent");
            MethodDefinition presentation=Find(unit,"UpdateUnitAsPresentationControlOnly",0);
            if(presentation.IsStatic||presentation.ReturnType.FullName!="System.Void")throw new InvalidOperationException("Unexpected native aircraft presentation update ABI");
            MethodReference advance=new MethodReference("BeforeAircraftPresentationUpdate",module.TypeSystem.Void,guide);
            advance.Parameters.Add(new ParameterDefinition(unit));
            ILProcessor updateIL=presentation.Body.GetILProcessor();Instruction updateFirst=presentation.Body.Instructions[0];
            updateIL.InsertBefore(updateFirst,Instruction.Create(OpCodes.Ldarg_0));updateIL.InsertBefore(updateFirst,Instruction.Create(OpCodes.Call,advance));
            presentation.Body.MaxStackSize=Math.Max(presentation.Body.MaxStackSize,8);
            // Preserve every Instruction identity and branch target. Only the
            // original unsafe bay[slot].lift lookup is replaced by the scoped
            // native-data bridge; it still returns Fixed64 to UnsafeFloatValue.
            int replacements=0;Mono.Collections.Generic.Collection<Instruction> code=docking.Body.Instructions;
            for(int i=1;i+4<code.Count;i++)
            {
                MethodReference bays=code[i].Operand as MethodReference,lift=code[i+4].Operand as MethodReference;
                FieldReference slot=code[i+2].Operand as FieldReference;
                if(bays==null||bays.DeclaringType.FullName!="BBI.Game.Data.UnitHangarAttributes"||bays.Name!="get_HangarBays")continue;
                if(code[i].OpCode!=OpCodes.Callvirt||code[i-1].OpCode!=OpCodes.Ldloc_2||code[i+1].OpCode!=OpCodes.Ldarg_1||code[i+2].OpCode!=OpCodes.Ldfld||slot==null||slot.Name!="DockSlotIndex"||code[i+3].OpCode!=OpCodes.Ldelem_Ref||code[i+4].OpCode!=OpCodes.Callvirt||lift==null||lift.Name!="get_UndockLiftTime")
                    throw new InvalidOperationException("Unexpected native aircraft lift lookup IL");
                MethodReference bridge=new MethodReference("ResolveUndockLift",lift.ReturnType,guide);
                bridge.Parameters.Add(new ParameterDefinition(bays.DeclaringType));bridge.Parameters.Add(new ParameterDefinition(docking.Parameters[0].ParameterType));
                foreach(int offset in new int[]{0,2,3}){code[i+offset].OpCode=OpCodes.Nop;code[i+offset].Operand=null;}
                code[i+4].OpCode=OpCodes.Call;code[i+4].Operand=bridge;replacements++;
            }
            if(replacements!=1)throw new InvalidOperationException("Native aircraft lift lookup must be replaced exactly once");
            docking.Body.MaxStackSize=Math.Max(docking.Body.MaxStackSize,8);
        }
        static void AviationEventPrefix(ModuleDefinition module,MethodDefinition method,TypeReference guide,string name,string eventType)
        {
            if(method.IsStatic||method.ReturnType.FullName!="System.Void"||method.Parameters[0].ParameterType.FullName!=eventType)
                throw new InvalidOperationException("Unexpected native aviation presentation event ABI");
            MethodReference hook=new MethodReference(name,module.TypeSystem.Void,guide);
            hook.Parameters.Add(new ParameterDefinition(method.DeclaringType));hook.Parameters.Add(new ParameterDefinition(method.Parameters[0].ParameterType));
            ILProcessor il=method.Body.GetILProcessor();Instruction first=method.Body.Instructions[0];
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_1));il.InsertBefore(first,Instruction.Create(OpCodes.Call,hook));
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void PatchNativeHangarUI(ModuleDefinition module,AssemblyNameReference runtime)
        {
            MethodDefinition ctor=Find(module.GetType("BBI.Unity.Game.UI.UnitHangarPanelController"),".ctor",4);
            string[] parameters={"BBI.Unity.Game.UI.UnitInterfaceController","BBI.Unity.Game.UI.UnitHangarPanelController/UnitHangarPanelControllerSettings","BBI.Unity.Game.UI.BlackbirdPanelBase/BlackbirdPanelGlobalLifetimeDependencyContainer","BBI.Unity.Game.UI.BlackbirdPanelBase/BlackbirdPanelSessionDependencyContainer"};
            if(!ctor.IsConstructor||ctor.IsStatic)throw new InvalidOperationException("Unexpected native hangar controller constructor ABI");
            for(int i=0;i<parameters.Length;i++)if(ctor.Parameters[i].ParameterType.FullName!=parameters[i])throw new InvalidOperationException("Unexpected native hangar controller constructor parameter ABI");
            TypeReference carrier=new TypeReference("EndfieldDoK.MapUnitLab","RhodesCarrierUnits",module,runtime);
            NotifyReturns(module,ctor,carrier,"PrepareAviationHangarUI");
        }
        static void PatchHudTheme(ModuleDefinition module,AssemblyNameReference runtime)
        {
            TypeReference theme=new TypeReference("EndfieldDoK.MapUnitLab","EndfieldHudTheme",module,runtime);
            TypeReference rotorAnchor=new TypeReference("EndfieldDoK.MapUnitLab","RhodesTiltrotorPresentationAnchor",module,runtime);
            MethodDefinition unitIcon=Find(module.GetType("BBI.Unity.Game.UI.UnitIconController"),"OnIconSpawned",1);
            if(unitIcon.Parameters[0].ParameterType.FullName!="BBI.Unity.Game.HUD.NGUIIconController")
                throw new InvalidOperationException("Unexpected native unit icon spawn ABI");
            NotifyReturns(module,unitIcon,rotorAnchor,"IconSpawned");
            NotifyReturns(module,Find(module.GetType("BBI.Unity.Game.UI.BlackbirdPanelBase"),"InitializePanel",1),theme,"PanelInitialized");
            TypeDefinition icon=module.GetType("BBI.Unity.Game.HUD.NGUIIconController");
            NotifyReturns(module,Find(icon,"OnSpawned",0),theme,"IconSpawned");
            NotifyReturns(module,Find(module.GetType("BBI.Unity.Game.UI.ProductionQueueController"),"Awake",0),theme,"ProductionQueueInitialized");
            MethodDefinition research=Find(module.GetType("BBI.Unity.Game.UI.ResearchButtonController"),"InitializeResearchItem",2);
            if(research.Parameters[0].ParameterType.FullName!="BBI.Game.Data.ResearchItemAttributes"||research.Parameters[1].ParameterType.FullName!="BBI.Unity.Game.Localize.IGameLocalization")
                throw new InvalidOperationException("Unexpected research item initialization ABI");
            NotifyReturns(module,research,theme,"ResearchItemInitialized");
            NotifyReturns(module,Find(module.GetType("BBI.Unity.Game.UI.InGameMenuPanel"),"OnEnable",0),theme,"PanelOpened");
            MethodDefinition playerSlot=Find(module.GetType("BBI.Unity.Game.UI.PlayerSlotController"),"Initialize",4);
            string[] playerParameters={"BBI.Unity.Game.UI.Frontend.Helpers.MultiplayerSetupMode","System.Collections.Generic.Dictionary`2<System.String,BBI.Unity.Game.UI.PlayerFactionSelection>","BBI.Unity.Game.Utility.CommanderDescription","BBI.Unity.Game.Localize.IGameLocalization"};
            for(int i=0;i<playerParameters.Length;i++)if(playerSlot.Parameters[i].ParameterType.FullName!=playerParameters[i])
                throw new InvalidOperationException("Unexpected native player slot initialization ABI");
            NotifyReturns(module,playerSlot,theme,"PlayerSlotInitialized");
            MethodDefinition messageBox=Find(module.GetType("BBI.Unity.Game.UI.MessageBoxPanel"),"Show",0);
            if(!messageBox.IsVirtual)throw new InvalidOperationException("Unexpected native message box show ABI");
            NotifyReturns(module,messageBox,theme,"PanelOpened");
            MethodDefinition options=Find(module.GetType("BBI.Unity.Game.UI.OptionsPanelBase"),"EnteredOptionsMenu",1);
            if(options.Parameters[0].ParameterType.FullName!="System.Boolean")throw new InvalidOperationException("Unexpected options entry ABI");
            NotifyReturns(module,options,theme,"PanelOpened");
            TypeDefinition visual=module.GetType("BBI.Unity.Game.HUD.NGUIIconController/VisualizationContainer");
            MapAtlasArgument(Find(visual,"SetAtlas",1),theme,0);
            MapContextColourArgument(Find(visual,"SetColour",1),theme,0,false);
            MapColourArgument(Find(visual,"SetBackgroundColour",1),theme,0);
            MapContextColourArgument(Find(icon,"SetColour",2),theme,1,true);
            MapColourArgument(Find(icon,"SetBackgroundColour",2),theme,1);
            MapColourArgument(Find(icon,"SetGlobalColour",1),theme,0);
        }
        static void NotifyReturns(ModuleDefinition module,MethodDefinition method,TypeReference owner,string name)
        {
            if(method.IsStatic||method.ReturnType.FullName!="System.Void")throw new InvalidOperationException("Unexpected HUD lifecycle ABI");
            MethodReference notify=new MethodReference(name,module.TypeSystem.Void,owner);
            notify.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            List<Instruction> exits=new List<Instruction>();
            foreach(Instruction instruction in method.Body.Instructions)if(instruction.OpCode==OpCodes.Ret)exits.Add(instruction);
            ILProcessor il=method.Body.GetILProcessor();
            foreach(Instruction instruction in exits)
            {
                instruction.OpCode=OpCodes.Ldarg_0;instruction.Operand=null;
                Instruction call=Instruction.Create(OpCodes.Call,notify);il.InsertAfter(instruction,call);il.InsertAfter(call,Instruction.Create(OpCodes.Ret));
            }
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void MapColourArgument(MethodDefinition method,TypeReference owner,int index)
        {
            if(method.IsStatic||method.Parameters[index].ParameterType.FullName!="UnityEngine.Color")throw new InvalidOperationException("Unexpected HUD colour ABI");
            ParameterDefinition colour=method.Parameters[index];
            MethodReference map=new MethodReference("MapColour",colour.ParameterType,owner);map.Parameters.Add(new ParameterDefinition(colour.ParameterType));
            ILProcessor il=method.Body.GetILProcessor();Instruction first=method.Body.Instructions[0];
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg,colour));
            il.InsertBefore(first,Instruction.Create(OpCodes.Call,map));
            il.InsertBefore(first,Instruction.Create(OpCodes.Starg,colour));
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void MapContextColourArgument(MethodDefinition method,TypeReference owner,int index,bool variable)
        {
            if(method.IsStatic||method.Parameters[index].ParameterType.FullName!="UnityEngine.Color"||
                (variable&&method.Parameters[0].ParameterType.FullName!="System.String"))
                throw new InvalidOperationException("Unexpected context HUD colour ABI");
            ParameterDefinition colour=method.Parameters[index];ModuleDefinition module=method.Module;
            MethodReference map=new MethodReference(variable?"MapIconColour":"MapContainerColour",colour.ParameterType,owner);
            map.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            if(variable)map.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            map.Parameters.Add(new ParameterDefinition(colour.ParameterType));
            ILProcessor il=method.Body.GetILProcessor();Instruction first=method.Body.Instructions[0];
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));
            if(variable)il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg,method.Parameters[0]));
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg,colour));
            il.InsertBefore(first,Instruction.Create(OpCodes.Call,map));
            il.InsertBefore(first,Instruction.Create(OpCodes.Starg,colour));
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void MapAtlasArgument(MethodDefinition method,TypeReference owner,int index)
        {
            if(method.IsStatic||method.Parameters[index].ParameterType.FullName!="UIAtlas")throw new InvalidOperationException("Unexpected HUD atlas ABI");
            ParameterDefinition atlas=method.Parameters[index];
            MethodReference map=new MethodReference("ThemeAtlas",atlas.ParameterType,owner);map.Parameters.Add(new ParameterDefinition(atlas.ParameterType));
            ILProcessor il=method.Body.GetILProcessor();Instruction first=method.Body.Instructions[0];
            il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg,atlas));
            il.InsertBefore(first,Instruction.Create(OpCodes.Call,map));
            il.InsertBefore(first,Instruction.Create(OpCodes.Starg,atlas));
            method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,8);
        }
        static void PatchPreparedNavigation59(ModuleDefinition module,AssemblyNameReference reference)
        {
            TypeDefinition iterator=module.GetType("BBI.Game.Simulation.Sim/<PrepareSimMap>d__7");
            if(iterator==null)throw new InvalidOperationException("Native map preparation iterator differs");
            MethodDefinition move=Find(iterator,"MoveNext",0);
            if(move.IsStatic||move.ReturnType.FullName!="System.Boolean"||!move.HasBody)
                throw new InvalidOperationException("Native map preparation ABI differs");
            Instruction anchor=null;FieldReference simulation=null;
            foreach(Instruction instruction in move.Body.Instructions)
            {
                FieldReference field=instruction.Operand as FieldReference;
                if(instruction.OpCode!=OpCodes.Stfld||field==null||field.Name!="mSimInitializationState"||field.DeclaringType.FullName!="BBI.Game.Simulation.Sim")continue;
                if(instruction.Previous==null||instruction.Previous.OpCode!=OpCodes.Ldc_I4_3)continue;
                Instruction load=instruction.Previous.Previous;
                FieldReference owner=load==null?null:load.Operand as FieldReference;
                if(anchor!=null||load.OpCode!=OpCodes.Ldfld||owner==null||owner.Name!="<>4__this"||owner.FieldType.FullName!="BBI.Game.Simulation.Sim"||load.Previous.OpCode!=OpCodes.Ldarg_0)
                    throw new InvalidOperationException("Native map-ready commit anchor differs");
                anchor=load.Previous;simulation=owner;
            }
            if(anchor==null)throw new InvalidOperationException("Native map-ready commit absent");
            TypeReference ownerType=new TypeReference("EndfieldDoK.MapUnitLab","RhodesNavigation59",module,reference);
            MethodReference ready=new MethodReference("AfterMapPrepared",module.TypeSystem.Void,ownerType);ready.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            // Keep the original branch target. Both deserialization and bake
            // paths execute repair before publishing Ready, exactly once.
            ILProcessor il=move.Body.GetILProcessor();Instruction cursor=anchor;
            foreach(Instruction instruction in new Instruction[]{Instruction.Create(OpCodes.Ldfld,simulation),Instruction.Create(OpCodes.Call,ready),Instruction.Create(OpCodes.Ldarg_0)})
            {il.InsertAfter(cursor,instruction);cursor=instruction;}
            move.Body.MaxStackSize=Math.Max(move.Body.MaxStackSize,4);
        }
        static void PatchRhodesUI59(ModuleDefinition module,AssemblyNameReference reference)
        {
            TypeDefinition localization=module.GetType("BBI.Unity.Game.Localize.GameLocalizationManager");
            TypeDefinition reticles=module.GetType("BBI.Unity.Game.UI.TargetingReticleController");
            if(localization==null||reticles==null)throw new InvalidOperationException("Native UI59 owners differ");
            MethodDefinition entry=null;
            foreach(MethodDefinition method in localization.Methods)
                if(method.Name=="GetEntry"&&method.Parameters.Count==1&&method.Parameters[0].ParameterType.FullName=="System.String")entry=method;
            if(entry==null||entry.IsStatic||!entry.HasBody||entry.ReturnType.FullName!="BBI.Unity.Game.Localize.LocalizedEntry")
                throw new InvalidOperationException("Native localization entry ABI differs");
            TypeReference textOwner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesLocalization59",module,reference);
            MethodReference resolve=new MethodReference("Resolve",entry.ReturnType,textOwner);resolve.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            VariableDefinition found=new VariableDefinition(entry.ReturnType);entry.Body.Variables.Add(found);entry.Body.InitLocals=true;
            ILProcessor textIL=entry.Body.GetILProcessor();Instruction textFirst=entry.Body.Instructions[0];
            textIL.InsertBefore(textFirst,Instruction.Create(OpCodes.Ldarg_1));textIL.InsertBefore(textFirst,Instruction.Create(OpCodes.Call,resolve));
            textIL.InsertBefore(textFirst,Instruction.Create(OpCodes.Stloc,found));textIL.InsertBefore(textFirst,Instruction.Create(OpCodes.Ldloc,found));
            textIL.InsertBefore(textFirst,Instruction.Create(OpCodes.Brfalse,textFirst));textIL.InsertBefore(textFirst,Instruction.Create(OpCodes.Ldloc,found));
            textIL.InsertBefore(textFirst,Instruction.Create(OpCodes.Ret));entry.Body.MaxStackSize=Math.Max(entry.Body.MaxStackSize,2);

            TypeReference markerOwner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesReticle59",module,reference);
            MethodDefinition update=Find(reticles,"UpdateTargetingReticle",4);
            if(update.IsStatic||!update.HasBody||update.ReturnType.FullName!="System.Void"||update.Parameters[1].ParameterType.FullName!="System.String")
                throw new InvalidOperationException("Native reticle update ABI differs");
            MethodReference allowTargeting=new MethodReference("AllowNativeTargeting",module.TypeSystem.Boolean,markerOwner);
            allowTargeting.Parameters.Add(new ParameterDefinition(reticles));allowTargeting.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            ILProcessor updateIL=update.Body.GetILProcessor();Instruction updateFirst=update.Body.Instructions[0];
            updateIL.InsertBefore(updateFirst,Instruction.Create(OpCodes.Ldarg_0));updateIL.InsertBefore(updateFirst,Instruction.Create(OpCodes.Ldarg_2));
            updateIL.InsertBefore(updateFirst,Instruction.Create(OpCodes.Call,allowTargeting));updateIL.InsertBefore(updateFirst,Instruction.Create(OpCodes.Brtrue,updateFirst));
            updateIL.InsertBefore(updateFirst,Instruction.Create(OpCodes.Ret));update.Body.MaxStackSize=Math.Max(update.Body.MaxStackSize,2);

            MethodDefinition sortie=Find(reticles,"OnAirSortieEvent",1);
            if(sortie.IsStatic||!sortie.HasBody||sortie.ReturnType.FullName!="System.Void"||sortie.Parameters[0].ParameterType.FullName!="BBI.Game.Events.AirSortieEvent"||
                sortie.Body.Variables.Count<3||sortie.Body.Variables[2].VariableType.FullName!="BBI.Game.Simulation.AirSortieState")
                throw new InvalidOperationException("Native sortie reticle ABI differs");
            Instruction anchor=null;bool feedbackBefore=false;
            foreach(Instruction instruction in sortie.Body.Instructions)
            {
                MethodReference call=instruction.Operand as MethodReference;
                if(call!=null&&call.Name=="OnAirSortieEvent"&&call.DeclaringType.FullName=="BBI.Unity.Game.World.ReticleSprite")feedbackBefore=true;
                FieldReference field=instruction.Operand as FieldReference;
                MethodReference next=instruction.Next==null?null:instruction.Next.Operand as MethodReference;
                if(instruction.OpCode==OpCodes.Ldfld&&field!=null&&field.Name=="LaunchedByAbility"&&field.DeclaringType.FullName=="BBI.Game.Simulation.AirSortieState"&&
                    next!=null&&next.Name=="IsNullOrEmpty"&&next.DeclaringType.FullName=="System.String")
                {
                    if(anchor!=null||!feedbackBefore||instruction.Previous.OpCode!=OpCodes.Ldloc_2)
                        throw new InvalidOperationException("Native sortie reticle guard anchor differs");
                    anchor=instruction.Previous;
                }
            }
            if(anchor==null)throw new InvalidOperationException("Native sortie reticle guard anchor absent");
            MethodReference allowSortie=new MethodReference("AllowNativeSortieFeedback",module.TypeSystem.Boolean,markerOwner);
            allowSortie.Parameters.Add(new ParameterDefinition(reticles));allowSortie.Parameters.Add(new ParameterDefinition(sortie.Body.Variables[2].VariableType));
            // Mutate the old branch target in place so incoming loop exits must
            // pass through the guard after updating all existing feedback.
            anchor.OpCode=OpCodes.Ldarg_0;anchor.Operand=null;
            ILProcessor sortieIL=sortie.Body.GetILProcessor();Instruction cursor=anchor;
            Instruction continuation=Instruction.Create(OpCodes.Ldloc_2);
            foreach(Instruction instruction in new Instruction[]{Instruction.Create(OpCodes.Ldloc_2),Instruction.Create(OpCodes.Call,allowSortie),
                Instruction.Create(OpCodes.Brtrue,continuation),Instruction.Create(OpCodes.Ret),continuation})
            {sortieIL.InsertAfter(cursor,instruction);cursor=instruction;}
            sortie.Body.MaxStackSize=Math.Max(sortie.Body.MaxStackSize,2);
        }
        static void PatchDeferredAssetEnable59(ModuleDefinition module,AssemblyNameReference reference)
        {
            AssemblyNameReference unity=null;
            foreach(AssemblyNameReference item in module.AssemblyReferences)if(item.Name=="UnityEngine")unity=item;
            if(unity==null)throw new InvalidOperationException("Native data assembly Unity reference differs");
            TypeReference owner=new TypeReference("EndfieldDoK.MapUnitLab","RhodesAssetClone59",module,reference);
            MethodReference guard=new MethodReference("ShouldDeferEnable",module.TypeSystem.Boolean,owner);
            guard.Parameters.Add(new ParameterDefinition(new TypeReference("UnityEngine","ScriptableObject",module,unity)));
            foreach(string name in new string[]{"AssetContainer","UnitAssetContainer","UnitMovementAttributesAsset"})
            {
                TypeDefinition type=module.GetType("BBI.Unity.Game.Data."+name);
                if(type==null)throw new InvalidOperationException("Native asset enable type differs: "+name);
                MethodDefinition method=Find(type,"OnEnable",0);
                if(method.IsStatic||method.ReturnType.FullName!="System.Void"||!method.HasBody)
                    throw new InvalidOperationException("Native asset enable ABI differs: "+name);
                ILProcessor il=method.Body.GetILProcessor();Instruction first=method.Body.Instructions[0];
                il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first,Instruction.Create(OpCodes.Call,guard));
                il.InsertBefore(first,Instruction.Create(OpCodes.Brfalse,first));
                il.InsertBefore(first,Instruction.Create(OpCodes.Ret));
                method.Body.MaxStackSize=Math.Max(method.Body.MaxStackSize,2);
            }
        }
        static MethodDefinition Find(TypeDefinition type,string name,int count)
        {
            foreach(MethodDefinition method in type.Methods)if(method.Name==name&&method.Parameters.Count==count)return method;
            throw new MissingMethodException(type.FullName,name);
        }
    }
}
