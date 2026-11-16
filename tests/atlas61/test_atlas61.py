"""Execute the exact production MapAtlas/Needs methods with counted NGUI stubs."""
from pathlib import Path
import hashlib,json,re,subprocess,os

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).parent
AUDIT=ROOT/"out/tests/atlas61"
AUDIT.mkdir(parents=True,exist_ok=True)
FILES=["AtlasPositiveCache61.cs","OperatorUiIdentity.cs","RhodesHudIcon.cs","RhodesUnitIcons.cs","OperatorPortraitIcons.cs","OperatorSkillIcons.cs"]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()

def method(filename,name):
    text=(ROOT/"src"/filename).read_text(encoding="utf-8-sig")
    match=re.search(r"^        (?:public |internal |private )?static [^\n]+\b"+re.escape(name)+r"\([^\n]*\)\s*\n?",text,re.M)
    assert match,(filename,name)
    start=match.start();opening=text.index("{",match.start())
    # All selected bodies have ordinary literals/comments. Remove their brace
    # significance while retaining source offsets for exact text extraction.
    token=re.compile(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/')
    scrub=token.sub(lambda m:" "*len(m.group()),text)
    depth=0
    for index in range(opening,len(text)):
        if scrub[index]=="{":depth+=1
        elif scrub[index]=="}":
            depth-=1
            if depth==0:return text[start:index+1]
    raise AssertionError("Unclosed production method")

prefix=r'''
using System;
using System.Collections.Generic;
using EndfieldDoK.MapUnitLab;
public sealed class UISpriteData { public string name; }
public sealed class UIAtlas
{
    public List<UISpriteData> spriteList=new List<UISpriteData>();
    public int Lookups;
    public UISpriteData GetSprite(string name){Lookups++;foreach(UISpriteData sprite in spriteList)if(sprite.name==name)return sprite;return null;}
    public void Add(string name){spriteList.Add(new UISpriteData{name=name});}
    public UIAtlas Copy(){UIAtlas result=new UIAtlas();result.spriteList.AddRange(spriteList);return result;}
}
public struct Color32 {}
namespace EndfieldDoK.MapUnitLab
{
    public static class Runtime {public static int Logs;public static void Log(string value){Logs++;}}
    internal static class ArdeliaHealingAssets {internal const string AbilityKey="EF_Ardelia_HealingField";}
    public static class RhodesHudIcon
    {
        const string SpriteName="EF_RHODES_Info";
        static readonly string[] Names={SpriteName,"EF_RHODES_Info_AV","EF_RHODES_Info_GN"};
        static string ReferenceName="carrier";
        static int ReferenceGeneration=1;
        static readonly AtlasPositiveCache61 Cache=new AtlasPositiveCache61();
        static bool Warned;
        public static int Builds;
        static UIAtlas BuildAtlas(UIAtlas source,UISpriteData reference){Builds++;UIAtlas result=source.Copy();foreach(string name in Names)if(result.GetSprite(name)==null)result.Add(name);return result;}
        public static UIAtlas TestMap(UIAtlas source){return MapCarrierAtlas(source);}
        public static void NewGeneration(){ReferenceGeneration++;}
        // HUD_METHOD
    }
    public static class RhodesUnitIcons
    {
        sealed class Entry {internal string Name,Reference;}
        static readonly List<Entry> Entries=new List<Entry>();
        static readonly AtlasPositiveCache61 Cache=new AtlasPositiveCache61();
        static bool Warned;
        public static int Builds;
        public static void Register(){for(int i=0;i<22;i++)Entries.Add(new Entry{Name="unit"+i,Reference="native"+i});}
        static UIAtlas Build(UIAtlas source){Builds++;UIAtlas result=source.Copy();foreach(Entry entry in Entries)if(source.GetSprite(entry.Reference)!=null&&result.GetSprite(entry.Name)==null)result.Add(entry.Name);return result;}
        // UNIT_NEEDS
        // UNIT_MAP
    }
    public static class OperatorPortraitIcons
    {
        static readonly string[] Fields={"production","information","fleet"};
        sealed class PortraitEntry {internal OperatorUiIdentity Identity;internal string[] References;internal Color32[] Pixels=new Color32[0];}
        static readonly Dictionary<string,PortraitEntry> Entries=new Dictionary<string,PortraitEntry>();
        static readonly AtlasPositiveCache61 Mapped=new AtlasPositiveCache61();
        static int RegistrationGeneration;
        static bool Warned;
        public static int Builds;
        public static void Register(int index){OperatorUiIdentity id=OperatorUiIdentity.All[index];Entries.Add(id.TypeKey,new PortraitEntry{Identity=id,References=new string[]{"op-prod"+index,"op-info"+index,"op-fleet"+index}});RegistrationGeneration++;}
        public static UIAtlas AppendPatterns(UIAtlas source,string[] references,string[] names,Color32[][] pixels){Builds++;UIAtlas result=source.Copy();foreach(string name in names)if(result.GetSprite(name)==null)result.Add(name);return result;}
        // PORTRAIT_NAMES
        // PORTRAIT_NEEDS
        // PORTRAIT_MAP
    }
    public static class OperatorSkillIcons
    {
        sealed class Entry {internal OperatorUiIdentity Identity;internal string Reference;internal Color32[] Pixels=new Color32[0];}
        static readonly Dictionary<string,Entry> Entries=new Dictionary<string,Entry>();
        static readonly AtlasPositiveCache61 Cache=new AtlasPositiveCache61();
        static int RegistrationGeneration;
        static bool Warned;
        public static void Register(int index){OperatorUiIdentity id=OperatorUiIdentity.All[index];Entries.Add(id.TypeKey,new Entry{Identity=id,Reference="op-skill"+index});RegistrationGeneration++;}
        static void EnsurePattern(Entry entry){}
        // SKILL_NEEDS
        // SKILL_MAP
    }
}
'''
replacements={
    "// HUD_METHOD":method("RhodesHudIcon.cs","MapCarrierAtlas"),
    "// UNIT_NEEDS":method("RhodesUnitIcons.cs","Needs"),"// UNIT_MAP":method("RhodesUnitIcons.cs","MapAtlas"),
    "// PORTRAIT_NAMES":method("OperatorPortraitIcons.cs","SpriteNames"),
    "// PORTRAIT_NEEDS":method("OperatorPortraitIcons.cs","Needs"),"// PORTRAIT_MAP":method("OperatorPortraitIcons.cs","MapAtlas"),
    "// SKILL_NEEDS":method("OperatorSkillIcons.cs","Needs"),"// SKILL_MAP":method("OperatorSkillIcons.cs","MapAtlas")}
for marker,body in replacements.items():prefix=prefix.replace(marker,body)
tests=r'''
internal static class Atlas61Fixture
{
    static int Checks;
    static void Check(bool value,string reason){Checks++;if(!value)throw new InvalidOperationException(reason);}
    static UIAtlas Native(){UIAtlas source=new UIAtlas();source.Add("carrier");for(int i=0;i<22;i++)source.Add("native"+i);for(int i=0;i<3;i++){source.Add("op-prod"+i);source.Add("op-info"+i);source.Add("op-fleet"+i);source.Add("op-skill"+i);}return source;}
    static void Hot(string label,Func<UIAtlas,UIAtlas> map,UIAtlas source)
    {
        UIAtlas mapped=map(source);Check(!ReferenceEquals(source,mapped),label+" creates a positive mapping");
        source.Lookups=mapped.Lookups=0;
        for(int i=0;i<1000;i++){Check(ReferenceEquals(map(source),mapped),label+" source cache preserves result");Check(ReferenceEquals(map(mapped),mapped),label+" own result is idempotent");}
        Check(source.Lookups==0&&mapped.Lookups==0,label+" 2000 repeated maps make no GetSprite call");
        source.Add("late-native-reference");UIAtlas grown=map(source);
        Check(!ReferenceEquals(grown,mapped),label+" native sprite-list growth invalidates positive mapping");
        source.spriteList=new List<UISpriteData>(source.spriteList);UIAtlas replaced=map(source);
        Check(!ReferenceEquals(replaced,grown),label+" same-count replacement of source list invalidates mapping");
        Check(replaced.GetSprite("late-native-reference")!=null,label+" newly appended native sprite is preserved");
    }
    static void Main()
    {
        UIAtlas native=Native();
        Check(ReferenceEquals(RhodesUnitIcons.MapAtlas(native),native),"incomplete vehicle registration is not cached");
        RhodesUnitIcons.Register();Hot("vehicle",RhodesUnitIcons.MapAtlas,native);
        Hot("carrier",RhodesHudIcon.TestMap,Native());
        UIAtlas carrier=Native(),old=RhodesHudIcon.TestMap(carrier);RhodesHudIcon.NewGeneration();
        Check(!ReferenceEquals(RhodesHudIcon.TestMap(carrier),old),"carrier reference generation invalidates mapping");
        UIAtlas portrait=Native();Check(ReferenceEquals(OperatorPortraitIcons.MapAtlas(portrait),portrait),"zero portrait entries do not create a negative cache");
        OperatorPortraitIcons.Register(0);UIAtlas first=OperatorPortraitIcons.MapAtlas(portrait);
        OperatorPortraitIcons.Register(1);OperatorPortraitIcons.Register(2);
        UIAtlas all=OperatorPortraitIcons.MapAtlas(portrait);
        Check(!ReferenceEquals(first,all)&&all.GetSprite(OperatorUiIdentity.All[2].InfoName)!=null,"late portrait registration rebuilds all required names");
        Hot("portrait",OperatorPortraitIcons.MapAtlas,Native());
        UIAtlas skill=Native();Check(ReferenceEquals(OperatorSkillIcons.MapAtlas(skill),skill),"zero skill entries do not create a negative cache");
        OperatorSkillIcons.Register(0);UIAtlas one=OperatorSkillIcons.MapAtlas(skill);
        OperatorSkillIcons.Register(1);OperatorSkillIcons.Register(2);UIAtlas three=OperatorSkillIcons.MapAtlas(skill);
        Check(!ReferenceEquals(one,three)&&three.GetSprite(OperatorUiIdentity.All[2].SkillName)!=null,"late skill registration invalidates positive mappings");
        Hot("skill",OperatorSkillIcons.MapAtlas,Native());
        UIAtlas late=new UIAtlas();Check(ReferenceEquals(RhodesHudIcon.TestMap(late),late),"unknown atlas is not negatively cached");late.Add("carrier");
        Check(!ReferenceEquals(RhodesHudIcon.TestMap(late),late),"later donor sprite becomes eligible without explicit cache reset");
        foreach(OperatorUiIdentity id in OperatorUiIdentity.All)
        {
            Check(id.ProductionName==id.Prefix+"_Portrait_Production"&&id.InfoName==id.Prefix+"_Portrait_Info"&&id.FleetName==id.Prefix+"_Portrait_Fleet"&&id.CargoName==id.Prefix+"_Portrait_Cargo","identity strings retain exact values");
            for(int i=0;i<1000;i++)Check(ReferenceEquals(id.ProductionName,id.ProductionName)&&ReferenceEquals(id.InfoName,id.InfoName)&&ReferenceEquals(id.FleetName,id.FleetName)&&ReferenceEquals(id.CargoName,id.CargoName)&&ReferenceEquals(id.SkillName,id.SkillName)&&ReferenceEquals(id.PortraitSpriteNames,id.PortraitSpriteNames),"getter reads reuse precomputed strings and name arrays");
        }
        Check(Runtime.Logs==0,"cache paths do not produce failure logging");
        Console.WriteLine("PASS_ATLAS61 checks="+Checks+"; hotMapCalls=8000; hotGetSpriteCalls=0; lateRegistration=True; nativeListGrowthAndReplacement=True; noNegativeCache=True; sameIdentityStrings=True");
    }
}
'''
fixture=AUDIT/"Atlas61ExtractedFixture.cs"
fixture.write_text(prefix+tests,encoding="utf-8")
exe=AUDIT/"Atlas61Fixture.exe"
compiler=Path(os.environ["WINDIR"])/"Microsoft.NET/Framework64/v4.0.30319/csc.exe"
command=[str(compiler),"/nologo","/target:exe","/optimize+","/warn:4","/warnaserror+","/out:"+str(exe),str(fixture),str(ROOT/"src/AtlasPositiveCache61.cs"),str(ROOT/"src/OperatorUiIdentity.cs")]
build=subprocess.run(command,capture_output=True,text=True)
(AUDIT/"COMPILE61.txt").write_text(build.stdout+build.stderr,encoding="utf-8")
assert build.returncode==0,build.stdout+build.stderr
run=subprocess.run([str(exe)],capture_output=True,text=True)
(AUDIT/"ATLAS61_FIXTURE.txt").write_text(run.stdout+run.stderr,encoding="utf-8")
assert run.returncode==0,run.stdout+run.stderr
checks=int(re.search(r"checks=(\d+)",run.stdout).group(1))
report=dict(status="PASS_OFFLINE_ATLAS61_PRODUCTION_METHOD_FIXTURE",checks=checks,productionMapAndNeedsBodiesExtractedVerbatim=True,
            realProductionHelperAndIdentitySource=True,controlledNguiSpriteLookupCounters=True,
            hotMapCalls=8000,hotGetSpriteCalls=0,lateRegistrationInvalidation=True,nativeSpriteListGrowthAndReplacementInvalidation=True,
            noNegativeCache=True,newTextureBehavior="Builders replaced with counted append-only test doubles; actual Unity texture creation not exercised",
            nativeGraphicsAndGameplayAcceptance="NOT_RUN",gameStarted=False,
            sourceHashes={name:sha(ROOT/"src"/name) for name in FILES},
            extractedBodySha256={name:hashlib.sha256(body.encode()).hexdigest() for name,body in replacements.items()},
            fixtureSha256=sha(fixture))
runtime=ROOT/"out/EndfieldDoK.MapUnitLab.dll"
if runtime.is_file():
    report["runtimeSha256"]=sha(runtime)
    report["runtimeBindingMeaning"]="Source fixture hash-bound to the strictly compiled candidate; Unity runtime graphics not executed"
(AUDIT/"ATLAS61_FINAL_BINDING.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
print(run.stdout.strip())
