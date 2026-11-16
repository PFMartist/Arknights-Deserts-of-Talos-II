using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;

// Runs production serialization helpers and real native buff wrappers under
// CLR. Only the immutable base UnitAttributes input is a proxy; Unity/player
// initialization, simulation ticks and game files are never involved.
internal static class NativeCarrierBalanceFixture60
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Assembly Core,Data,Game,Mod;
    static Type Carrier,Profile,UnitType,EntryType;
    static int BaseHealth,BaseArmour,BaseSensor,BaseContact;
    static readonly int[] ExpectedArmour={25,30,40,60,80,100};
    static readonly int[] ExpectedSensor={800,900,1100,1400,1700,2000};
    static readonly int[] ExpectedContact={1250,1350,1550,1850,2150,2450};

    sealed class UnitProxy:RealProxy
    {
        readonly Type Contract;
        internal UnitProxy(Type type):base(type){Contract=type;}
        public override IMessage Invoke(IMessage message)
        {
            IMethodCallMessage call=(IMethodCallMessage)message;
            MethodInfo method=(MethodInfo)call.MethodBase;
            object result=null;
            if(method.Name=="GetType")result=Contract;
            else if(method.Name=="Equals")result=object.ReferenceEquals(GetTransparentProxy(),call.Args[0]);
            else if(method.Name=="get_Armour")result=Number(method.ReturnType,BaseArmour);
            else if(method.Name=="get_SensorRadius")result=Number(method.ReturnType,BaseSensor);
            else if(method.Name=="get_ContactRadius")result=Number(method.ReturnType,BaseContact);
            else if(method.Name=="get_MaxHealth")result=Number(method.ReturnType,BaseHealth);
            else if(method.ReturnType==typeof(string))result="CarrierBalanceBase60";
            else if(method.ReturnType.IsArray)result=Array.CreateInstance(method.ReturnType.GetElementType(),0);
            else if(method.ReturnType.IsValueType)result=Activator.CreateInstance(method.ReturnType);
            return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
        }
    }

    static object Number(Type type,int value)
    {
        if(type.FullName=="BBI.Core.Utility.FixedPoint.Fixed64")
            return type.GetMethod("FromInt",All).Invoke(null,new object[]{value});
        return Convert.ChangeType(value,type,CultureInfo.InvariantCulture);
    }
    static double Numeric(object value)
    {
        if(value.GetType().FullName=="BBI.Core.Utility.FixedPoint.Fixed64")
            return Convert.ToDouble(value.GetType().GetMethod("UnsafeFloatValue",All).Invoke(null,new object[]{value}),CultureInfo.InvariantCulture);
        return Convert.ToDouble(value,CultureInfo.InvariantCulture);
    }
    static void Require(bool test,string message){if(!test)throw new InvalidOperationException(message);}
    static void Equal(double actual,double expected,string message)
    {Require(Math.Abs(actual-expected)<.001,message+": expected "+expected+", got "+actual);}
    static object Call(Type type,string name,params object[] args)
    {return type.GetMethod(name,All).Invoke(null,args);}
    static int ProfileInt(string name,int value){return (int)Call(Profile,name,value);}
    static object EntityType(string name)
    {
        object value=Activator.CreateInstance(Core.GetType("BBI.Core.Data.EntityTypeAttributes",true),new object[]{name});
        value.GetType().GetMethod("Add",All).Invoke(value,new object[]{new UnitProxy(UnitType).GetTransparentProxy()});
        NativeBuff("AddBuffs",value,null);
        return value;
    }
    static object Copy(object value)
    {return Activator.CreateInstance(value.GetType(),new object[]{value});}
    static object NativeUnit(object entityType)
    {
        foreach(MethodInfo method in entityType.GetType().GetMethods(All))
            if(method.Name=="Get"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==0)
                return method.MakeGenericMethod(UnitType).Invoke(entityType,null);
        throw new MissingMethodException("Native EntityTypeAttributes.Get<T>");
    }
    static void NativeBuff(string operation,object entityType,object set)
    {
        Type extensions=Game.GetType("BBI.Game.Simulation.EntityTypeBuffExtensions",true);
        foreach(MethodInfo method in extensions.GetMethods(All))
            if(method.Name==operation&&method.GetParameters().Length==2&&method.GetParameters()[0].ParameterType==entityType.GetType())
            {method.Invoke(null,new object[]{entityType,set});return;}
        throw new MissingMethodException("Native "+operation+"(EntityTypeAttributes, buff)");
    }
    static object ProductionBuff(int level)
    {
        int armour=ProfileInt("ArmourIncrease",level),sensor=ProfileInt("SensorContactIncrease",level);
        Array entries=Array.CreateInstance(EntryType,3);
        entries.SetValue(Activator.CreateInstance(EntryType,All,null,new object[]{"",65540,0,armour},null),0);
        entries.SetValue(Activator.CreateInstance(EntryType,All,null,new object[]{"",65542,0,sensor},null),1);
        entries.SetValue(Activator.CreateInstance(EntryType,All,null,new object[]{"",65543,0,sensor},null),2);
        return Call(Carrier,"CreatePowerBuffData",entries);
    }
    static void AssertUnit(object entityType,int level,string phase)
    {
        object unit=NativeUnit(entityType);
        Equal(Numeric(unit.GetType().GetProperty("Armour",All).GetValue(unit,null)),ExpectedArmour[level],phase+" armour L"+level);
        Equal(Numeric(unit.GetType().GetProperty("SensorRadius",All).GetValue(unit,null)),ExpectedSensor[level],phase+" sensor L"+level);
        Equal(Numeric(unit.GetType().GetProperty("ContactRadius",All).GetValue(unit,null)),ExpectedContact[level],phase+" contact L"+level);
        Equal(Numeric(unit.GetType().GetProperty("MaxHealth",All).GetValue(unit,null)),15000,phase+" health unchanged");
    }

    static void CheckConfiguration(string root)
    {
        Type settingsType=Carrier.GetNestedType("Configuration",BindingFlags.NonPublic);
        object settings=Activator.CreateInstance(settingsType,true);
        BaseHealth=(int)settingsType.GetField("Health",All).GetValue(settings);
        BaseArmour=(int)settingsType.GetField("Armour",All).GetValue(settings);
        BaseSensor=(int)(float)settingsType.GetField("Sensor",All).GetValue(settings);
        BaseContact=(int)(float)settingsType.GetField("Contact",All).GetValue(settings);
        Equal(BaseHealth,15000,"compiled default health");Equal(BaseArmour,25,"compiled default armour");
        Equal(BaseSensor,800,"compiled default sensor");Equal(BaseContact,1250,"compiled default contact");
        Equal((float)settingsType.GetField("Speed",All).GetValue(settings),60,"compiled default speed");
        Equal((float)settingsType.GetField("Length",All).GetValue(settings),600,"compiled default length unchanged");
        if(string.IsNullOrEmpty(root)||root=="-"){Console.WriteLine("PASS CONFIG compiled defaults; external INI not supplied, INI comparison skipped");return;}
        string iniPath=Path.GetFullPath(root);
        Dictionary<string,double> ini=new Dictionary<string,double>(StringComparer.Ordinal);
        foreach(string line in File.ReadAllLines(iniPath))
        {
            string text=line.Trim();if(text.StartsWith("#")||text.StartsWith(";")||!text.Contains("="))continue;
            string[] pair=text.Split(new char[]{'='},2);
            ini.Add(pair[0].Trim(),double.Parse(pair[1].Trim(),CultureInfo.InvariantCulture));
        }
        Equal(ini["HitPoints"],BaseHealth,"overlay health equals compiled default");
        Equal(ini["Armour"],BaseArmour,"overlay armour equals compiled default");
        Equal(ini["SensorRadiusMetres"],BaseSensor,"overlay sensor equals compiled default");
        Equal(ini["ContactRadiusMetres"],BaseContact,"overlay contact equals compiled default");
        Equal(ini["MaxSpeedMetresPerSecond"],60,"overlay speed unchanged");
        Equal(ini["LengthMetres"],600,"overlay length unchanged");
        Console.WriteLine("PASS CONFIG compiled private Configuration defaults match overlay INI; HP15000/armour25/speed60/sensor800/contact1250/length600");
    }

    static void CheckUnchanged(string referencePath)
    {
        Type old=Assembly.LoadFrom(referencePath).GetType(Profile.FullName,true);
        string[] functions={"StartingLevel","ExtraPowerCost","CumulativePowerCost","CapacityForResearchLevel","HullWeaponDamagePercent","AircraftDamagePercent","InterceptorServicePercent","RepairPulse","RepairDamagePercent","DamageReceivedPercent","SpeedPercent","MainRangePercent","CiwsDamage","CiwsRateOfFire","CiwsRange"};
        foreach(string function in functions)
        {
            int limit=function=="StartingLevel"?3:5;
            for(int level=0;level<=limit;level++)
                Equal(ProfileInt(function,level),(int)Call(old,function,level),"Hotfix59 preserved "+function+" L"+level);
        }
        foreach(FieldInfo field in old.GetFields(BindingFlags.Public|BindingFlags.Static))
            if(field.IsLiteral)Require(object.Equals(field.GetRawConstantValue(),Profile.GetField(field.Name).GetRawConstantValue()),"Power constant changed "+field.Name);
        Type thermal=Mod.GetType("EndfieldDoK.MapUnitLab.RhodesThermalProfile",true),oldThermal=old.Assembly.GetType(thermal.FullName,true);
        foreach(FieldInfo field in oldThermal.GetFields(BindingFlags.Public|BindingFlags.Static))
            if(field.IsLiteral)Require(object.Equals(field.GetRawConstantValue(),thermal.GetField(field.Name).GetRawConstantValue()),"Thermal constant changed "+field.Name);
        for(int level=0;level<=5;level++)
        {
            Equal((int)Call(thermal,"HeatPointsForLevel",level),(int)Call(oldThermal,"HeatPointsForLevel",level),"incremental heat unchanged");
            Equal((int)Call(thermal,"CumulativeHeat",level),(int)Call(oldThermal,"CumulativeHeat",level),"cumulative heat unchanged");
        }
        Console.WriteLine("PASS SCOPE all 15 unrelated profile functions/constants and native thermal settings equal frozen Hotfix59 source");
    }

    static void CheckUI()
    {
        for(int level=0;level<=5;level++)
        {
            string armour=(string)Call(Carrier,"PowerDescription",1,level),sensor=(string)Call(Carrier,"PowerDescription",3,level);
            if(level==0)
            {Require(armour.Contains("维持基础能力")&&sensor.Contains("维持基础能力"),"zero-power UI lost base state");continue;}
            Require(armour.Contains("本舰装甲+"+(ExpectedArmour[level]-BaseArmour)+"；"),"damage-control UI differs L"+level+": "+armour);
            Require(sensor.Contains("侦测与接触范围+"+(ExpectedSensor[level]-BaseSensor)+"；"),"fire-control UI differs L"+level+": "+sensor);
            Require(armour.Contains("累计占用"+ProfileInt("CumulativePowerCost",level)+"点")&&sensor.Contains("累计占用"+ProfileInt("CumulativePowerCost",level)+"点"),"UI power costs differ L"+level);
        }
        string armourLong=(string)Call(Carrier,"PowerLongDescription",1),sensorLong=(string)Call(Carrier,"PowerLongDescription",3);
        Require(armourLong.Contains("装甲增加5/15/35/55/75"),"damage-control long UI has stale curve");
        Require(armourLong.Contains("维修每次恢复1/2/2/3/3")&&armourLong.Contains("不能维修自身"),"repair/self-repair scope changed");
        Require(sensorLong.Contains("100/300/600/900/1200"),"fire-control long UI has stale curve");
        Require(!sensorLong.Contains("每档+100")&&!armourLong.Contains("2/4/7/10/14"),"old hard-coded UI text survived");
        Console.WriteLine("PASS UI actual compiled per-level and long description methods match native results and unchanged repair scope");
    }

    static void CheckNativeLayers()
    {
        for(int level=0;level<=5;level++)
        {
            object type=EntityType("Balance60Commander"+level),existing=Copy(type),buff=ProductionBuff(level);
            if(level>0)NativeBuff("AddBuffs",existing,buff);
            AssertUnit(existing,level,"instance");AssertUnit(type,0,"owner isolation");
            // Native type-level route plus a later-created instance exercises
            // both wrappers, rather than treating returned profile ints as stats.
            if(level>0)NativeBuff("AddBuffs",type,buff);
            object fresh=Copy(type);AssertUnit(type,level,"type");AssertUnit(fresh,level,"new instance");
            Console.WriteLine("LEVEL "+level+" armour="+ExpectedArmour[level]+" sensor="+ExpectedSensor[level]+" contact="+ExpectedContact[level]+"; actual native type and instance calculations PASS");
        }
        object transitioning=Copy(EntityType("Balance60Transitions")),previous=null;
        foreach(int level in new int[]{1,5,2,4,3,0})
        {
            if(previous!=null)NativeBuff("RemoveBuffs",transitioning,previous);
            previous=level==0?null:ProductionBuff(level);
            if(previous!=null)NativeBuff("AddBuffs",transitioning,previous);
            AssertUnit(transitioning,level,"charge/drain replacement");
        }
        Console.WriteLine("PASS TRANSITIONS native remove/add 1->5->2->4->3->0; no cumulative buff leak and base stats restored");
    }

    static int Main(string[] args)
    {
        try
        {
            string native=args[0],modPath=args[1],root=args[2],reference=args[3];
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs evt)
            {
                string path=Path.Combine(native,new AssemblyName(evt.Name).Name+".dll");
                return File.Exists(path)?Assembly.LoadFrom(path):null;
            };
            Core=Assembly.LoadFrom(Path.Combine(native,"BBI.Core.dll"));Data=Assembly.LoadFrom(Path.Combine(native,"BBI.Game.Data.dll"));Game=Assembly.LoadFrom(Path.Combine(native,"BBI.Game.dll"));
            Mod=Assembly.LoadFrom(modPath);Carrier=Mod.GetType("EndfieldDoK.MapUnitLab.RhodesCarrierUnits",true);Profile=Mod.GetType("EndfieldDoK.MapUnitLab.RhodesPowerProfile",true);
            UnitType=Data.GetType("BBI.Game.Data.UnitAttributes",true);EntryType=Carrier.GetNestedType("PowerBuffEntry",BindingFlags.NonPublic);
            CheckConfiguration(root);CheckUnchanged(reference);CheckUI();CheckNativeLayers();
            Console.WriteLine("PASS ALL offline/native-attribute scope only; Unity player and simulation not started; gameplay acceptance remains manual");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
