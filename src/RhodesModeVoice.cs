using System;
using System.Collections;
using BBI.Game.Events;
using BBI.Unity.Core.Utility;
using BBI.Unity.Game.World;

namespace EndfieldDoK.MapUnitLab
{
    // Event-only native voice routing. No Update, command/sim mutation,
    // custom playback, volume changes or global faction replacement.
    public static class RhodesModeVoice
    {
        const string Bank="vo/soban";
        const string Hierarchy="assets/content/audio/tempspeechhierarchy/sobanspeechhierarchy.prefab";
        const string ReadyClip="assets/content/audio/bundled/audio/vo/english/soban/carrier/vo_soban_carrier_sel_cargengengen_t1_12480";
        static WeakReference Loader;
        static bool Warned,ReadyReported;
        static int Reports,WaitReports;
        static string LastWaitReason;
        static bool EventReported;

        // Prefix of the native StreamableAssetContainer overload. Keep the
        // original load intact and request the extra native hierarchy only once.
        public static void BeforeBankLoad(object loader,object rawContainer)
        {
            try{RhodesFiveRoleVoice.Bind(loader);}catch(Exception error){Warning(error);}
            if(Runtime.SkirmishMode && !RhodesSkirmishFaction.IsMatchActive)return;
            if(!Runtime.RhodesCarrier || loader==null)return;
            StreamableAssetContainer container=rawContainer as StreamableAssetContainer;
            if(container==null)return;
            string bank=container.AssetBundleName;
            if(bank!="vo/coalition" && bank!=Bank)return;
            if(Loader!=null && object.ReferenceEquals(Loader.Target,loader))return;
            Loader=new WeakReference(loader);ReadyReported=false;Warned=false;Reports=0;WaitReports=0;LastWaitReason=null;EventReported=false;
            try
            {
                if(bank==Bank)return; // Native Soban load is already in flight.
                StreamableAssetContainer extra=new StreamableAssetContainer();
                NativeRead.Set(extra,"m_AssetBundleToLoad",Bank);
                NativeRead.Set(extra,"m_PathToAssetInBundle",Hierarchy);
                if(!extra.IsValid())throw new InvalidOperationException("Native Soban voice bundle is unavailable");
                // This re-enters the same prefix; the exact-loader guard above
                // makes that a no-op. Native promises/coroutines own the load.
                NativeRead.Call(loader,"LoadAssetBundle",extra);
                Runtime.Log("RHODES_MODE_VOICE_BANK_REQUESTED bank="+Bank+
                    "; nativeHierarchyAndLoader=True; oncePerLoader=True; sceneScan=False; recurringPoll=False");
            }
            catch(Exception error){Warning(error);}
        }

        static bool BankReady()
        {
            object loader=Loader==null?null:Loader.Target;if(loader==null){Wait("native_loader_missing");return false;}
            // The native coroutine decrements this only AFTER clips, Fabric
            // hierarchy and mixer registration. Pending loads retain old VO.
            int pending=Convert.ToInt32(NativeRead.Get(loader,"mBundlesToLoad"));
            if(pending!=0){Wait("native_pending_loads="+pending);return false;}
            IDictionary clips=NativeRead.Get(loader,"mClipDictionary") as IDictionary;
            if(clips==null || !clips.Contains(ReadyClip) || clips[ReadyClip]==null)
            {Wait("native_extensionless_clip_missing");return false;}
            if(!ReadyReported)
            {
                ReadyReported=true;Runtime.Log("RHODES_MODE_VOICE_BANK_READY bank="+Bank+
                    "; nativePendingLoads=0; carrierRecordingPresent=True; nativeQueueMixerCooldownRetained=True");
            }
            return true;
        }

        // Prefix of ExecuteVO(event,direct): clone ONLY the exact registered
        // Rhodes carrier's event at playback. Events lacking an exact unit
        // identity, fleet/mission broadcasts and direct audio stay native.
        public static object PrepareEvent(object rawEvent,bool direct)
        {
            VoiceOverEvent ev=rawEvent as VoiceOverEvent;
            if(ev==null)return rawEvent;
            VoiceOverEvent replacement;
            if(RhodesFiveRoleVoice.Prepare(ev,direct,out replacement))return replacement;
            if(!Runtime.RhodesCarrier || direct)return rawEvent;
            ev=RhodesUnitVoiceAliases.Prepare(ev);rawEvent=ev;
            try
            {
                UnitView view;
                if(!UnitViewDiscovery.TryGet(ev.PositionalEntity,out view) || view==null ||
                    !view.EntityID.Equals(ev.PositionalEntity))return rawEvent;
                RhodesDeploymentSnapshot state;
                if(!RhodesDeploymentState.TryGet(ev.PositionalEntity,view.CommanderID,out state) ||
                    !RhodesVoicePolicy.UseSobanCarrier(state))return rawEvent;
                if(!EventReported)
                {
                    EventReported=true;Runtime.Log("RHODES_MODE_VOICE_EVENT entity="+ev.PositionalEntity+
                        "; action="+ev.Action+"; nativeSummary="+ev.PrimarySummary+"; nativeDetail="+ev.PrimaryDetail+
                        "; nativeFaction="+ev.Faction+"; branch="+state.Branch+"; sourceLevel="+state.SourceLevel);
                }
                if(!string.Equals(ev.PrimarySummary,"carrier",StringComparison.OrdinalIgnoreCase))
                {Wait("non_carrier_native_summary="+ev.PrimarySummary);return rawEvent;}
                if(!BankReady())return rawEvent;
                // The Soban bank's generic carrier speaker is car. Native
                // donor-specific speaker codes must not block its selection.
                VoiceOverEvent routed=new VoiceOverEvent(ev.Priority,ev.Action,ev.Group,"soban",
                    "carrier","car",ev.SecondarySummary,ev.SecondaryDetail,
                    ev.TertiarySummary,ev.TertiaryDetail,ev.QuaternarySummary,ev.QuaternaryDetail,
                    ev.PositionalEntity,ev.Intensity);
                if(Reports<12)
                {
                    Reports++;Runtime.Log("RHODES_MODE_VOICE_ROUTED entity="+ev.PositionalEntity+
                        "; action="+ev.Action+"; faction=soban; branch=2; sourceLevel="+state.SourceLevel+
                        "; originalEventUnchanged=True; nativePlayback=True");
                }
                return routed;
            }
            catch(Exception error){Warning(error);return rawEvent;}
        }

        // Bounded event-time diagnostics, only after exact Rhodes identity
        // and committed gunnery state have passed. Never runs on a timer.
        static void Wait(string reason)
        {
            if(WaitReports>=4 || reason==LastWaitReason)return;
            LastWaitReason=reason;WaitReports++;
            Runtime.Log("RHODES_MODE_VOICE_WAIT reason="+reason+"; originalEventRetained=True; eventOnly=True");
        }

        static void Warning(Exception error)
        {
            if(Warned)return;Warned=true;
            Runtime.Log("WARNING RHODES_MODE_VOICE "+error.GetType().Name+": "+error.Message+
                "; originalVoiceEventRetained=True; nativeCombatUnaffected=True");
        }
        public static void Reset(){RhodesFiveRoleVoice.Reset();Loader=null;Warned=false;ReadyReported=false;Reports=0;WaitReports=0;LastWaitReason=null;EventReported=false;}
    }
}
