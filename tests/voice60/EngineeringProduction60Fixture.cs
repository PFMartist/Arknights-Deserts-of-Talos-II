using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

internal static class EngineeringProduction60Fixture
{
    static int Checks;
    static string Native;
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static void Check(bool value,string reason)
    {Checks++;if(!value)throw new InvalidOperationException(reason);}
    static Assembly Resolve(object sender,ResolveEventArgs args)
    {
        string file=Path.Combine(Native,new AssemblyName(args.Name).Name+".dll");
        return File.Exists(file)?Assembly.LoadFrom(file):null;
    }
    static object Event(Type type,string action,string summary,string detail)
    {
        ConstructorInfo ctor=null;
        foreach(ConstructorInfo item in type.GetConstructors(All))
            if(item.GetParameters().Length==14)ctor=item;
        Check(ctor!=null,"native fourteen-argument voice event constructor");
        ParameterInfo[] parameters=ctor.GetParameters();
        object[] values=new object[parameters.Length];
        for(int i=0;i<values.Length;i++)
            values[i]=parameters[i].ParameterType==typeof(string)?string.Empty:
                parameters[i].ParameterType.IsValueType?Activator.CreateInstance(parameters[i].ParameterType):null;
        values[1]=action;values[4]=summary;values[7]=detail;values[13]="cal";
        return ctor.Invoke(values);
    }
    static object Read(object value,string field)
    {return value.GetType().GetField(field,All).GetValue(value);}
    static int Main(string[] args)
    {
        try{return Run(args);}
        catch(Exception error)
        {
            for(Exception current=error;current!=null;current=current.InnerException)
                Console.WriteLine("FAIL "+current.GetType().FullName+": "+current.Message);
            return 1;
        }
    }
    static int Run(string[] args)
    {
        Native=args[1];AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        Assembly runtime=Assembly.LoadFrom(args[0]);
        Type aliases=runtime.GetType("EndfieldDoK.MapUnitLab.RhodesUnitVoiceAliases",true);
        MethodInfo production=aliases.GetMethod("ProductionEventKey",All);
        Type nativeEvent=production.GetParameters()[0].ParameterType;
        const string key="RI_ENGINEERING_PRODUCTION_READY",token="EF_RI_U03_VOICE";
        object ready=Event(nativeEvent,"prc","opsfleet",token);
        Check((string)production.Invoke(null,new object[]{ready})==key,"engineering event selects repair production report");
        Check((string)Read(ready,"SecondaryDetail")==token,"source event retains exact private unit token");
        Check((string)production.Invoke(null,new object[]{Event(nativeEvent,"PRC","OpsFleet",token)})==key,"native action and role comparison stays case insensitive");
        Check(production.Invoke(null,new object[]{null})==null,"null event retains native handling");
        foreach(string action in new string[]{"sel","mov","unf","des","rsh"})
            Check(production.Invoke(null,new object[]{Event(nativeEvent,action,"opsfleet",token)})==null,"non-production action stays native: "+action);
        foreach(string role in new string[]{"opscombat","carrier","engineering","sensors"})
            Check(production.Invoke(null,new object[]{Event(nativeEvent,"prc",role,token)})==null,"other role stays native: "+role);
        foreach(string unit in new string[]{"bas","csp","C_Baserunner_MP","EF_RI_U02_PROBE_VOICE","unknown",null})
            Check(production.Invoke(null,new object[]{Event(nativeEvent,"prc","opsfleet",unit)})==null,"other unit preserves its route: "+unit);
        string[] preservedTokens={"EF_RI_U09_NATIVE_AA","EF_RI_U08_VOICE","EF_RI_LAEVAT_VOICE","EF_RI_GILBERTA_VOICE","EF_RI_ARDELIA_VOICE"};
        string[] preservedKeys={"RI_ARTILLERY_PRODUCTION_READY","RI_BAD_GUY_PRODUCTION_READY","RI_OPERATOR_LAEVAT_READY","RI_OPERATOR_GILBERTA_READY","RI_OPERATOR_ARDELIA_READY"};
        for(int i=0;i<preservedTokens.Length;i++)
            Check((string)production.Invoke(null,new object[]{Event(nativeEvent,"prc","opsfleet",preservedTokens[i])})==preservedKeys[i],"existing custom production route stays intact");

        Type catalogType=runtime.GetType("EndfieldDoK.MapUnitLab.RhodesVoiceCatalog",true);
        string[] lines=File.ReadAllLines(args[2]),baseline=File.ReadAllLines(args[3]);
        Check(lines.Length==baseline.Length+1,"one dedicated route is added");
        for(int i=0;i<baseline.Length;i++)Check(lines[i]==baseline[i],"original route preserved: "+i);
        object catalog=catalogType.GetMethod("Parse",All).Invoke(null,new object[]{lines});
        object[] query={key,null};
        Check((bool)catalogType.GetMethod("TryGet",All).Invoke(catalog,query),"dedicated route parses and is enabled");
        object route=query[1];
        Check((string)Read(route,"Role")=="OpsFleet","Kaltsit report uses native fleet mixer and queue");
        Check((string)Read(route,"Clip")=="clips/kaltsit/opsfleet_f3882c74003715.wav","dedicated route binds existing engineering recording");
        Check((string)Read(route,"EventKey")==key&&(string)Read(route,"CustomKey")==key,"private registration retains exact production identity");
        Check((string)Read(route,"UnitType")==string.Empty,"clip loader can resolve RI private event key");
        object[] load={key,null};
        Check((bool)catalogType.GetMethod("TryLoadRoute",All).Invoke(catalog,load),"actual private clip lookup succeeds");
        int privateMatches=0;
        foreach(object item in (IEnumerable)catalogType.GetField("PrivateRoutes",All).GetValue(catalog))
            if((string)Read(item,"EventKey")==key)privateMatches++;
        Check(privateMatches==1,"Fabric registers one private engineering event");
        query=new object[]{"vo_coalition_opsfleet_prc_opscspgengen_t1_10626",null};
        Check((bool)catalogType.GetMethod("TryGet",All).Invoke(catalog,query),"original support report remains available");
        Check((string)Read(query[1],"Clip")== (string)Read(route,"Clip"),"reuse has identical clip identity");
        string audioPath=Path.Combine(args[4],((string)Read(route,"Clip")).Replace('/',Path.DirectorySeparatorChar));
        string hash;
        using(SHA256 sha=SHA256.Create())using(FileStream stream=File.OpenRead(audioPath))
            hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",string.Empty).ToLowerInvariant();
        Check(hash==(string)Read(route,"Sha256"),"routed production WAV matches frozen recording hash");
        Console.WriteLine("PASS_ENGINEERING_PRODUCTION60 checks="+Checks+"; originalRoutesPreserved="+(baseline.Length-1)+"; nativeProductionToken=True; privateClipLookup=True; newTTS=False; gameplayAcceptance=Pending");
        return 0;
    }
}
