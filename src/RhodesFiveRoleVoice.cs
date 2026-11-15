using System;
using System.Collections.Generic;
using System.IO;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using Fabric;
using Fabric.Core.Code.Utils;
using UnityEngine;
using BBI.Core.Events;
using EndfieldDoK.MapUnitLab.ModCore;

namespace EndfieldDoK.MapUnitLab
{
    // Fabric requests a clip at playback and releases it after the line stops.
    // The native VO controller retains its queues, priorities and mixer routing.
    internal sealed class RhodesFiveRoleVoice:IAudioClipLoader
    {
        sealed class CachedClip
        {
            internal AudioClip Clip;
            internal int References, LastUse;
        }

        const int MaximumCachedClips=64,MaximumWaveBytes=1048576;
        static RhodesFiveRoleVoice Current;
        readonly IAudioClipLoader Native;
        readonly object Loader;
        readonly Dictionary<string,CachedClip> Clips=new Dictionary<string,CachedClip>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> ReportedPrivateClips=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly RhodesVoiceDiagnostics51 Diagnostics=new RhodesVoiceDiagnostics51();
        readonly Dictionary<string,List<RhodesVoiceRoute>> WarmPools=new Dictionary<string,List<RhodesVoiceRoute>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,string> WavePaths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        readonly BBI.Core.Events.EventHandler<ResearchEvent> WarmResearchHandler;
        BoundedVoicePcmPrewarm48<HumanoidAttackAudio.DecodedWave> Warmup;
        IEventReceiver WarmResearchReceiver;
        RhodesVoiceCatalog Catalog;
        string Folder;
        int UseClock,Reports,ArtilleryUnitReports,ArtilleryProductionReports,ArtilleryCombatReports;
        bool CatalogAttempted,FaultReported,EventsRegistered;

        RhodesFiveRoleVoice(object loader,IAudioClipLoader native)
        {Loader=loader;Native=native;WarmResearchHandler=OnResearchWarmup;}

        internal static void Bind(object loader)
        {
            IAudioClipLoader native=loader as IAudioClipLoader;
            if(native==null)return;
            if(Current!=null&&ReferenceEquals(Current.Loader,loader))return;
            Reset();Current=new RhodesFiveRoleVoice(loader,native);
            DialogAudioComponent.SetClipLoadCallback(Current);
        }

        internal static bool LocalScope()
        {CommanderID local;return TryLocalCommander(out local);}

        static bool TryLocalCommander(out CommanderID local)
        {
            local=CommanderID.None;
            if(!Runtime.RhodesCarrier||Runtime.SkirmishMode&&!RhodesSkirmishFaction.IsMatchActive)return false;
            object voice=ShipbreakersMain.VOController;
            ICommanderManager commanders=voice==null?null:NativeRead.Get(voice,"mCommanderManager") as ICommanderManager;
            if(commanders==null)return false;
            local=commanders.LocalCommanderID;return RhodesResearch.IsDeclaredOwner(local);
        }

        bool LoadCatalog()
        {
            if(CatalogAttempted)return Catalog!=null;
            CatalogAttempted=true;
            try
            {
                Folder=Path.Combine(Runtime.LabDirectory,"assets/map_unit_lab/rhodes_voice_v01");
                string path=Path.Combine(Folder,"routes.tsv");
                if(!File.Exists(path)||new FileInfo(path).Length>1048576)throw new InvalidDataException("Rhodes voice route table is missing or too large");
                string[] lines=File.ReadAllLines(path);Catalog=RhodesVoiceCatalog.Parse(lines);
                PrepareWarmup(lines);
                Runtime.Log("RHODES_FIVE_ROLE_VOICE_READY routes="+Catalog.Count+"; customRoutes="+Catalog.PrivateRoutes.Count+
                    "; nativeFabricLoader=True; cacheLimit="+MaximumCachedClips+"; lazyPCM=True; actualAudioAcceptancePending=True");
                return true;
            }
            catch(Exception error){Warning(error);return false;}
        }

        internal static bool Prepare(VoiceOverEvent original,bool direct,out VoiceOverEvent result)
        {
            result=original;
            CommanderID local;
            if(original==null||Current==null)return false;
            bool localScope=TryLocalCommander(out local);
            Current.TraceChatterRequest(original,direct,localScope);
            if(!localScope||!Current.LoadCatalog())return false;
            Current.BindResearchWarmup();
            try
            {
                if(!RhodesVoiceFabricEvents.Ensure(Current.Loader))return false;
                if(!Current.EventsRegistered)
                {
                    foreach(RhodesVoiceRoute route in Current.Catalog.PrivateRoutes)
                    {
                        // Form events use the native controller's real intensity
                        // family. Do not pre-register an unused fixed-cal leaf.
                        if(route.EventKey.StartsWith(RhodesCarrierFormVoice.Prefix,StringComparison.Ordinal))continue;
                        RhodesVoiceFabricEvents.Register(route.EventKey,route.Role);
                    }
                    Current.EventsRegistered=true;
                }
                string custom=RhodesUnitVoiceAliases.ProductionEventKey(original);
                if(custom==null&&original.Action=="rsh"&&original.SecondarySummary!=null&&original.SecondarySummary.StartsWith("RI_",StringComparison.Ordinal))
                    custom=original.SecondarySummary;
                if(custom==null&&original.Action!=null&&original.Action.StartsWith("RI_",StringComparison.Ordinal))custom=original.Action;
                RhodesVoiceRoute selected;
                if(custom!=null&&Current.Catalog.TryGet(custom,out selected))
                {
                    Current.WarmRelated(selected);
                    result=RhodesVoiceFabricEvents.Route(original,selected.EventKey,selected.Role);
                    Current.TraceArtillery(original,result,selected.EventKey);
                    return result!=null;
                }
                string unit=RhodesUnitVoiceAliases.UnitTypeForToken(original.SecondaryDetail);
                if(unit==null)unit=RhodesUnitVoiceAliases.UnitTypeForToken(original.PrimaryDetail);
                // Only eligible combat broadcasts advance the local rotation.
                selected=string.Equals(original.PrimarySummary,"opscombat",StringComparison.OrdinalIgnoreCase)?
                    Current.Catalog.UnitEvent(unit,original.Action):null;
                if(selected!=null)
                {
                    Current.WarmRelated(selected);
                    result=RhodesVoiceFabricEvents.Route(original,selected.EventKey,selected.Role);
                    Current.TraceArtillery(original,result,selected.EventKey);
                    return result!=null;
                }
                if(!direct)Current.TraceArtillery(original,null,"fallbackCandidate_armored_mib");
                // Must precede the coalition carrier rewrite below. Only this
                // native event's exact registered local carrier can select a pool.
                string form;
                if(RhodesCarrierFormVoice.TryGet(original,direct,local,out form))
                {
                    string[] intensities=RhodesVoiceFabricEvents.NativeIntensityFamily(ShipbreakersMain.VOController);
                    if(RhodesVoiceFabricEvents.SupportsNativeIntensity(original.Intensity,intensities))
                    {
                        selected=Current.Catalog.CarrierFormEvent(form,original.Action);
                        if(selected!=null)
                        {
                            Current.WarmRelated(selected);
                            VoiceOverEvent formed=RhodesVoiceFabricEvents.RoutePreservingIntensity(original,selected.EventKey,selected.Role,intensities);
                            if(formed!=null){result=formed;return true;}
                        }
                    }
                }
                // The five-role package supplies the coalition carrier hierarchy.
                if(!direct&&string.Equals(original.PrimarySummary,"carrier",StringComparison.OrdinalIgnoreCase))
                {
                    result=new VoiceOverEvent(original.Priority,original.Action,original.Group,"coalition",
                        original.PrimarySummary,"car",original.SecondarySummary,original.SecondaryDetail,
                        original.TertiarySummary,original.TertiaryDetail,original.QuaternarySummary,original.QuaternaryDetail,
                        original.PositionalEntity,original.Intensity);
                    return true;
                }
                return false;
            }
            catch(Exception error){Current.Warning(error);return false;}
        }

        public bool TryLoadClip(string path,out AudioClip clip)
        {
            clip=null;
            string key=null;
            bool localScope=false,catalogReady=false,routeMatched=false;
            string diagnosticError=null;
            try
            {
                RhodesVoiceRoute route;
                key=RhodesVoiceCatalog.ClipKey(path);
                localScope=LocalScope();catalogReady=localScope&&LoadCatalog();
                if(catalogReady&&Catalog.TryLoadRoute(key,out route))
                {
                    routeMatched=true;
                    CachedClip cached;
                    bool cacheHit=Clips.TryGetValue(route.Clip,out cached);
                    if(!cacheHit)
                    {
                        TrimCache();
                        string fullPath=WavePath(route);
                        HumanoidAttackAudio.DecodedWave wave;
                        if(Warmup==null||!Warmup.TryTakeOrCancel(fullPath,out wave))
                            wave=DecodeWave(fullPath);
                        AudioClip created=AudioClip.Create("EF_RhodesVoice_"+route.Job,wave.Samples.Length,1,wave.SampleRate,false);
                        if(created==null)throw new InvalidOperationException("Unity could not create Rhodes voice clip");
                        if(!created.SetData(wave.Samples,0)){UnityEngine.Object.Destroy(created);throw new InvalidOperationException("Unity rejected Rhodes voice samples");}
                        cached=new CachedClip{Clip=created};Clips.Add(route.Clip,cached);
                    }
                    cached.References++;cached.LastUse=++UseClock;clip=cached.Clip;
                    WarmRelated(route);
                    bool privateClip=key.StartsWith("RI_",StringComparison.OrdinalIgnoreCase)||key.StartsWith("rhodes.",StringComparison.OrdinalIgnoreCase);
                    if(privateClip?ReportedPrivateClips.Count<64&&ReportedPrivateClips.Add(key):Reports<20)
                    {
                        if(!privateClip)Reports++;
                        Runtime.Log("RHODES_FIVE_ROLE_VOICE_CLIP key="+key+"; role="+route.Role+"; nativeMixer=True; nativeQueue=True; privateEvent="+privateClip);
                    }
                    TraceRoleLoad(key,localScope,catalogReady,routeMatched,true,cacheHit,false,null);
                    return true;
                }
            }
            catch(Exception error){diagnosticError=error.GetType().Name;Warning(error);}
            TraceRoleLoad(key,localScope,catalogReady,routeMatched,false,false,true,diagnosticError);
            return Native.TryLoadClip(path,out clip);
        }

        void TraceChatterRequest(VoiceOverEvent original,bool direct,bool localScope)
        {
            try{
                string message=Diagnostics.ChatterRequest(original.Action,original.PrimarySummary,original.PrimaryDetail,direct,localScope);
                if(message!=null)Runtime.Log(message);
            }catch{ } // Optional observation must never alter native VO behavior.
        }

        void TraceRoleLoad(string key,bool localScope,bool catalogReady,bool routeMatched,bool ready,bool cacheHit,bool fallback,string error)
        {
            try{
                string message=Diagnostics.RoleLoad(key,localScope,catalogReady,routeMatched,ready,cacheHit,fallback,error);
                if(message!=null)Runtime.Log(message);
            }catch{ } // A loaded clip is not an observed native playback notification.
        }

        // Independent from the opening chatter limit; only runs for a native
        // event bearing U09's exact private identity, never on a timer.
        void TraceArtillery(VoiceOverEvent original,VoiceOverEvent result,string key)
        {
            if(original.PrimaryDetail!=RhodesUnitVoiceAliases.ArtilleryVoiceToken&&
                original.SecondaryDetail!=RhodesUnitVoiceAliases.ArtilleryVoiceToken)return;
            if(original.Action=="prc"){if(ArtilleryProductionReports>=8)return;ArtilleryProductionReports++;}
            else if(original.Action=="unf"||original.Action=="des"){if(ArtilleryCombatReports>=8)return;ArtilleryCombatReports++;}
            else{if(ArtilleryUnitReports>=8)return;ArtilleryUnitReports++;}
            Runtime.Log("RHODES_U09_VOICE_EVENT action="+original.Action+"; speaker="+original.PrimarySummary+
                "; route="+key+"; privateEventReady="+(result!=null)+"; nativeQueueRetained=True; audibleAcceptancePending=True");
        }

        void TrimCache()
        {
            if(Clips.Count<MaximumCachedClips)return;
            string oldest=null;int clock=int.MaxValue;
            foreach(KeyValuePair<string,CachedClip> item in Clips)
                if(item.Value.References==0&&item.Value.LastUse<clock){oldest=item.Key;clock=item.Value.LastUse;}
            if(oldest==null)throw new InvalidOperationException("All Rhodes voice cache entries are in use");
            UnityEngine.Object.Destroy(Clips[oldest].Clip);Clips.Remove(oldest);
        }

        public void UnloadClip(AudioClip clip)
        {
            foreach(CachedClip cached in Clips.Values)
                if(ReferenceEquals(cached.Clip,clip)){if(cached.References>0)cached.References--;return;}
            Native.UnloadClip(clip);
        }

        static HumanoidAttackAudio.DecodedWave DecodeWave(string path)
        {return HumanoidAttackAudio.DecodePcm16Wave(HumanoidAttackAudio.ReadBoundedFile(path,MaximumWaveBytes));}
        static long DecodedBytes(HumanoidAttackAudio.DecodedWave wave){return checked(wave.Samples.LongLength*4L);}
        string WavePath(RhodesVoiceRoute route)
        {string path;return WavePaths.TryGetValue(route.Clip,out path)?path:Path.Combine(Folder,route.Clip.Replace('/',Path.DirectorySeparatorChar));}
        // Group only genuine alternatives; no full-catalog background sweep.
        internal static string WarmPoolKey(RhodesVoiceRoute route)
        {
            string key=route.EventKey;
            if(key.StartsWith(RhodesCarrierFormVoice.Prefix,StringComparison.Ordinal)){
                int dot=key.LastIndexOf('.');return dot>0?key.Substring(0,dot):null;
            }
            if(!key.StartsWith("vo_",StringComparison.OrdinalIgnoreCase)&&!key.StartsWith("rhodes.opscombat.vo_",StringComparison.OrdinalIgnoreCase))return null;
            int last=key.LastIndexOf('_');return last>0?route.Role+"|"+route.UnitType+"|"+key.Substring(0,last):null;
        }
        void PrepareWarmup(string[] lines)
        {
            try{
                Warmup=new BoundedVoicePcmPrewarm48<HumanoidAttackAudio.DecodedWave>(DecodeWave,DecodedBytes);
                for(int i=1;i<lines.Length;i++){
                    int tab=lines[i].IndexOf('\t');if(tab<=0)continue;RhodesVoiceRoute route;
                    if(!Catalog.TryGet(lines[i].Substring(0,tab),out route))continue;
                    if(!WavePaths.ContainsKey(route.Clip))WavePaths.Add(route.Clip,Path.Combine(Folder,route.Clip.Replace('/',Path.DirectorySeparatorChar)));
                    string key=WarmPoolKey(route);if(key==null)continue;List<RhodesVoiceRoute> pool;
                    if(!WarmPools.TryGetValue(key,out pool)){pool=new List<RhodesVoiceRoute>();WarmPools.Add(key,pool);}
                    bool duplicate=false;foreach(RhodesVoiceRoute prior in pool)if(prior.Clip==route.Clip){duplicate=true;break;}
                    if(!duplicate)pool.Add(route);
                }
            }catch{if(Warmup!=null)Warmup.Dispose();Warmup=null;WarmPools.Clear();WavePaths.Clear();}
        }
        void WarmRelated(RhodesVoiceRoute current)
        {
            if(Warmup==null)return;string key=WarmPoolKey(current);List<RhodesVoiceRoute> pool;
            if(key==null||!WarmPools.TryGetValue(key,out pool))return;
            foreach(RhodesVoiceRoute route in pool)
                if(route.Clip!=current.Clip&&!Clips.ContainsKey(route.Clip))Warmup.Request(WavePath(route));
        }
        void BindResearchWarmup()
        {
            if(Warmup==null)return;
            try{
                IEventReceiver receiver=ShipbreakersMain.SimToPresentationEventSystem;
                if(ReferenceEquals(receiver,WarmResearchReceiver))return;
                if(WarmResearchReceiver!=null){WarmResearchReceiver.RemoveHandler<ResearchEvent>(WarmResearchHandler);WarmResearchReceiver=null;}
                WarmResearchReceiver=receiver;if(receiver!=null)receiver.AddHandler<ResearchEvent>(WarmResearchHandler);
            }catch{ } // Retain a possibly partially bound receiver so Reset can retry removal.
        }
        void OnResearchWarmup(ResearchEvent research)
        {
            try{
                CommanderID local;
                if(Warmup==null||research==null||research.ResearchItem==null||research.Reason!=ResearchEvent.EventReason.Started||
                    !TryLocalCommander(out local)||!local.Equals(research.Commander))return;
                string key=RhodesResearch.ResearchVoiceCode(research.ResearchItem.Name);RhodesVoiceRoute route;
                if(!string.IsNullOrEmpty(key)&&Catalog.TryGet(key,out route)&&!Clips.ContainsKey(route.Clip))Warmup.Request(WavePath(route));
            }catch{ } // Optional managed prewarm cannot alter research or VO events.
        }
        void ReleaseWarmup()
        {
            if(WarmResearchReceiver!=null){try{WarmResearchReceiver.RemoveHandler<ResearchEvent>(WarmResearchHandler);}catch{}WarmResearchReceiver=null;}
            if(Warmup!=null){Warmup.Dispose();Warmup=null;}WarmPools.Clear();WavePaths.Clear();
        }

        void Warning(Exception error)
        {
            if(FaultReported)return;FaultReported=true;
            Runtime.Log("RHODES_FIVE_ROLE_VOICE_WARNING "+error.GetType().Name+": "+error.Message);
        }

        internal static void Reset()
        {
            RhodesVoiceFabricEvents.Reset();
            if(Current==null)return;
            Current.ReleaseWarmup();
            DialogAudioComponent.SetClipLoadCallback(Current.Native);
            foreach(CachedClip clip in Current.Clips.Values)if(clip.Clip!=null)UnityEngine.Object.Destroy(clip.Clip);
            Current.Clips.Clear();Current=null;
        }
    }
}
