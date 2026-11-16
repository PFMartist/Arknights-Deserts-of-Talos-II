using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using BBI.Core.Data;
using BBI.Core.IO.Streams;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;

class CarrierNavigation62Fixture
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly Core,Data,UnityData,Game,Runtime;
    static MethodInfo Configure,Verify;
    static int Checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static readonly List<Dictionary<string,object>> Results=new List<Dictionary<string,object>>();

    static void Check(bool okay,string message){Checks++;if(!okay)throw new Exception(message);}
    static Dictionary<string,object> D(object value){return (Dictionary<string,object>)value;}
    static FieldInfo Field(Type type,string name)
    {for(Type t=type;t!=null;t=t.BaseType){FieldInfo f=t.GetField(name,Flags|BindingFlags.DeclaredOnly);if(f!=null)return f;}throw new MissingFieldException(type.FullName,name);}
    static object Get(object value,string name)
    {
        for(Type t=value.GetType();t!=null;t=t.BaseType)
        {FieldInfo f=t.GetField(name,Flags|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(value);PropertyInfo p=t.GetProperty(name,Flags|BindingFlags.DeclaredOnly);if(p!=null)return p.GetValue(value,null);}
        throw new MissingMemberException(value.GetType().FullName,name);
    }
    static void Set(object value,string name,object data){Field(value.GetType(),name).SetValue(value,data);}
    static Type N(string name){return Core.GetType(name,false)??Data.GetType(name,false)??UnityData.GetType(name,false)??Game.GetType(name,true);}
    static object Decode(Type type,object value)
    {
        if(value==null)return null;
        if(type.IsEnum)return Enum.ToObject(type,Convert.ToInt32(value));
        if(type==typeof(string))return (string)value;
        if(type.IsPrimitive)return Convert.ChangeType(value,type,CultureInfo.InvariantCulture);
        if(type.IsArray){IList input=(IList)value;Array array=Array.CreateInstance(type.GetElementType(),input.Count);for(int i=0;i<input.Count;i++)array.SetValue(Decode(type.GetElementType(),input[i]),i);return array;}
        object result=Activator.CreateInstance(type,true);
        foreach(KeyValuePair<string,object> row in D(value))
        {FieldInfo field=null;try{field=Field(type,row.Key);}catch(MissingFieldException){}if(field!=null&&!field.IsStatic)field.SetValue(result,Decode(field.FieldType,row.Value));}
        return result;
    }
    static object Snapshot(object value)
    {
        if(value==null)return null;Type type=value.GetType();
        if(type.IsEnum)return Convert.ToInt32(value);
        if(type.IsPrimitive||type==typeof(string))return value;
        Array array=value as Array;if(array!=null){object[] result=new object[array.Length];for(int i=0;i<result.Length;i++)result[i]=Snapshot(array.GetValue(i));return result;}
        SortedDictionary<string,object> fields=new SortedDictionary<string,object>(StringComparer.Ordinal);
        for(Type t=type;t!=null&&t!=typeof(object);t=t.BaseType)foreach(FieldInfo field in t.GetFields(Flags|BindingFlags.DeclaredOnly))
            if(!field.IsStatic)fields[t.FullName+"."+field.Name]=Snapshot(field.GetValue(value));
        return fields;
    }
    static string SnapshotText(object[] values){return Json.Serialize(Snapshot(values));}
    static float F(Fixed64 value){return Fixed64.UnsafeFloatValue(value);}
    static void Equal(Fixed64 a,Fixed64 b,string message){Check(a==b,message+" exact Fixed64");}
    static byte[] NavBytes(SerializableNavMeshAttributes profile)
    {using(MemoryStream stream=new MemoryStream()){profile.Write(new BinaryStreamWriter(stream));return stream.ToArray();}}
    static void Apply(object[] values){Configure.Invoke(null,values);}

    static void Test(Dictionary<string,object> input)
    {
        string name=(string)input["unit"];
        Dictionary<string,object> nav=D(D(input["navigation"])["tree"]),movement=D(D(D(input["movement"])["tree"])["m_Simulation"]);
        object donorNav=Decode(N("BBI.Unity.Game.Data.NavMeshData"),nav["m_NavMeshConfig"]);
        object donorMovement=Decode(N("BBI.Unity.Game.Data.UnitMovementAttributesData"),movement);
        object donorDynamics=Get(donorMovement,"m_Dynamics"),donorManeuvers=Get(donorMovement,"m_Maneuvers"),donorAvoidance=Get(donorMovement,"m_Avoidance");
        object customNav=Decode(N("BBI.Unity.Game.Data.NavMeshData"),nav["m_NavMeshConfig"]);
        object customMovement=Decode(N("BBI.Unity.Game.Data.UnitMovementAttributesData"),movement);
        object dynamics=Get(customMovement,"m_Dynamics"),maneuvers=Get(customMovement,"m_Maneuvers"),avoidance=Get(customMovement,"m_Avoidance");
        // Explicit Hotfix61 negative starting values establish that this is the
        // actual compiled mutator, rather than a log/getter-only correction.
        Set(customNav,"m_DistanceFromObstacles",305f);Set(customNav,"m_DistanceErrorPercentageTolerance",.3f);Set(customNav,"m_BlockedBy",Enum.ToObject(Field(customNav.GetType(),"m_BlockedBy").FieldType,1));
        Set(dynamics,"m_Width",272.1483f);Set(dynamics,"m_Length",600f);Set(dynamics,"m_MaxSpeedTurnRadius",270f);
        Set(maneuvers,"m_ObstacleAvoidanceDistance",305f);Set(maneuvers,"m_ArrivalDistance",15f);Set(avoidance,"m_AvoidanceDistance",305f);
        Set(dynamics,"m_MaxSpeed",60f);Set(dynamics,"m_MinCruiseSpeed",45f);Set(dynamics,"m_AccelerationTime",10f);Set(dynamics,"m_BrakingTime",6f);Set(dynamics,"m_ReverseFactor",.35f);
        Set(maneuvers,"m_FormationSpacingDistance",691f);Set(maneuvers,"m_FormationSectionGapAhead",30f*600f/550f);
        object[] originals={donorNav,donorDynamics,donorManeuvers,donorAvoidance};string before=SnapshotText(originals);
        object[] values={customNav,dynamics,maneuvers,avoidance,donorNav,donorDynamics,donorManeuvers,donorAvoidance};
        Apply(values);Verify.Invoke(null,values);
        Check(before==SnapshotText(originals),name+" original donor raw fields remain unchanged");

        // Register the actual prepared native data objects in an ordinary
        // EntityTypeAttributes graph and read the interfaces native consumers use.
        Dictionary<string,object> coreJson=D(D(D(input["core"])["tree"])["m_Attributes"]);
        object core=Decode(N("BBI.Game.Data.UnitAttributesData"),coreJson);Set(core,"m_NavMesh",customNav);
        EntityTypeAttributes definition=new EntityTypeAttributes("NAV62_FIXTURE_"+name);
        Check(definition.Add(core),"prepared core registered");Check(definition.Add(customMovement),"prepared movement registered");
        UnitMovementAttributes prepared=definition.Get<UnitMovementAttributes>();UnitAttributes preparedCore=definition.Get<UnitAttributes>();
        UnitMovementAttributes donor=(UnitMovementAttributes)donorMovement;NavMeshAttributes nativeNav=(NavMeshAttributes)donorNav;
        Equal(preparedCore.NavMeshAttributes.DistanceFromObstacles,nativeNav.DistanceFromObstacles,name+" registered clearance");
        Equal(preparedCore.NavMeshAttributes.DistanceErrorPercentageTolerance,nativeNav.DistanceErrorPercentageTolerance,name+" registered tolerance");
        Check(preparedCore.NavMeshAttributes.BlockedBy==nativeNav.BlockedBy,name+" registered blocker flags");
        Equal(prepared.Dynamics.Width,donor.Dynamics.Width,name+" registered width");Equal(prepared.Dynamics.Length,donor.Dynamics.Length,name+" registered length");
        Equal(prepared.Dynamics.MaxSpeedTurnRadius,donor.Dynamics.MaxSpeedTurnRadius,name+" registered turn radius");
        Equal(prepared.Maneuvers.ObstacleAvoidanceDistance,donor.Maneuvers.ObstacleAvoidanceDistance,name+" registered obstacle avoidance");
        Equal(prepared.Maneuvers.ArrivalDistance,donor.Maneuvers.ArrivalDistance,name+" registered arrival");
        Equal(prepared.Avoidance.AvoidanceDistance,donor.Avoidance.AvoidanceDistance,name+" registered dynamic avoidance");
        Equal(prepared.Dynamics.MaxSpeed,Fixed64.FromConstFloat(60f),"61 speed retained");Equal(prepared.Dynamics.MinCruiseSpeed,Fixed64.FromConstFloat(45f),"61 cruise retained");
        Equal(prepared.Dynamics.AccelerationTime,Fixed64.FromConstFloat(10f),"61 acceleration retained");Equal(prepared.Dynamics.BrakingTime,Fixed64.FromConstFloat(6f),"61 braking retained");
        Equal(prepared.Dynamics.ReverseFactor,Fixed64.FromConstFloat(.35f),"61 reverse retained");Equal(prepared.Maneuvers.FormationSpacingDistance,Fixed64.FromConstFloat(691f),"visual formation pitch retained");
        Check(preparedCore.MaxHealth==Convert.ToInt32(coreJson["m_MaxHealth"])&&preparedCore.Armour==Convert.ToInt32(coreJson["m_Armour"]),"prepared core health and armor retained");

        SerializableNavMeshAttributes serial=new SerializableNavMeshAttributes(preparedCore.NavMeshAttributes);
        byte[] bytes=NavBytes(serial),expected=NavBytes(new SerializableNavMeshAttributes(nativeNav));
        Check(Convert.ToBase64String(bytes)==Convert.ToBase64String(expected),name+" native nav serialization bytes match donor");
        SerializableNavMeshAttributes restored=new SerializableNavMeshAttributes();restored.Read(new BinaryStreamReader(new MemoryStream(bytes,false)));
        Equal(restored.DistanceFromObstacles,nativeNav.DistanceFromObstacles,"serialized exact clearance roundtrip");Equal(restored.DistanceErrorPercentageTolerance,nativeNav.DistanceErrorPercentageTolerance,"serialized exact tolerance roundtrip");
        Check(restored.BlockedBy==nativeNav.BlockedBy,"serialized exact blocker roundtrip");
        Fixed64 radius=prepared.Dynamics.Width>>1,axis=prepared.Dynamics.Length-(radius<<1);
        Type shapeType=Game.GetType("BBI.Game.Simulation.Shape",true);
        object shape=shapeType.GetMethod("Create",Flags).Invoke(null,new object[]{Vector2r.Zero,Orientation2.LocalForward,radius,axis,false,false,Fixed64.Zero});
        object capsule=Get(shape,"Capsule");Equal((Fixed64)Get(capsule,"Radius"),radius,"real native Shape radius");Equal((Fixed64)Get(shape,"mCapsuleAxisLength"),axis,"real native Shape axis length");
        if(name=="C_Carrier_MP"){Equal(radius,Fixed64.FromInt(125),"Coalition capsule radius125");Equal(axis,Fixed64.FromInt(300),"Coalition capsule axis300");}
        // Idempotence, then a corrupted prepared value must fail verification.
        string configured=SnapshotText(new object[]{customNav,dynamics,maneuvers,avoidance});Apply(values);
        Check(configured==SnapshotText(new object[]{customNav,dynamics,maneuvers,avoidance}),"configuration is idempotent");
        Set(customNav,"m_DistanceFromObstacles",305f);bool rejected=false;
        try{Verify.Invoke(null,values);}catch(TargetInvocationException){rejected=true;}Check(rejected,"verify rejects legacy305 after preparation");Apply(values);
        object[] aliases=(object[])values.Clone();aliases[0]=donorNav;rejected=false;
        try{Apply(aliases);}catch(TargetInvocationException){rejected=true;}Check(rejected,"actual mutator rejects aliased native data");
        Check(before==SnapshotText(originals),"negative controls did not mutate donor");
        Results.Add(new Dictionary<string,object>{{"donor",name},{"width",F(prepared.Dynamics.Width)},{"length",F(prepared.Dynamics.Length)},
            {"clearance",F(preparedCore.NavMeshAttributes.DistanceFromObstacles)},{"blockedBy",(uint)preparedCore.NavMeshAttributes.BlockedBy},{"capsuleRadius",F(radius)},{"capsuleAxisLength",F(axis)},
            {"navigationSerializedBytes",Convert.ToBase64String(bytes)},{"nativeDonorUnchanged",true},{"nineSpatialFieldsMatch",true}});
    }

    static int Main(string[] args)
    {
        try
        {
            string native=Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string path=Path.Combine(native,new AssemblyName(e.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
            return Run(args);
        }
        catch(Exception e){for(Exception x=e;x!=null;x=x.InnerException)Console.Error.WriteLine(x);return 1;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]static int Run(string[] args)
    {
        Json.MaxJsonLength=64*1024*1024;
        Core=typeof(EntityTypeAttributes).Assembly;Data=typeof(UnitAttributes).Assembly;UnityData=Assembly.LoadFrom(Path.Combine(args[0],"BBI.Unity.Game.Data.dll"));Game=Assembly.LoadFrom(Path.Combine(args[0],"BBI.Game.dll"));Runtime=Assembly.LoadFrom(args[1]);
        Type carrier=Runtime.GetType("EndfieldDoK.MapUnitLab.RhodesCarrierUnits",true);
        Configure=carrier.GetMethod("ConfigureNativeCarrierNavigation",Flags);Verify=carrier.GetMethod("VerifyNativeCarrierNavigation",Flags);
        Check(Configure!=null&&Verify!=null,"actual candidate DLL navigation entry points present");
        var document=D(Json.DeserializeObject(File.ReadAllText(args[2])));
        foreach(object row in (object[])document["carriers"])Test(D(row));
        Console.WriteLine(Json.Serialize(new Dictionary<string,object>{{"status","PASS_ACTUAL_DLL_NATIVE_NAVIGATION62"},{"checks",Checks},{"donors",Results},
            {"actualCandidateDllInvoked",true},{"nativeDataInterfacesAndEntityTypeRegistration",true},{"nativeSerializableNavAndShapeInvoked",true},
            {"fullUnityConfigureInvoked",false},{"unityNativeFunctionsInvoked",false},{"gameStarted",false},{"runtimeRouteAcceptance",false}}));
        return 0;
    }
}
