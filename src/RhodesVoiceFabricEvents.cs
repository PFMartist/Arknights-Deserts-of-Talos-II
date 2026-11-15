using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Game.Events;
using Fabric;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Private one-clip events use Fabric's own update tree and VO notifications.
    // The separate group keeps them outside native random-selection branches.
    internal static class RhodesVoiceFabricEvents
    {
        internal sealed class Registration
        {
            internal string EventName,Role;
            internal DialogAudioComponent Component;
        }

        static readonly string[] Roles={"carrier","opsfleet","opscombat","engineering","sensors"};
        static readonly Dictionary<string,DialogAudioComponent> Donors=new Dictionary<string,DialogAudioComponent>(StringComparer.Ordinal);
        static readonly Dictionary<string,GroupComponent> Groups=new Dictionary<string,GroupComponent>(StringComparer.Ordinal);
        static readonly Dictionary<string,GroupComponent> RoleGroups=new Dictionary<string,GroupComponent>(StringComparer.Ordinal);
        static readonly Dictionary<string,Registration> Events=new Dictionary<string,Registration>(StringComparer.OrdinalIgnoreCase);
        static WeakReference Loader,FailedLoader;
        static EventManager Manager;
        static IDictionary ListenerBindings;
        static bool CapacityReported;
        static int Warnings;

        internal static bool Ensure(object loader)
        {
            if(loader==null)return false;
            if(Loader!=null&&ReferenceEquals(Loader.Target,loader)&&RoleGroups.Count==Roles.Length)return true;
            if(FailedLoader!=null&&ReferenceEquals(FailedLoader.Target,loader))return false;
            if(Convert.ToInt32(NativeRead.Get(loader,"mBundlesToLoad"))!=0)return false;
            Reset();
            try
            {
                IEnumerable hierarchies=NativeRead.Get(loader,"mInstancedEventHierarchies") as IEnumerable;
                FabricManager fabric=FabricManager.Instance;
                Manager=EventManager.Instance;
                if(hierarchies==null||fabric==null||Manager==null)return false;
                ListenerBindings=NativeRead.Get(Manager,"_listeners") as IDictionary;
                if(ListenerBindings==null)
                    throw new InvalidOperationException("Native Fabric listener bindings are unavailable");
                foreach(object value in hierarchies)
                {
                    GameObject hierarchy=value as GameObject;
                    if(hierarchy==null)continue;
                    foreach(DialogAudioComponent component in hierarchy.GetComponentsInChildren<DialogAudioComponent>(true))
                    {
                        if(component==null||component.IsInstance)continue;
                        string reference=(component.AudioClipReference??string.Empty).Replace('\\','/').ToLowerInvariant();
                        foreach(string role in Roles)
                        {
                            if(reference.IndexOf("vo_coalition_"+role+"_",StringComparison.Ordinal)<0)continue;
                            if(Donors.ContainsKey(role))continue;
                            GroupComponent sourceGroup=FindSpeechRoot(component.transform);
                            if(sourceGroup!=null&&sourceGroup._targetGroupComponentPath==MixerTarget(role))
                                Donors.Add(role,component);
                        }
                    }
                }
                if(Donors.Count!=Roles.Length)
                    throw new InvalidOperationException("Native coalition role donors or mixer target are unavailable");
                foreach(string role in Roles)
                {
                    GroupComponent speechRoot=FindSpeechRoot(Donors[role].transform);
                    if(speechRoot==null||fabric.GetComponentByName(speechRoot._targetGroupComponentPath)==null)
                        throw new InvalidOperationException("Exact native role mixer target is unavailable: "+role);
                    string target=speechRoot._targetGroupComponentPath;
                    GroupComponent root;
                    if(!Groups.TryGetValue(target,out root))
                    {
                        GameObject groupObject=new GameObject("EF_RhodesVoiceEvents_"+Groups.Count);
                        groupObject.SetActive(false);
                        groupObject.hideFlags=HideFlags.DontSave;
                        root=groupObject.AddComponent<GroupComponent>();
                        Groups.Add(target,root);
                        // RegisterGroupComponent supplies native mixer ancestry,
                        // a native update-tree entry and independent instances.
                        root._targetGroupComponentPath=null;
                        root.Guid=Guid.NewGuid().ToString();
                        if(!fabric.RegisterGroupComponent(root,target,false))
                            throw new InvalidOperationException("Native Fabric rejected the private voice group");
                        root._isRegisteredWithMainHierarchy=true;
                        groupObject.SetActive(true);
                    }
                    RoleGroups.Add(role,root);
                }
                Loader=new WeakReference(loader);
                Runtime.Log("RHODES_VOICE_PRIVATE_EVENTS_READY roleDonors="+Donors.Count+"; privateMixerBranches="+Groups.Count+
                    "; independentGroup=True; nativeQueue=True; runtimeAudioAcceptancePending=True");
                return true;
            }
            catch(Exception error)
            {
                Reset();FailedLoader=new WeakReference(loader);Report(error);return false;
            }
        }

        internal static bool Register(string key,string role)
        {return RegisterCore(key,role,"cal",key);}

        static bool RegisterCore(string key,string role,string intensity,string cacheKey)
        {
            string normalized;
            if(!TryRole(role,out normalized)||!ValidKey(key)||Manager==null)return false;
            GroupComponent root;
            if(!RoleGroups.TryGetValue(normalized,out root)||root==null)return false;
            Registration existing;
            if(Events.TryGetValue(cacheKey,out existing))return existing.Role==normalized&&existing.Component!=null;
            if(Events.Count>=256)return false;
            DialogAudioComponent donor;
            if(!Donors.TryGetValue(normalized,out donor)||donor==null)return false;
            GameObject instance=null;
            DialogAudioComponent component=null;
            string eventName=EventNameWithIntensity(key,normalized,intensity);
            bool added=false;
            try
            {
                instance=(GameObject)UnityEngine.Object.Instantiate(donor.gameObject);
                instance.SetActive(false);
                instance.name="EF_RhodesVoice_"+key;
                instance.hideFlags=HideFlags.DontSave;
                instance.transform.parent=root.transform;
                instance.transform.localPosition=Vector3.zero;
                instance.transform.localRotation=Quaternion.identity;
                instance.transform.localScale=Vector3.one;
                component=instance.GetComponent<DialogAudioComponent>();
                if(component==null||instance.GetComponents<Fabric.Component>().Length!=1||
                    instance.GetComponentsInChildren<DialogAudioComponent>(true).Length!=1)
                    throw new InvalidOperationException("Native voice donor must be a single dialog leaf");
                foreach(EventListener listener in instance.GetComponents<EventListener>())
                    UnityEngine.Object.DestroyImmediate(listener);
                EventListener own=instance.AddComponent<EventListener>();
                own._eventName=eventName;
                own._eventID=EventManager.GetIDFromEventName(eventName);
                component.AudioClip=null;
                component._audioClipHandle=null;
                // DialogAudioComponent.Load owns this clip. A copied dynamic
                // handle would otherwise overwrite it in AudioComponent.Play.
                component._dynamicAudioClipLoading=false;
                component._dynamicAsyncAudioClipLoading=false;
                component.Guid=Guid.NewGuid().ToString();
                component.ComponentHolder=component;
                ConfigurePrivateCapacity(component);
                component._onEventNotifyEventName=string.Empty;
                component.AudioClipReference="Rhodes/"+normalized+"/"+key+".wav";
                component.Loop=false;
                component.DontStopOnDestroy=false;
                component.Initialise(root,false);
                ValidatePrivateBinding(component,own,eventName);
                root.AddComponent(component);added=true;
                instance.SetActive(true);
                Events.Add(cacheKey,new Registration{EventName=eventName,Role=normalized,Component=component});
                if(!CapacityReported)
                {
                    CapacityReported=true;
                    Runtime.Log("RHODES_VOICE_PRIVATE_CAPACITY_READY donorMaxInstances="+donor.MaxInstances+
                        "; privateMaxInstances="+component.MaxInstances+"; nativeListenerBound=True; oncePerLoader=True; runtimeAudioAcceptancePending=True");
                }
                return true;
            }
            catch(Exception error)
            {
                if(component!=null)
                {
                    Manager.UnregisterListener(component,eventName);
                    if(added&&root!=null)root.RemoveComponent(component);
                    component.Destroy();
                }
                if(instance!=null)UnityEngine.Object.DestroyImmediate(instance);
                Report(error);return false;
            }
        }

        internal static void ConfigurePrivateCapacity(DialogAudioComponent component)
        {
            if(ReferenceEquals(component,null))throw new ArgumentNullException("component");
            // Native random-selection leaves have no event of their own and
            // carry MaxInstances=0. A clone with its own listener must own one
            // event instance or Fabric.CreateInstance always returns null.
            component.MaxInstances=1;
        }

        static void ValidatePrivateBinding(DialogAudioComponent component,EventListener own,string eventName)
        {
            List<EventListener> local=new List<EventListener>(1);
            IList registered=ListenerBindings==null?null:ListenerBindings[eventName] as IList;
            if(component.MaxInstances!=1||!((IEventListener)component).GetEventListeners(eventName,local)||
                local.Count!=1||!ReferenceEquals(local[0],own)||registered==null||registered.Count!=1||
                !ReferenceEquals(registered[0],component))
                throw new InvalidOperationException("Private Fabric leaf capacity or listener binding is invalid");
        }

        internal static VoiceOverEvent Route(VoiceOverEvent original,string key,string role)
        {
            if(original==null||!Register(key,role))return null;
            Registration entry=Events[key];
            return new VoiceOverEvent(original.Priority,Action(key),original.Group,"coalition",
                entry.Role,"gen",null,"gen",null,"gen",null,"gen",original.PositionalEntity,"cal");
        }

        internal static string EventName(string key,string role)
        {return EventNameWithIntensity(key,role,"cal");}

        internal static string EventNameWithIntensity(string key,string role,string intensity)
        {return "vo/coalition/"+role+"/"+Action(key)+"/gengengengen/"+intensity;}

        internal static VoiceOverEvent RoutePreservingIntensity(VoiceOverEvent original,string key,string role,string[] intensities)
        {
            if(original==null||!SupportsNativeIntensity(original.Intensity,intensities))return null;
            // ExecuteVO resolves a null/empty intensity AFTER this prefix, using
            // its own unit/drama state. Register all <=4 native choices without
            // reading or duplicating that combat-state calculation here.
            foreach(string intensity in intensities)
                if(!RegisterCore(key,role,intensity,intensity=="cal"?key:key+"/"+intensity))return null;
            return BuildPreservingIntensity(original,key,role,intensities);
        }

        // A field-preserving event clone; Fabric still owns playback and native
        // VO still owns priority, group, positional entity, intensity and queues.
        internal static VoiceOverEvent BuildPreservingIntensity(VoiceOverEvent original,string key,string role,string[] intensities)
        {
            string normalized;
            if(original==null||!ValidKey(key)||!TryRole(role,out normalized)||
                !SupportsNativeIntensity(original.Intensity,intensities))return null;
            return new VoiceOverEvent(original.Priority,Action(key),original.Group,"coalition",
                normalized,"gen",null,"gen",null,"gen",null,"gen",original.PositionalEntity,original.Intensity);
        }

        internal static string[] NativeIntensityFamily(object controller)
        {
            if(controller==null)return null;
            object fields=NativeRead.Get(controller,"Fields");
            object intensity=NativeRead.Get(fields,"Intensity");
            string[] candidates={(string)NativeRead.Get(intensity,"Calm"),(string)NativeRead.Get(intensity,"Tense"),
                (string)NativeRead.Get(intensity,"Combat"),"nt"};
            List<string> family=new List<string>(4);
            foreach(string token in candidates)
            {
                if(!ValidIntensityToken(token))return null;
                if(!family.Contains(token))family.Add(token);
            }
            return family.ToArray();
        }

        internal static bool SupportsNativeIntensity(string intensity,string[] family)
        {
            if(family==null||family.Length<1||family.Length>4)return false;
            foreach(string token in family)if(!ValidIntensityToken(token))return false;
            return string.IsNullOrEmpty(intensity)||Array.IndexOf(family,intensity)>=0;
        }

        static bool ValidIntensityToken(string intensity)
        {
            if(string.IsNullOrEmpty(intensity)||intensity.Length>16)return false;
            foreach(char value in intensity)
                if(!(value>='a'&&value<='z'||value>='A'&&value<='Z'||value>='0'&&value<='9'||value=='_'))return false;
            return true;
        }

        static string Action(string key){return "ef_"+key.ToLowerInvariant();}

        static string MixerTarget(string role)
        {
            return "AudioMaster_VO_Core Speech_VO_Speech_"+
                (role=="opsfleet"||role=="opscombat"?"Ops":"Allships");
        }

        static GroupComponent FindSpeechRoot(Transform node)
        {
            for(Transform current=node;current!=null;current=current.parent)
            {
                GroupComponent group=current.GetComponent<GroupComponent>();
                if(group!=null&&!string.IsNullOrEmpty(group._targetGroupComponentPath))return group;
            }
            return null;
        }

        static bool TryRole(string value,out string role)
        {
            role=value==null?null:value.ToLowerInvariant();
            return Array.IndexOf(Roles,role)>=0;
        }

        static bool ValidKey(string key)
        {
            if(string.IsNullOrEmpty(key)||key.Length>160)return false;
            foreach(char value in key)
                if(!(value>='a'&&value<='z'||value>='A'&&value<='Z'||value>='0'&&value<='9'||value=='_'||value=='.'||value=='-'))return false;
            return true;
        }

        static void Report(Exception error)
        {
            if(Warnings++>=3)return;
            Runtime.Log("RHODES_VOICE_PRIVATE_EVENT_UNAVAILABLE error="+error.GetType().Name+": "+error.Message);
        }

        internal static void Reset()
        {
            foreach(Registration entry in Events.Values)
            {
                DialogAudioComponent component=entry.Component;
                if(component==null)continue;
                if(Manager!=null)Manager.UnregisterListener(component,entry.EventName);
                GroupComponent root;
                if(RoleGroups.TryGetValue(entry.Role,out root)&&root!=null)root.RemoveComponent(component);
                component.Destroy();
                UnityEngine.Object.DestroyImmediate(component.gameObject);
            }
            Events.Clear();Donors.Clear();RoleGroups.Clear();
            foreach(GroupComponent root in Groups.Values)
            {
                if(root==null)continue;
                FabricManager fabric=FabricManager.Instance;
                if(fabric!=null&&root._isRegisteredWithMainHierarchy)
                    fabric.UnregisterGroupComponent(root,false);
                root._isRegisteredWithMainHierarchy=false;
                root.Destroy();
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
            Groups.Clear();Loader=null;FailedLoader=null;Manager=null;ListenerBindings=null;CapacityReported=false;
        }
    }
}
