using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.Events;
using BBI.Game.Commands;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Audio;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Exact HUD-owner listener substitution, once per session. No scene scan,
    // audio playback, simulation mutation or global faction/VO-field remapping.
    public sealed class RhodesPowerVoice:MonoBehaviour
    {
        static readonly List<RhodesPowerVoice> Known=new List<RhodesPowerVoice>();
        static readonly FieldInfo Listeners=typeof(AsynchronousEventSystem).GetField("mListeners",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo Active=typeof(BlackbirdPanelBase).GetField("mSessionActive",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly MethodInfo NativeHandler=typeof(NotificationController).GetMethod("OnPowerShuntEvent",BindingFlags.Instance|BindingFlags.NonPublic);
        static bool Session,FramesPending;
        static int Reports;
        // ObservePanel may run before native session listeners exist. It only
        // records the owner: the two attempts belong to actual HUD frames.
        internal sealed class BindingRetry
        {
            internal int Attempts;
            uint FirstFrame;
            bool Done;
            internal bool Pending{get{return !Done&&Attempts<2;}}
            internal bool Due(uint frame)
            {
                if(!Pending)return false;
                if(Attempts==1&&unchecked(frame-FirstFrame)<8)return false;
                if(Attempts==0)FirstFrame=frame;Attempts++;return true;
            }
            internal void Succeeded(){Done=true;}
            internal void Reset(){Attempts=0;FirstFrame=0;Done=false;}
        }
        NotificationController Owner;
        IEventReceiver Receiver;
        BBI.Core.Events.EventHandler<PowerShuntEvent> Original,Wrapper;
        readonly BindingRetry Retry=new BindingRetry();

        public static void ObservePanel(object panel)
        {
            NotificationController owner=panel as NotificationController;if(owner==null)return;
            try
            {
                RhodesPowerVoice driver=owner.GetComponent<RhodesPowerVoice>();
                if(driver==null&&Known.Count>=4)return;
                if(driver==null)driver=owner.gameObject.AddComponent<RhodesPowerVoice>();
                driver.Owner=owner;if(!Known.Contains(driver)&&Known.Count<4)Known.Add(driver);
                if(Session&&driver.Retry.Pending)FramesPending=true;
            }
            catch(Exception error){Report("observe",error);}
        }
        public static void BeginSession()
        {
            EndSession();Session=true;Reports=0;
            for(int i=Known.Count-1;i>=0;i--)
            {RhodesPowerVoice driver=Known[i];if(driver==null)Known.RemoveAt(i);else{driver.Retry.Reset();FramesPending=true;}}
        }
        public static void EndSession()
        {
            Session=false;FramesPending=false;
            for(int i=Known.Count-1;i>=0;i--)
            {RhodesPowerVoice driver=Known[i];if(driver==null)Known.RemoveAt(i);else driver.Release(driver.Owner!=null);}
        }
        // Exactly two eligible HUD-frame attempts per observed owner: the first
        // frame and at least8 frames later. Once successful/exhausted the static
        // guard makes following frames idle, with no scene or listener polling.
        public static void FirstFrame(uint frame)
        {
            if(!Session||!FramesPending)return;bool pending=false;
            for(int i=Known.Count-1;i>=0;i--)
            {
                RhodesPowerVoice driver=Known[i];if(driver==null){Known.RemoveAt(i);continue;}
                if(driver.Retry.Due(frame)&&driver.TryInstall())driver.Retry.Succeeded();
                if(driver.Retry.Pending)pending=true;
            }
            FramesPending=pending;
        }
        bool TryInstall()
        {
            if(!Session)return false;if(Receiver!=null)return true;
            if(Owner==null||!IsActive(Owner)||NativeHandler==null||Listeners==null)return false;
            IEventReceiver receiver=ShipbreakersMain.SimToPresentationEventSystem;
            if(receiver==null||receiver.GetType()!=typeof(AsynchronousEventSystem))return false;
            try
            {
                Original=(BBI.Core.Events.EventHandler<PowerShuntEvent>)Delegate.CreateDelegate(typeof(BBI.Core.Events.EventHandler<PowerShuntEvent>),Owner,NativeHandler);
                Wrapper=Handle;
                if(!SwapExact(receiver,Original,Wrapper)){Original=null;Wrapper=null;return false;}
                Receiver=receiver;
                Runtime.Log("RHODES_POWER_VOICE_BOUND exactNotificationDelegate=True; nativeQueue=True; nativeTunablePriority=True; privateManeuverAnnouncements=True; sceneScan=False; actualAudioAcceptancePending=True");
                return true;
            }
            catch(Exception error){ReleaseListener(true);Report("install",error);return false;}
        }
        // AsynchronousEventSystem's native Add/RemoveHandler methods use this
        // exact dictionary. Replace one multicast slot in one assignment,
        // preserving every other listener and its order. Unknown ABI fails shut.
        internal static bool SwapExact(IEventReceiver receiver,Delegate expected,Delegate replacement)
        {
            if(receiver==null||receiver.GetType()!=typeof(AsynchronousEventSystem)||Listeners==null||expected==null)return false;
            IDictionary<Type,Delegate> listeners=Listeners.GetValue(receiver) as IDictionary<Type,Delegate>;
            if(listeners==null)return false;Delegate before;
            if(!listeners.TryGetValue(typeof(PowerShuntEvent),out before)||before==null)return false;
            Delegate[] list=before.GetInvocationList();int matches=0;
            foreach(Delegate item in list){if(item.Equals(expected))matches++;if(replacement!=null&&item.Equals(replacement))return false;}
            if(matches!=1)return false;
            Delegate after=null;
            foreach(Delegate item in list)after=Delegate.Combine(after,item.Equals(expected)?replacement:item);
            // All registration/dispatch happens on the presentation thread.
            // Do not overwrite a list changed since the verified snapshot.
            Delegate current;if(!listeners.TryGetValue(typeof(PowerShuntEvent),out current)||!ReferenceEquals(current,before))return false;
            try
            {
                if(after==null)listeners.Remove(typeof(PowerShuntEvent));else listeners[typeof(PowerShuntEvent)]=after;
                return true;
            }
            catch
            {
                // Assignment is the only mutation. Restore the original list if
                // it committed before an exceptional dictionary implementation.
                if(!listeners.TryGetValue(typeof(PowerShuntEvent),out current)||ReferenceEquals(current,after))listeners[typeof(PowerShuntEvent)]=before;
                throw;
            }
        }
        void Release(bool restore)
        {ReleaseListener(restore);if(Retry!=null)Retry.Reset();}
        void ReleaseListener(bool restore)
        {
            if(Receiver!=null&&Wrapper!=null)
            {
                try{RestoreExact(Receiver,Wrapper,Original,restore);}
                catch(Exception error){Report("release",error);}
            }
            Receiver=null;Original=null;Wrapper=null;
        }
        internal static bool RestoreExact(IEventReceiver receiver,Delegate wrapper,Delegate original,bool restore)
        {
            if(receiver==null||receiver.GetType()!=typeof(AsynchronousEventSystem)||Listeners==null||wrapper==null)return false;
            IDictionary<Type,Delegate> listeners=Listeners.GetValue(receiver) as IDictionary<Type,Delegate>;Delegate current;
            if(listeners==null)return false;
            if(!listeners.TryGetValue(typeof(PowerShuntEvent),out current)||current==null)return true;
            int wrappers=0,natives=0;foreach(Delegate item in current.GetInvocationList())
            {if(item.Equals(wrapper))wrappers++;if(original!=null&&item.Equals(original))natives++;}
            if(wrappers==0)return true;if(wrappers!=1)return false;
            return SwapExact(receiver,wrapper,restore&&natives==0?original:null);
        }
        void OnDestroy(){Release(false);Known.Remove(this);Owner=null;}
        static void Report(string stage,Exception error)
        {if(Reports++<3)Runtime.Log("WARNING RHODES_POWER_VOICE_"+stage+" "+error.GetType().Name+": "+error.Message+"; unknownScopeUsesOriginal=True");}

        static bool IsActive(NotificationController owner)
        {return Active!=null&&(bool)Active.GetValue(owner);}
        internal static bool Scope(bool match,bool localOwner,string unitType,bool privateSystem,int system)
        {return match&&localOwner&&unitType==RhodesCarrierUnits.UnitKey&&system>=0&&system<RhodesPowerProfile.SystemCount&&privateSystem;}
        internal static PowerSystemAttributes RootSystem(PowerSystemAttributes value)
        {
            for(int depth=0;depth<8&&value!=null;depth++)
            {
                if(value.GetType().FullName!="BBI.Game.Simulation.BuffedPowerSystemAttributes")return value;
                PowerSystemAttributes next=NativeRead.Get(value,"Base") as PowerSystemAttributes;
                if(next==null||ReferenceEquals(next,value))return null;value=next;
            }
            return null;
        }
        internal static bool SystemIdentity(PowerSystemAttributes value,PowerShuntAttributes attributes,int system)
        {
            if(value==null||attributes==null||system<0||system>=RhodesPowerProfile.SystemCount||(int)value.PowerSystemType!=system)return false;
            PowerSystemAttributes[] all=attributes.PowerSystems;if(all==null||all.Length!=RhodesPowerProfile.SystemCount)return false;
            PowerSystemAttributes root=RootSystem(value);
            return root!=null&&ReferenceEquals(RootSystem(all[system]),root);
        }
        static bool PrivateSystem(PowerSystemAttributes value,int system)
        {
            if(SystemIdentity(value,RhodesCarrierUnits.PreparedPowerAttributes,system))return true;
            PowerShuntAttributes[] all=RhodesCarrierUnits.RegisteredPowerAttributes;
            if(all!=null)foreach(PowerShuntAttributes attributes in all)if(SystemIdentity(value,attributes,system))return true;
            return false;
        }
        bool OwnEvent(PowerShuntEvent value,out int system)
        {
            system=-1;if(value==null||value.PowerSystemAttributes==null||Owner==null||!IsActive(Owner))return false;
            system=(int)value.PowerSystemAttributes.PowerSystemType;
            if(system<0||system>=RhodesPowerProfile.SystemCount)return false;
            SimStateFrame frame=ShipbreakersMain.CurrentSimFrame;if(frame==null)return false;
            UnitState unit=frame.FindObject<UnitState>(value.Entity);if(unit==null)return false;
            ICommanderInteractionProvider interaction=NativeRead.Get(Owner,"mInteractionProvider") as ICommanderInteractionProvider;
            return Scope(Runtime.SkirmishMode&&RhodesSkirmishFaction.IsMatchActive,
                interaction!=null&&RhodesResearch.IsDeclaredOwner(unit.OwnerCommander)&&interaction.LocalCommanderCanControl(unit.OwnerCommander),
                unit.TypeID,PrivateSystem(value.PowerSystemAttributes,system),system);
        }
        // Weapons and armor use native power lines. Maneuvering uses its
        // dedicated Rhodes recordings for online, offline and maximum power.
        internal static string Route(int system,int action,int level,int max)
        {
            if(system==2)
            {
                if(action==1&&level==0)return "RI_MANEUVER_POWER_OFFLINE";
                if(level<=0||level>RhodesPowerProfile.MaximumLevel||max<level||max>RhodesPowerProfile.MaximumLevel)return null;
                if(action==0)return "RI_MANEUVER_POWER_ONLINE";
                if(action==4&&level==max)return "RI_MANEUVER_POWER_FULL";
                return null;
            }
            if(level<=0||level>RhodesPowerProfile.MaximumLevel||max<level||max>RhodesPowerProfile.MaximumLevel||action!=0&&action!=4)return null;
            if(action==4&&level!=max)return null;
            if(system==0)return action==4?"ps2/ful":"ps2/onl";
            if(system==1)return action==4?"ps3/ful":"ps3/onl";
            return null;
        }
        internal static VoiceOverEvent BuildVoice(int priority,string command,DialogueGroup group,string ops,string calm,string route)
        {
            if(route!=null&&route.StartsWith("RI_",StringComparison.Ordinal))
                return new VoiceOverEvent(priority,route,group,"coalition",ops,null,null,null,calm);
            if(route==null)return null;string[] parts=route.Split('/');if(parts.Length!=2)return null;
            return new VoiceOverEvent(priority,command,group,"coalition",ops,null,parts[0],null,parts[1],null,calm);
        }
        bool HandleOwn(PowerShuntEvent value)
        {
            int action=(int)value.Action;if(action<0||action>4)return false;
            int system;if(!Session||!OwnEvent(value,out system))return false;
            string route=Route(system,action,value.CurrentPowerLevelInEffectIndex,value.MaxPowerLevelIndex);
            if(route==null)return true;
            VOController voice=ShipbreakersMain.VOController;IEventSystem queue=ShipbreakersMain.PresentationEventSystem;
            if(voice==null||queue==null)return true;
            DialogueTunable tunable=voice.Commands.Shunt;
            VoiceOverEvent routed=BuildVoice(tunable.Priority,tunable.Command,tunable.Group,voice.Voices.OpsFleet,voice.Fields.Intensity.Calm,route);
            if(routed!=null)queue.Post(routed);return true;
        }
        void Handle(PowerShuntEvent value)
        {
            bool handled=false;
            try{handled=HandleOwn(value);}catch(Exception error){Report("route",error);}
            if(!handled&&Original!=null)Original(value);
        }
    }
}
