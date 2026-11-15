using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using EndfieldDoK.MapUnitLab;

internal static class Clone59Fixture
{
    static int Checks;
    sealed class Asset
    {
        internal object Data;
        internal int Enabled;
        internal void Enable()
        {
            if(RhodesAssetCreationPolicy59.ShouldDefer(this))return;
            if(Data==null)throw new NullReferenceException("empty fields");
            Enabled++;
        }
    }
    sealed class OtherAsset{}
    static void Check(bool value,string reason){Checks++;if(!value)throw new InvalidOperationException(reason);}
    static void Expect(Action action,string reason)
    {bool threw=false;try{action();}catch(InvalidOperationException){threw=true;}Check(threw,reason);}
    static void Policies()
    {
        Asset donor=new Asset{Data=new object()};donor.Enable();
        Check(donor.Enabled==1,"native callback outside controlled creation must run");
        Asset oldEmpty=new Asset();bool oldFailed=false;
        try{oldEmpty.Enable();}catch(NullReferenceException){oldFailed=true;}
        Check(oldFailed,"negative control must expose the premature empty-field callback");
        for(int pass=0;pass<1000;pass++)
        {
            Asset created=new Asset(),unrelated=new Asset{Data=new object()};
            using(RhodesAssetCreationPolicy59.Scope scope=RhodesAssetCreationPolicy59.Begin(typeof(Asset)))
            {
                Check(!RhodesAssetCreationPolicy59.ShouldDefer(new OtherAsset()),"type mismatch is not suppressed");
                created.Enable();Check(created.Enabled==0,"creation callback delayed");
                Check(!RhodesAssetCreationPolicy59.ShouldDefer(unrelated),"second matching instance remains native");
                unrelated.Enable();Check(unrelated.Enabled==1,"native unrelated asset still enabled inside creation scope");
                Check(RhodesAssetCreationPolicy59.ShouldDefer(created),"same-instance base callback may also be delayed");
                scope.RequireReturned(created,true);
                using(RhodesAssetCreationPolicy59.Scope nested=RhodesAssetCreationPolicy59.Begin(typeof(Asset)))
                {
                    Asset inner=new Asset();inner.Enable();nested.RequireReturned(inner,true);
                    Check(!RhodesAssetCreationPolicy59.ShouldDefer(created),"outer instance does not leak into nested scope");
                }
                Check(RhodesAssetCreationPolicy59.ShouldDefer(created),"parent scope restored after nested creation");
                Expect(delegate{scope.RequireReturned(unrelated,true);},"foreign callback/return mismatch rejected");
            }
            created.Data=new object();created.Enable();
            Check(created.Enabled==1,"post-copy explicit initialization runs exactly once");
            Check(!object.ReferenceEquals(created.Data,donor.Data),"caller retains independent mutable data contract");
            Check(!RhodesAssetCreationPolicy59.ShouldDefer(created),"scope never leaks beyond CreateInstance");
        }
        Asset callback=new Asset();
        using(RhodesAssetCreationPolicy59.Scope noCallback=RhodesAssetCreationPolicy59.Begin(typeof(Asset)))
        {
            Expect(delegate{noCallback.RequireReturned(callback,true);},"missing required native hook is rejected");
            noCallback.RequireReturned(callback,false);
        }
        bool caught=false;
        try{using(RhodesAssetCreationPolicy59.Scope scope=RhodesAssetCreationPolicy59.Begin(typeof(Asset))){callback.Enable();throw new Exception("creation failure");}}
        catch(Exception){caught=true;}
        Check(caught&&!RhodesAssetCreationPolicy59.ShouldDefer(callback),"failed creation retires thread state");
        using(RhodesAssetCreationPolicy59.Scope parent=RhodesAssetCreationPolicy59.Begin(typeof(Asset)))
        {
            callback.Enable();Exception workerError=null;int workerPassed=0;
            Thread worker=new Thread(delegate()
            {
                try
                {
                    Check(!RhodesAssetCreationPolicy59.ShouldDefer(callback),"thread-local scope not shared with another thread");
                    using(RhodesAssetCreationPolicy59.Scope own=RhodesAssetCreationPolicy59.Begin(typeof(OtherAsset)))
                    {OtherAsset ownObject=new OtherAsset();Check(RhodesAssetCreationPolicy59.ShouldDefer(ownObject),"independent thread binds its own instance");own.RequireReturned(ownObject,true);}
                    workerPassed=1;
                }catch(Exception error){workerError=error;}
            });worker.Start();worker.Join();if(workerError!=null)throw workerError;
            Check(workerPassed==1&&RhodesAssetCreationPolicy59.ShouldDefer(callback),"main-thread binding survived independent thread scope");
        }
    }
    static bool Calls(MethodDefinition method,string fragment)
    {if(!method.HasBody)return false;foreach(Instruction item in method.Body.Instructions)if(item.Operand is MethodReference&&item.Operand.ToString().Contains(fragment))return true;return false;}
    static TypeDefinition Type(AssemblyDefinition assembly,string name)
    {foreach(TypeDefinition type in assembly.MainModule.Types)if(type.FullName==name)return type;throw new InvalidOperationException("Type missing: "+name);}
    static MethodDefinition Method(TypeDefinition type,string name)
    {foreach(MethodDefinition method in type.Methods)if(method.Name==name&&method.Parameters.Count==0)return method;throw new InvalidOperationException("Method missing: "+type.FullName+"."+name);}
    static void Native(string nativeFile)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(nativeFile))
        {
            string[] types={"BBI.Unity.Game.Data.AssetContainer","BBI.Unity.Game.Data.UnitAssetContainer","BBI.Unity.Game.Data.UnitMovementAttributesAsset"};
            foreach(string name in types)
            {
                MethodDefinition method=Method(Type(assembly,name),"OnEnable");
                Check(!method.IsStatic&&method.ReturnType.FullName=="System.Void"&&method.HasBody,"supported native callback signature");
            }
            Check(Calls(Method(Type(assembly,types[0]),"OnEnable"),"EnumerateAllAssetData"),"container builds its cache from populated fields");
            Check(Calls(Method(Type(assembly,types[1]),"OnEnable"),"InitializeAttributes"),"instantiating a source container could mutate borrowed child asset data");
            Check(Calls(Method(Type(assembly,types[2]),"OnEnable"),"MultiAssetBase::OnEnable"),"movement initialization has its supported base callback");
        }
    }
    static void Runtime(string file)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(file))
        {
            TypeDefinition helper=Type(assembly,"EndfieldDoK.MapUnitLab.RhodesAssetClone59");
            MethodDefinition create=null,guard=null;
            foreach(MethodDefinition method in helper.Methods)
            {
                if(method.Name=="CreateEmpty"&&!method.HasGenericParameters)create=method;
                if(method.Name=="ShouldDeferEnable")guard=method;
            }
            Check(helper.IsPublic&&guard!=null&&guard.IsPublic&&guard.IsStatic&&guard.ReturnType.FullName=="System.Boolean","hook accessible to patched native assembly");
            Check(create!=null&&Calls(create,"UnityEngine.ScriptableObject::CreateInstance"),"actual creation keeps native API");
            Check(Calls(create,"RhodesAssetCreationPolicy59::Begin")&&Calls(create,"::RequireReturned"),"creation verifies scope and exact returned instance");
            Check(Calls(create,"::Dispose"),"creation always has scope disposal");
            bool finallyFound=false;foreach(ExceptionHandler handler in create.Body.ExceptionHandlers)if(handler.HandlerType==ExceptionHandlerType.Finally)finallyFound=true;
            Check(finallyFound,"creation cleanup is a real finally block");
            Check(!Calls(create,"UnityEngine.Object::Instantiate"),"does not instantiate source container or borrow its mutable child state");
        }
    }
    static void Patched(string file)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(file))
            foreach(string name in new string[]{"BBI.Unity.Game.Data.AssetContainer","BBI.Unity.Game.Data.UnitAssetContainer","BBI.Unity.Game.Data.UnitMovementAttributesAsset"})
            {
                MethodDefinition method=Method(Type(assembly,name),"OnEnable");
                Check(method.Body.Instructions.Count>=5,"native guard prefix exists");
                Check(method.Body.Instructions[0].OpCode.Code==Code.Ldarg_0,"guard receives exact callback instance");
                Check(method.Body.Instructions[1].Operand is MethodReference&&method.Body.Instructions[1].Operand.ToString().Contains("RhodesAssetClone59::ShouldDeferEnable"),"guard runs before any original field access");
                Check(method.Body.Instructions[2].OpCode.Code==Code.Brfalse||method.Body.Instructions[2].OpCode.Code==Code.Brfalse_S,"false guard preserves normal callback");
                Check(method.Body.Instructions[3].OpCode.Code==Code.Ret,"only controlled empty-shell callback returns early");
            }
    }
    static int Main(string[] args)
    {
        try
        {
            Policies();Native(args[0]);if(args.Length>1)Runtime(args[1]);if(args.Length>2)Patched(args[2]);
            Console.WriteLine("{\"status\":\"PASS_CLONE59_OFFLINE\",\"checks\":"+Checks+",\"policyCycles\":1000,\"runtimeMetadataChecked\":"+(args.Length>1?"true":"false")+",\"patchedNativeMetadataChecked\":"+(args.Length>2?"true":"false")+",\"UnityExecuted\":false}");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
