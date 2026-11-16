using System;
using System.Collections.Generic;
using System.Reflection;
using EndfieldDoK.MapUnitLab;

namespace UnityEngine
{
    public static class Time { public static float realtimeSinceStartup,unscaledDeltaTime; }
    public static class Application { public static int targetFrameRate=72; }
    public static class QualitySettings { public static int vSyncCount=2; }
}
namespace EndfieldDoK.MapUnitLab
{
    public static class Runtime
    {
        public static bool FrameTimingEnabled;
        public static readonly List<string> Logs=new List<string>();
        public static void Log(string value){Logs.Add(value);}
    }
    public static class UnitViewDiscovery { public static int Count {get{return 42;}} }
}
internal static class FrameTiming60Fixture
{
    static int Checks;
    static void Check(bool value,string reason)
    {Checks++;if(!value)throw new InvalidOperationException(reason);}
    static void Frame(float time)
    {UnityEngine.Time.realtimeSinceStartup=time;UnityEngine.Time.unscaledDeltaTime=.01f;RhodesSkirmishFrameTiming.Frame();}
    static object Field(string name)
    {return typeof(RhodesSkirmishFrameTiming).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);}
    static string Last {get{return Runtime.Logs[Runtime.Logs.Count-1];}}
    static void Main()
    {
        Frame(0);Frame(100);
        Check(Runtime.Logs.Count==0&&RhodesSkirmishFrameTiming.Begin()==0,"default-disabled timing performs no logging or stopwatch work");
        Runtime.FrameTimingEnabled=true;RhodesSkirmishFrameTiming.Reset();Frame(0);Frame(9.99f);
        Check(Runtime.Logs.Count==0,"first window starts correctly at time zero and does not report early");
        Frame(10);
        Check(Runtime.Logs.Count==1&&Last.Contains("elapsedSeconds=10; windowSeconds=10; targetWindowSeconds=10"),"first 10s window and elapsed time are exact");
        Check(Last.Contains("trackedUnitViews=42")&&Last.Contains("notActiveOrVisibleCount"),"cached lifecycle count has explicit scope");
        Check(Last.Contains("vSyncCount=2; targetFrameRate=72"),"existing frame cap settings are observed");
        Check(Last.Contains("frames=3;"),"time-zero frame is included exactly once");
        object histogram=Field("Window"),ticks=Field("Ticks"),worst=Field("Worst"),calls=Field("Calls");
        Check(((long[])ticks).Length==11&&((int[])calls).Length==11,"rig, track and selection presentation sections have fixed slots");
        for(int window=2;window<=60;window++)
        {
            long start=RhodesSkirmishFrameTiming.Begin();RhodesSkirmishFrameTiming.End(6,start);
            start=RhodesSkirmishFrameTiming.Begin();RhodesSkirmishFrameTiming.End(7,start);
            start=RhodesSkirmishFrameTiming.Begin();RhodesSkirmishFrameTiming.End(8,start);
            start=RhodesSkirmishFrameTiming.Begin();RhodesSkirmishFrameTiming.End(9,start);
            start=RhodesSkirmishFrameTiming.Begin();RhodesSkirmishFrameTiming.End(10,start);
            Frame(window*10);
            Check(Runtime.Logs.Count==window&&Last.Contains("targetWindowSeconds=10"),"initial window remains detailed: "+window);
        }
        Check(Last.Contains("factionRig=")&&Last.Contains("tracks=")&&Last.Contains("sectionTimesNotAdditive=True"),"nested measurements are identified and not presented as additive cost");
        Check(Last.Contains("hudIcons=")&&Last.Contains("atlas=")&&Last.Contains("humanoidSelection="),"selection-related costs are included in rolling diagnostics");
        Check(Last.Contains("presentationSectionTotalMsPerFrame=")&&Last.Contains("presentationSectionCallsPerFrame=")&&Last.Contains("factionRig=1,tracks=1"),"per-unit sections also expose total frame cost and calls per frame");
        Frame(659.99f);Check(Runtime.Logs.Count==60,"after initial ten minutes a full minute is accumulated");
        Frame(660);Check(Runtime.Logs.Count==61&&Last.Contains("elapsedSeconds=660; windowSeconds=60; targetWindowSeconds=60")&&Last.Contains("frames=2;"),"late-match minute reporting continues with a fresh histogram");
        for(int window=1;window<=100;window++)
        {
            Frame(660+window*60);
            Check(Runtime.Logs.Count==61+window&&Last.Contains("targetWindowSeconds=60"),"late-match diagnostic has no 60-report cutoff: "+window);
            Check(ReferenceEquals(histogram,Field("Window"))&&ReferenceEquals(ticks,Field("Ticks"))&&ReferenceEquals(worst,Field("Worst"))&&ReferenceEquals(calls,Field("Calls")),"storage reused across all windows");
        }
        int previous=Runtime.Logs.Count;
        Frame(6900);Check(Runtime.Logs.Count==previous+1&&Last.Contains("windowSeconds=240; targetWindowSeconds=60"),"large scheduling gaps report actual duration without catch-up log spam");
        Check(UnityEngine.Application.targetFrameRate==72&&UnityEngine.QualitySettings.vSyncCount==2,"quality and frame cap settings remain unchanged");
        RhodesSkirmishFrameTiming.Reset();Frame(8000);Check(Runtime.Logs.Count==previous+1,"match reset restarts first window");
        Frame(8010);Check(Last.Contains("elapsedSeconds=10; windowSeconds=10; targetWindowSeconds=10"),"new match restarts elapsed time and detailed schedule");
        Runtime.FrameTimingEnabled=false;int finalCount=Runtime.Logs.Count;Frame(9000);
        RhodesSkirmishFrameTiming.End(7,1);
        Check(Runtime.Logs.Count==finalCount&&RhodesSkirmishFrameTiming.Begin()==0,"opt-out suppresses timing and logging");
        Console.WriteLine("PASS_FRAME_TIMING60 checks="+Checks+"; initialWindows=60x10s; laterWindows=101x60sPlusActualDelay; fixedStorage=True; settingsUnchanged=True; defaultEnabled=False");
    }
}
