// Optional unit acknowledgements. Presentation only: no orders, simulated
// attacks, new listeners, Fabric/global volume or global pause changes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    internal enum HumanoidVoiceKind { None=-1, Select=0, Move=1, Attack=2, Ability=3 }

    // Pure managed, no Unity types. Edges/requests are consumed immediately:
    // busy, paused, hidden or cooling-down requests are never queued.
    internal sealed class HumanoidVoiceState
    {
        readonly double[] NextKind=new double[4];
        readonly int[] PreviousClip=new int[]{-1,-1,-1,-1};
        readonly int Available;
        uint RandomState;
        double Clock,NextAny,AttackSilence;
        bool Selected,Moving,AttackActive,Eligible,Paused=true;
        int Target;
        long LastAttackEvent,LastAbilityEvent;
        public HumanoidVoiceKind CurrentKind { get; private set; }
        public HumanoidVoiceState(int available,uint seed)
        {Available=available&15;RandomState=seed==0?0x9E3779B9u:seed;CurrentKind=HumanoidVoiceKind.None;}

        public HumanoidVoiceKind Step(float dt,bool selected,float speed,bool eligible,bool audioBusy)
        {
            bool selectEdge=selected&&!Selected;Selected=selected;
            bool moving=Finite(speed)&&(Moving?speed>.06f:speed>.12f);
            bool moveEdge=moving&&!Moving;Moving=moving;
            Paused=!Finite(dt)||dt<=0f;Eligible=eligible;
            if(Paused)return HumanoidVoiceKind.None;
            Clock+=dt;
            if(AttackActive)AttackSilence+=dt;
            if(!eligible){EndAttackSession();return HumanoidVoiceKind.None;}
            if(moving)EndAttackSession();
            if(selectEdge)return Request(HumanoidVoiceKind.Select,audioBusy);
            if(moveEdge&&selected)return Request(HumanoidVoiceKind.Move,audioBusy);
            return HumanoidVoiceKind.None;
        }
        public HumanoidVoiceKind ObserveAttackSession(long eventSequence,int targetToken,bool audioBusy,bool eligibleNow)
        {
            if(eventSequence<=0||eventSequence<=LastAttackEvent)return HumanoidVoiceKind.None;
            LastAttackEvent=eventSequence;
            if(Paused||!Eligible||!eligibleNow||Moving)return HumanoidVoiceKind.None;
            bool changed=AttackActive&&Target!=0&&targetToken!=0&&Target!=targetToken;
            bool first=!AttackActive||changed||AttackSilence>=8d;
            AttackActive=true;AttackSilence=0d;if(targetToken!=0)Target=targetToken;
            return first?Request(HumanoidVoiceKind.Attack,audioBusy):HumanoidVoiceKind.None;
        }
        public HumanoidVoiceKind ObserveAbility(long acceptedSequence,bool audioBusy,bool eligibleNow)
        {
            if(acceptedSequence<=0||acceptedSequence<=LastAbilityEvent)return HumanoidVoiceKind.None;
            LastAbilityEvent=acceptedSequence;
            if(Paused||!Eligible||!eligibleNow)return HumanoidVoiceKind.None;
            return Request(HumanoidVoiceKind.Ability,audioBusy);
        }
        public void EndAttackSession(){AttackActive=false;AttackSilence=0d;Target=0;}
        HumanoidVoiceKind Request(HumanoidVoiceKind kind,bool busy)
        {
            int slot=(int)kind;
            if(Paused||!Eligible||(Available&(1<<slot))==0||Clock<NextKind[slot])return HumanoidVoiceKind.None;
            bool ability=kind==HumanoidVoiceKind.Ability;
            if(ability)
            {if(busy&&CurrentKind==HumanoidVoiceKind.Ability)return HumanoidVoiceKind.None;}
            else if(busy||Clock<NextAny)return HumanoidVoiceKind.None;
            NextKind[slot]=Clock+(kind==HumanoidVoiceKind.Select?4d:kind==HumanoidVoiceKind.Move?7d:kind==HumanoidVoiceKind.Attack?15d:10d);
            NextAny=Clock+1.25d;CurrentKind=kind;return kind;
        }
        public int ChooseClip(HumanoidVoiceKind kind,int count)
        {
            if((int)kind<0||(int)kind>3||count<1||count>4)return -1;
            uint value=NextRandom();int previous=PreviousClip[(int)kind];
            int chosen=count==1?0:previous<0?(int)(value%(uint)count):(previous+1+(int)(value%(uint)(count-1)))%count;
            PreviousClip[(int)kind]=chosen;return chosen;
        }
        uint NextRandom(){uint value=RandomState;value^=value<<13;value^=value>>17;value^=value<<5;RandomState=value;return value;}
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }

    public sealed class HumanoidUnitVoice : MonoBehaviour
    {
        // Current configured roster: 29 existing clips + Ardelia4 =33.
        // Keep a fixed cache ceiling; reset still destroys every cached clip.
        const int MaximumConfigBytes=4096,MaximumWaveBytes=4*1024*1024,MaximumCachedClips=40;
        static readonly Dictionary<string,AudioClip> Clips=new Dictionary<string,AudioClip>(StringComparer.OrdinalIgnoreCase);
        static readonly List<HumanoidUnitVoice> Instances=new List<HumanoidUnitVoice>();
        UnitView View;Entity BoundEntity;string RequestedFolder;
        GameObject VoiceRoot;AudioSource Source;AudioClip[][] Categories;
        HumanoidVoiceState State;
        bool Attempted,PausedOwn;
        int Reports;
        internal sealed class Configuration
        {
            public readonly string[][] Paths=new string[4][];
            public float Volume=.7f;
        }

        // Root binds only the supported operator and guard presentations.
        public void Initialize(UnitView view,string actorFolderAbs)
        {
            if(Attempted&&View==view&&view!=null&&BoundEntity.Equals(view.EntityID)&&
                string.Equals(RequestedFolder,actorFolderAbs,StringComparison.OrdinalIgnoreCase))return;
            ReleaseOwn();View=view;RequestedFolder=actorFolderAbs;Attempted=true;
            if(view!=null)BoundEntity=view.EntityID;Instances.Add(this);
            try
            {
                if(view==null)throw new ArgumentNullException("view");
                string configPath=VoiceConfigurationPath(actorFolderAbs);
                if(!File.Exists(configPath))
                {Runtime.Log("HUMANOID_UNIT_VOICE_NOT_CONFIGURED entity="+BoundEntity+"; config="+configPath);return;}
                string text=new UTF8Encoding(false,true).GetString(HumanoidAttackAudio.ReadBoundedFile(configPath,MaximumConfigBytes));
                if(text.Length>0&&text[0]=='\uFEFF')text=text.Substring(1);
                Configuration config=ParseConfiguration(text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'));
                Categories=new AudioClip[4][];int available=0,total=0;
                for(int kind=0;kind<4;kind++)
                {
                    string[] paths=config.Paths[kind];if(paths==null)continue;
                    Categories[kind]=new AudioClip[paths.Length];
                    for(int index=0;index<paths.Length;index++)
                    {Categories[kind][index]=LoadClip(HumanoidAttackAudio.SafePath(actorFolderAbs,paths[index]));total++;}
                    available|=1<<kind;
                }
                State=new HumanoidVoiceState(available,unchecked((uint)BoundEntity.GetHashCode()+0xA511E9B3u));
                VoiceRoot=new GameObject("EF_HumanoidUnitVoice_"+BoundEntity);VoiceRoot.hideFlags=HideFlags.DontSave;
                Source=VoiceRoot.AddComponent<AudioSource>();Source.playOnAwake=false;Source.loop=false;
                Source.spatialBlend=0f;Source.dopplerLevel=0f;Source.volume=config.Volume;Source.pitch=1f;
                Source.ignoreListenerPause=false;Source.ignoreListenerVolume=false;
                Runtime.Log("HUMANOID_UNIT_VOICE_READY entity="+BoundEntity+"; clips="+total+"; categoryMask="+available+
                    "; volume="+config.Volume+"; spatialBlend=0; queue=False; existingListenerOnly=True; simulationUnchanged=True");
            }
            catch(Exception error)
            {
                DestroyOwnSource();Categories=null;State=null;
                Runtime.Log("HUMANOID_UNIT_VOICE_DISABLED entity="+BoundEntity+"; reason="+error.GetType().Name+": "+error.Message+"; nativeCombatUnaffected=True");
            }
        }

        // Call once per presentation frame BEFORE the native attack/ability
        // event callbacks. Speed is native horizontal speed in metres/second.
        // Move feedback is inferred from actual start motion, not command receipt.
        public void Tick(float presentationDt,bool selected,float speed)
        {
            if(Source==null||State==null)return;
            bool pause=!Finite(presentationDt)||presentationDt<=0f;
            if(pause!=PausedOwn){if(pause)Source.Pause();else Source.UnPause();PausedOwn=pause;}
            bool eligible=CanEmit();if(!eligible)Source.Stop();
            HumanoidVoiceKind kind=State.Step(presentationDt,selected,speed,eligible,Source.isPlaying);
            Play(kind);
        }
        // Shared ordinary-weapon message.EventSequence, not animation frames.
        // Call for native windup or round. Same engagement does not speak each
        // round; target change, move or >=8s silence starts another engagement.
        public void ObserveAttackSession(long nativeEventSequence,int targetToken)
        {
            if(State==null||Source==null)return;
            Play(State.ObserveAttackSession(nativeEventSequence,targetToken,Source.isPlaying,CanEmit()));
        }
        public void PlayAttack(long nativeEventSequence,int targetToken)
        {ObserveAttackSession(nativeEventSequence,targetToken);}
        // Only after real native skill acceptance/WindUpStart, never on button
        // preview, animation or a failed cooldown/target-validation attempt.
        public void PlayAbility(long nativeAcceptedSequence)
        {
            if(State==null||Source==null)return;
            Play(State.ObserveAbility(nativeAcceptedSequence,Source.isPlaying,CanEmit()));
        }
        public void EndAttackSession(){if(State!=null)State.EndAttackSession();}
        void Play(HumanoidVoiceKind kind)
        {
            if(kind==HumanoidVoiceKind.None||PausedOwn||Source==null||State==null||!CanEmit())return;
            AudioClip[] options=Categories[(int)kind];if(options==null||options.Length==0)return;
            int index=State.ChooseClip(kind,options.Length);if(index<0||options[index]==null)return;
            bool preempt=Source.isPlaying;Source.Stop();Source.clip=options[index];Source.Play();
            if(Reports<24)
            {Reports++;Runtime.Log("HUMANOID_UNIT_VOICE_PLAY entity="+BoundEntity+"; kind="+kind+"; variant="+index+"; preempt="+preempt+"; queued=False; realNativeOrSelectionMotionEdge=True");}
        }
        bool CanEmit()
        {
            return View!=null&&View.gameObject.activeInHierarchy&&View.EntityID.Equals(BoundEntity)&&
                View.Visibility==DetectionState.Sensed&&View.NonInterpolatedTickState!=null&&
                !View.NonInterpolatedTickState.UnitIsDead&&View.NonInterpolatedTickState.Hitpoints>0;
        }
        static AudioClip LoadClip(string path)
        {
            AudioClip cached;if(Clips.TryGetValue(path,out cached)){if(cached!=null)return cached;Clips.Remove(path);}
            if(Clips.Count>=MaximumCachedClips)throw new InvalidDataException("Humanoid unit voice cache is full");
            HumanoidAttackAudio.DecodedWave wave=HumanoidAttackAudio.DecodePcm16Wave(HumanoidAttackAudio.ReadBoundedFile(path,MaximumWaveBytes));
            AudioClip clip=AudioClip.Create("EF_UnitVoice_"+Path.GetFileNameWithoutExtension(path),wave.Samples.Length,1,wave.SampleRate,false);
            if(clip==null)throw new InvalidOperationException("Unity could not create humanoid unit voice clip");
            if(!clip.SetData(wave.Samples,0)){UnityEngine.Object.Destroy(clip);throw new InvalidOperationException("Unity could not load humanoid unit voice PCM");}
            Clips.Add(path,clip);return clip;
        }
        internal static string VoiceConfigurationPath(string actorFolderAbs)
        {
            if(string.IsNullOrEmpty(actorFolderAbs)||!Path.IsPathRooted(actorFolderAbs))throw new InvalidDataException("Humanoid voice actor folder must be absolute");
            string folder=Path.GetFullPath(actorFolderAbs);if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Humanoid voice actor folder absent");
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Humanoid voice actor folder cannot be a reparse point");
            // A fixed basename only; do not widen HumanoidAttackAudio.SafePath.
            string path=Path.Combine(folder,"humanoid_voice.ini");
            if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("Humanoid voice configuration cannot be a reparse point");
            return path;
        }
        internal static Configuration ParseConfiguration(string[] lines)
        {
            Configuration result=new Configuration();HashSet<string> seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string raw in lines)
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith(";")||line.StartsWith("#")||line.StartsWith("[")&&line.EndsWith("]"))continue;
                int split=line.IndexOf('=');if(split<1)throw new InvalidDataException("Malformed humanoid voice setting");
                string key=line.Substring(0,split).Trim().ToLowerInvariant(),value=line.Substring(split+1).Trim();
                if(!seen.Add(key))throw new InvalidDataException("Duplicate humanoid voice key: "+key);
                if(key=="volume")
                {
                    float number;
                    if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)||!Finite(number)||number<0f||number>1f)
                        throw new InvalidDataException("Humanoid voice volume must be 0..1");
                    result.Volume=number;continue;
                }
                int kind=key=="select"?0:key=="move"?1:key=="attack"?2:key=="ability"?3:-1;
                if(kind<0)throw new InvalidDataException("Unknown humanoid voice key: "+key);
                string[] paths=value.Split('|');if(paths.Length<1||paths.Length>4)throw new InvalidDataException("Humanoid voice category needs 1..4 WAV paths");
                HashSet<string> unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for(int index=0;index<paths.Length;index++)
                {paths[index]=paths[index].Trim();if(paths[index].Length==0||!unique.Add(paths[index]))throw new InvalidDataException("Empty or duplicate humanoid voice category WAV");}
                result.Paths[kind]=paths;
            }
            return result;
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        void DestroyOwnSource()
        {
            if(Source!=null)Source.Stop();Source=null;
            if(VoiceRoot!=null)UnityEngine.Object.Destroy(VoiceRoot);VoiceRoot=null;
        }
        void ReleaseOwn()
        {
            DestroyOwnSource();View=null;RequestedFolder=null;Categories=null;State=null;Attempted=false;PausedOwn=false;Reports=0;
            Instances.Remove(this);
        }
        void OnDisable(){ReleaseOwn();}
        void OnDestroy(){ReleaseOwn();}
        public static void ResetAll()
        {
            HumanoidUnitVoice[] copy=Instances.ToArray();foreach(HumanoidUnitVoice voice in copy)if(voice!=null)voice.ReleaseOwn();
            Instances.Clear();foreach(AudioClip clip in Clips.Values)if(clip!=null)UnityEngine.Object.Destroy(clip);Clips.Clear();
        }
    }
}
