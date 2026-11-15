// Per-unit native-event audio. No animation-driven firing, new AudioListener,
// global listener controls, simulation commands or changes to source audio.
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
    public sealed class HumanoidAttackAudio : MonoBehaviour
    {
        const int Voices=4,MaximumWaveBytes=4*1024*1024,MaximumConfigBytes=4096,MaximumCachedClips=16;
        static readonly Dictionary<string,AudioClip> Clips=new Dictionary<string,AudioClip>(StringComparer.OrdinalIgnoreCase);
        static readonly List<HumanoidAttackAudio> Instances=new List<HumanoidAttackAudio>();
        static AudioListener ExistingListener;
        static float NextListenerScan;
        UnitView View;
        Entity BoundEntity;
        string RequestedFolder;
        GameObject VoiceRoot;
        AudioSource[] Sources;
        AudioClip Clip,HitClip;
        bool Attempted,PausedOwn;
        bool TriggerOnWindup;
        long LastWindup,LastRound,LastImpact,PendingWindupSequence;
        int NextVoice,Reports,PendingWindupVoice=-1;
        long[] VoiceWindupSequences;
        float MaximumAudibleDistance;

        internal sealed class Configuration
        {
            public string Shot,Hit,Trigger="round";
            public float Volume=.4f,MinDistance=20f,MaxDistance=260f,Pitch=1f;
        }
        internal sealed class DecodedWave
        {
            public readonly int SampleRate;
            public readonly float[] Samples;
            public DecodedWave(int rate,float[] samples){SampleRate=rate;Samples=samples;}
        }

        // Main/presentation thread only. Failure disables this optional audio
        // component and is logged; it never interrupts native combat or motion.
        public void Initialize(UnitView view,string actorFolderAbs)
        {
            if(Attempted&&View==view&&view!=null&&BoundEntity.Equals(view.EntityID)&&
                string.Equals(RequestedFolder,actorFolderAbs,StringComparison.OrdinalIgnoreCase))return;
            ReleaseOwn();View=view;RequestedFolder=actorFolderAbs;Attempted=true;
            if(view!=null)BoundEntity=view.EntityID;
            Instances.Add(this);
            try
            {
                if(view==null)throw new ArgumentNullException("view");
                string folder=ActorFolder(actorFolderAbs),configPath=SafePath(folder,"humanoid_audio.ini");
                if(!File.Exists(configPath))
                {Runtime.Log("HUMANOID_ATTACK_AUDIO_NOT_CONFIGURED entity="+BoundEntity+"; folder="+folder);return;}
                string text=new UTF8Encoding(false,true).GetString(ReadBoundedFile(configPath,MaximumConfigBytes));
                if(text.Length>0&&text[0]=='\uFEFF')text=text.Substring(1);
                Configuration config=ParseConfiguration(text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'));
                string wavePath=SafePath(folder,config.Shot);
                Clip=LoadClip(wavePath);
                string hitPath=string.IsNullOrEmpty(config.Hit)?null:SafePath(folder,config.Hit);
                if(hitPath!=null)HitClip=LoadClip(hitPath);
                TriggerOnWindup=config.Trigger=="windup";CreateVoices(config);MainTick();
                Runtime.Log("HUMANOID_ATTACK_AUDIO_READY entity="+BoundEntity+"; source="+wavePath+
                    "; voices=4; spatialBlend=1; rolloff=Linear; hardMaximumDistance=True; volume="+config.Volume+"; minDistance="+config.MinDistance+
                    "; maxDistance="+config.MaxDistance+"; pitch="+config.Pitch+"; existingListener="+(ExistingListener!=null)+
                    "; trigger="+config.Trigger+"; hit="+(hitPath??"none")+
                    "; realNativeEventsOnly=True; globalListenerUnchanged=True");
            }
            catch(Exception error)
            {
                DestroyVoices();Clip=HitClip=null;
                Runtime.Log("HUMANOID_ATTACK_AUDIO_DISABLED entity="+BoundEntity+"; reason="+error.GetType().Name+": "+error.Message+
                    "; nativeCombatUnaffected=True");
            }
        }

        // All three interfaces receive the shared native message.EventSequence,
        // not each weapon's separate round counter. Calls stay in native order.
        // A windup precedes its round; round and impact may share one event ID.
        public void PlayWindup(long nativeEventSequence,Vector3 muzzleWorld)
        {
            if(nativeEventSequence<=LastWindup||nativeEventSequence<=0)return;
            LastWindup=nativeEventSequence;
            if(!TriggerOnWindup)return;
            // A replacement windup cancels only the previous uncommitted voice.
            CancelWindup();
            int voice=Emit(Clip,nativeEventSequence,muzzleWorld,"WINDUP");
            if(voice<0)return;
            PendingWindupVoice=voice;PendingWindupSequence=nativeEventSequence;
            VoiceWindupSequences[voice]=nativeEventSequence;
        }

        public void PlayRound(long nativeEventSequence,Vector3 muzzleWorld)
        {
            if(nativeEventSequence<=LastRound||nativeEventSequence<=0)return;
            LastRound=nativeEventSequence;
            if(TriggerOnWindup)
            {
                // The original windup clip includes the release and tail. A real
                // release commits that voice, without replaying the same waveform.
                if(PendingWindupVoice>=0&&nativeEventSequence>PendingWindupSequence)
                {PendingWindupVoice=-1;PendingWindupSequence=0;}
                return;
            }
            Emit(Clip,nativeEventSequence,muzzleWorld,"ROUND");
        }

        public void PlayImpact(long nativeEventSequence,Vector3 impactWorld)
        {
            if(nativeEventSequence<=LastImpact||nativeEventSequence<=0)return;
            LastImpact=nativeEventSequence;
            Emit(HitClip,nativeEventSequence,impactWorld,"IMPACT");
        }

        public void CancelWindup()
        {
            if(PendingWindupVoice>=0&&Sources!=null&&VoiceWindupSequences!=null&&
                PendingWindupVoice<Sources.Length&&VoiceWindupSequences[PendingWindupVoice]==PendingWindupSequence)
            {
                AudioSource source=Sources[PendingWindupVoice];if(source!=null)source.Stop();
                VoiceWindupSequences[PendingWindupVoice]=0;
            }
            PendingWindupVoice=-1;PendingWindupSequence=0;
        }

        int Emit(AudioClip clip,long sequence,Vector3 position,string kind)
        {
            if(PausedOwn||clip==null||Sources==null||!Finite(position)||!CanEmit())return -1;
            MainTick();if(!ListenerValid(ExistingListener))return -1;
            if(!WithinMaximumDistance(position,ExistingListener.transform.position,MaximumAudibleDistance))return -1;
            int chosen=NextVoice;
            for(int offset=0;offset<Sources.Length;offset++)
            {
                int candidate=(NextVoice+offset)%Sources.Length;
                if(Sources[candidate]!=null&&!Sources[candidate].isPlaying){chosen=candidate;break;}
            }
            AudioSource source=Sources[chosen];if(source==null)return -1;
            // If voice pressure reuses a pending slot, its previous identity must
            // no longer allow CancelWindup to stop an unrelated impact or tail.
            VoiceWindupSequences[chosen]=0;
            if(PendingWindupVoice==chosen){PendingWindupVoice=-1;PendingWindupSequence=0;}
            source.Stop();source.transform.position=position;source.clip=clip;source.Play();
            NextVoice=(chosen+1)%Sources.Length;
            if(Reports<8)
            {
                Reports++;Runtime.Log("HUMANOID_ATTACK_AUDIO_"+kind+" entity="+BoundEntity+"; sequence="+sequence+
                    "; voice="+chosen+"; position="+position+"; realNativeEvent=True; animationLoopDidNotCreateShot=True");
            }
            return chosen;
        }

        public void Tick(float presentationDt)
        {
            if(Sources==null)return;
            bool pause=!Finite(presentationDt)||presentationDt<=0f;
            if(pause!=PausedOwn)
            {
                foreach(AudioSource source in Sources)if(source!=null){if(pause)source.Pause();else source.UnPause();}
                PausedOwn=pause;
            }
            if(!CanEmit())foreach(AudioSource source in Sources)if(source!=null)source.Stop();
        }

        bool CanEmit()
        {
            return View!=null&&View.gameObject.activeInHierarchy&&View.EntityID.Equals(BoundEntity)&&
                View.Visibility==DetectionState.Sensed&&View.NonInterpolatedTickState!=null&&
                !View.NonInterpolatedTickState.UnitIsDead&&View.NonInterpolatedTickState.Hitpoints>0;
        }

        // Discovery only. Native Fabric/scene listeners remain owned by DoK.
        public static void MainTick()
        {
            // The deadline also gates an absent/disabled listener. Missing
            // listeners must not turn this fallback into an every-frame scan.
            if(Time.realtimeSinceStartup<NextListenerScan)return;
            NextListenerScan=Time.realtimeSinceStartup+.5f;ExistingListener=null;
            Camera main=Camera.main;
            if(main!=null)
            {
                AudioListener listener=main.GetComponent<AudioListener>();
                if(ListenerValid(listener))ExistingListener=listener;
            }
            if(ExistingListener==null)
                foreach(AudioListener listener in UnityEngine.Object.FindObjectsOfType(typeof(AudioListener)))
                    if(ListenerValid(listener)){ExistingListener=listener;break;}
        }
        static bool ListenerValid(AudioListener listener)
        {return listener!=null&&listener.enabled&&listener.gameObject.activeInHierarchy;}

        void CreateVoices(Configuration config)
        {
            MaximumAudibleDistance=config.MaxDistance;
            VoiceRoot=new GameObject("EF_HumanoidAttackAudio_"+BoundEntity);VoiceRoot.hideFlags=HideFlags.DontSave;
            Sources=new AudioSource[Voices];VoiceWindupSequences=new long[Voices];
            for(int voice=0;voice<Voices;voice++)
            {
                GameObject node=new GameObject("RoundVoice_"+voice);node.transform.parent=VoiceRoot.transform;
                AudioSource source=node.AddComponent<AudioSource>();Sources[voice]=source;
                source.playOnAwake=false;source.loop=false;source.dopplerLevel=0f;source.spatialBlend=1f;
                source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=config.MinDistance;source.maxDistance=config.MaxDistance;
                source.volume=config.Volume;source.pitch=Mathf.Clamp(config.Pitch+(voice-1.5f)*.004f,.98f,1.02f);
                source.ignoreListenerPause=false;source.ignoreListenerVolume=false;source.clip=Clip;
            }
        }

        static AudioClip LoadClip(string path)
        {
            AudioClip cached;
            if(Clips.TryGetValue(path,out cached))
            {if(cached!=null)return cached;Clips.Remove(path);}
            if(Clips.Count>=MaximumCachedClips)throw new InvalidDataException("Humanoid audio clip cache is full");
            DecodedWave wave=DecodePcm16Wave(ReadBoundedFile(path,MaximumWaveBytes));
            AudioClip result=AudioClip.Create("EF_Attack_"+Path.GetFileNameWithoutExtension(path),wave.Samples.Length,1,wave.SampleRate,false);
            if(result==null)throw new InvalidOperationException("Unity could not create humanoid attack clip");
            if(!result.SetData(wave.Samples,0)){UnityEngine.Object.Destroy(result);throw new InvalidOperationException("Unity could not load humanoid PCM samples");}
            Clips.Add(path,result);return result;
        }
        internal static byte[] ReadBoundedFile(string path,int maximum)
        {
            using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length<1||stream.Length>maximum)throw new InvalidDataException("Humanoid audio file exceeds its bounded size");
                byte[] bytes=new byte[(int)stream.Length];int read=0;
                while(read<bytes.Length){int got=stream.Read(bytes,read,bytes.Length-read);if(got==0)throw new EndOfStreamException("Humanoid audio file truncated while reading");read+=got;}
                return bytes;
            }
        }

        internal static Configuration ParseConfiguration(string[] lines)
        {
            Configuration result=new Configuration();HashSet<string> seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string raw in lines)
            {
                string line=raw.Trim();
                if(line.Length==0||line.StartsWith(";")||line.StartsWith("#")||line.StartsWith("[")&&line.EndsWith("]"))continue;
                int split=line.IndexOf('=');if(split<1)throw new InvalidDataException("Malformed humanoid audio setting");
                string key=line.Substring(0,split).Trim().ToLowerInvariant(),value=line.Substring(split+1).Trim();
                if(!seen.Add(key))throw new InvalidDataException("Duplicate humanoid audio key: "+key);
                if(key=="shot")result.Shot=value;
                else if(key=="hit")
                {if(value.Length==0)throw new InvalidDataException("Humanoid audio hit path cannot be empty");result.Hit=value;}
                else if(key=="trigger")
                {
                    result.Trigger=value.ToLowerInvariant();
                    if(result.Trigger!="round"&&result.Trigger!="windup")throw new InvalidDataException("Humanoid audio trigger must be round or windup");
                }
                else if(key=="volume")result.Volume=Number(value,0f,1f,key);
                else if(key=="mindistance")result.MinDistance=Number(value,.5f,100f,key);
                else if(key=="maxdistance")result.MaxDistance=Number(value,10f,2000f,key);
                else if(key=="pitch")result.Pitch=Number(value,.98f,1.02f,key);
                else throw new InvalidDataException("Unknown humanoid audio key: "+key);
            }
            if(string.IsNullOrEmpty(result.Shot)||result.MaxDistance<result.MinDistance)throw new InvalidDataException("Humanoid audio shot or distance relation invalid");
            return result;
        }
        static float Number(string text,float minimum,float maximum,string key)
        {
            float value;
            if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!Finite(value)||value<minimum||value>maximum)
                throw new InvalidDataException("Invalid humanoid audio value: "+key);
            return value;
        }
        static string ActorFolder(string input)
        {
            if(string.IsNullOrEmpty(input)||!Path.IsPathRooted(input))throw new InvalidDataException("Humanoid audio folder must be absolute");
            string folder=Path.GetFullPath(input);if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Humanoid audio actor folder absent");
            RejectReparse(folder);return folder;
        }
        internal static string SafePath(string actorFolderAbs,string relative)
        {
            if(string.IsNullOrEmpty(actorFolderAbs)||!Path.IsPathRooted(actorFolderAbs))throw new InvalidDataException("Humanoid audio actor folder must be absolute");
            if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.IndexOf(':')>=0||relative.IndexOf('\0')>=0)
                throw new InvalidDataException("Humanoid audio resource must be a safe relative path");
            string folder=Path.GetFullPath(actorFolderAbs),normal=relative.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar);
            string[] pieces=normal.Split(Path.DirectorySeparatorChar);
            foreach(string piece in pieces)if(piece.Length==0||piece=="."||piece==".."||piece.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
                throw new InvalidDataException("Humanoid audio traversal or invalid segment rejected");
            string path=Path.GetFullPath(Path.Combine(folder,normal));
            string prefix=folder.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Humanoid audio resource escaped actor folder");
            RejectReparse(folder);string step=folder;
            foreach(string piece in pieces){step=Path.Combine(step,piece);if(File.Exists(step)||Directory.Exists(step))RejectReparse(step);}
            if(!string.Equals(relative,"humanoid_audio.ini",StringComparison.OrdinalIgnoreCase)&&!string.Equals(Path.GetExtension(path),".wav",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Humanoid shot audio must be WAV");
            return path;
        }
        static void RejectReparse(string path)
        {if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Humanoid audio reparse-point resource rejected");}

        internal static DecodedWave DecodePcm16Wave(byte[] bytes)
        {
            if(bytes==null||bytes.Length<44||bytes.Length>MaximumWaveBytes||Tag(bytes,0)!="RIFF"||Tag(bytes,8)!="WAVE")
                throw new InvalidDataException("Expected bounded RIFF/WAVE PCM16");
            long end=8L+U32(bytes,4);if(end!=bytes.Length)throw new InvalidDataException("WAV RIFF length differs from file");
            int channels=0,rate=0,align=0,dataAt=-1,dataSize=0;bool format=false;
            long position=12;
            while(position<end)
            {
                if(position+8>end)throw new InvalidDataException("WAV chunk header truncated");
                int header=(int)position;string kind=Tag(bytes,header);uint size=U32(bytes,header+4);
                long body=position+8,limit=body+size,padded=limit+(size&1u);
                if(limit>end||padded>end)throw new InvalidDataException("WAV chunk payload truncated");
                if(kind=="fmt ")
                {
                    if(format||size<16||size>64)throw new InvalidDataException("Invalid or duplicate WAV format");
                    int at=(int)body;int codec=U16(bytes,at),bits=U16(bytes,at+14);channels=U16(bytes,at+2);
                    uint sampleRate=U32(bytes,at+4),byteRate=U32(bytes,at+8);align=U16(bytes,at+12);
                    if(codec!=1||bits!=16||(channels!=1&&channels!=2)||sampleRate<8000||sampleRate>96000||align!=channels*2||byteRate!=sampleRate*(uint)align)
                        throw new InvalidDataException("WAV must be 8-96kHz mono/stereo PCM16 with consistent alignment");
                    rate=(int)sampleRate;format=true;
                }
                else if(kind=="data")
                {
                    if(dataAt>=0)throw new InvalidDataException("Duplicate WAV data chunk");
                    dataAt=(int)body;dataSize=(int)size;
                }
                position=padded;
            }
            if(!format||dataAt<0||dataSize<align||dataSize%align!=0)throw new InvalidDataException("WAV format/data absent or unaligned");
            int frames=dataSize/align;if(frames>rate*10)throw new InvalidDataException("Humanoid shot WAV exceeds ten seconds");
            float[] samples=new float[frames];
            for(int frame=0;frame<frames;frame++)
            {
                int at=dataAt+frame*align;int left=(short)U16(bytes,at);
                samples[frame]=channels==1?left/32768f:(left+(short)U16(bytes,at+2))/65536f;
            }
            return new DecodedWave(rate,samples);
        }
        static ushort U16(byte[] bytes,int at){return (ushort)(bytes[at]|bytes[at+1]<<8);}
        static uint U32(byte[] bytes,int at){return (uint)(bytes[at]|bytes[at+1]<<8|bytes[at+2]<<16|bytes[at+3]<<24);}
        static string Tag(byte[] bytes,int at){return new string(new char[]{(char)bytes[at],(char)bytes[at+1],(char)bytes[at+2],(char)bytes[at+3]});}
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static bool Finite(Vector3 value){return Finite(value.x)&&Finite(value.y)&&Finite(value.z);}
        internal static bool WithinMaximumDistance(Vector3 source,Vector3 listener,float maximum)
        {
            if(!Finite(source)||!Finite(listener)||!Finite(maximum)||maximum<=0f)return false;
            double x=(double)source.x-listener.x,y=(double)source.y-listener.y,z=(double)source.z-listener.z;
            return x*x+y*y+z*z<=(double)maximum*maximum;
        }

        void DestroyVoices()
        {
            if(Sources!=null)foreach(AudioSource source in Sources)if(source!=null)source.Stop();
            Sources=null;VoiceWindupSequences=null;PendingWindupVoice=-1;PendingWindupSequence=0;
            if(VoiceRoot!=null)UnityEngine.Object.Destroy(VoiceRoot);VoiceRoot=null;
        }
        void ReleaseOwn()
        {
            DestroyVoices();Clip=HitClip=null;View=null;RequestedFolder=null;Attempted=false;PausedOwn=TriggerOnWindup=false;
            LastWindup=LastRound=LastImpact=0;NextVoice=Reports=0;MaximumAudibleDistance=0f;
            Instances.Remove(this);
        }
        void OnDisable(){ReleaseOwn();}
        void OnDestroy(){ReleaseOwn();}
        public static void ResetAll()
        {
            HumanoidAttackAudio[] copy=Instances.ToArray();foreach(HumanoidAttackAudio instance in copy)if(instance!=null)instance.ReleaseOwn();
            Instances.Clear();foreach(AudioClip clip in Clips.Values)if(clip!=null)UnityEngine.Object.Destroy(clip);Clips.Clear();
            ExistingListener=null;NextListenerScan=0f;
        }
    }
}
