// Re-edited original Endfield energy SFX. Native phase observations alone
// trigger the release; this component never schedules simulation or damage.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public sealed class PerlicaOrbitalAudio : MonoBehaviour
    {
        static readonly List<PerlicaOrbitalAudio> Instances=new List<PerlicaOrbitalAudio>();
        static AudioListener Listener;
        static float NextListenerScan;
        readonly PerlicaOrbitalAudioPlan Plan=new PerlicaOrbitalAudioPlan();
        AudioSource Charge,Release;
        AudioClip ChargeClip,ReleaseClip;
        bool Attempted,PausedOwn,ReleaseStarted;
        // About +2 dB, preserving the original sample peaks and tail.
        const float ChargeVolume=.63f,ReleaseVolume=.82f;

        public void Designate(Vector3 target,Vector3 caster)
        {
            if(!Finite(target)||!Finite(caster))return;
            Cancel();EnsureSources();Plan.Designate();
            if(Charge==null||Release==null)return;
            Charge.transform.position=target;Release.transform.position=target;
            Charge.Play();
            Runtime.Log("PERLICA_ORBITAL_ENERGY_AUDIO phase=charge; source=Endfield_Perlica_ultimate; loopUntilNativeArrival=True; originalDoKOrbitalSoundsPosted=False");
        }
        public void Launched(){Plan.Launched();}
        public void Arrived()
        {
            if(!Plan.Arrived())return;
            StartRelease();
        }
        public void Detonated()
        {
            if(!Plan.Detonated())return;
            StartRelease();
            // The original energy event already contains the decay. Do not add
            // a second projectile/explosion sound or truncate the natural tail.
        }
        void StartRelease()
        {
            if(Charge!=null)Charge.Stop();
            if(ReleaseStarted)return;ReleaseStarted=true;
            if(Release==null)return;
            Release.Play();
            Runtime.Log("PERLICA_ORBITAL_ENERGY_AUDIO phase=beam; source=Endfield_Perlica_ultimate; trigger=actualNativeArrival; oneShotTailPreserved=True; originalDoKOrbitalSoundsPosted=False");
        }
        public void Cancel()
        {
            if(Charge!=null)Charge.Stop();if(Release!=null)Release.Stop();
            Plan.Cancel();ReleaseStarted=false;PausedOwn=false;
        }
        public void Tick(float presentationDt)
        {
            bool pause=!Finite(presentationDt)||presentationDt<=0f;
            Plan.SetPaused(pause);
            if(pause!=PausedOwn)
            {
                if(Charge!=null){if(pause)Charge.Pause();else Charge.UnPause();}
                if(Release!=null){if(pause)Release.Pause();else Release.UnPause();}
                PausedOwn=pause;
            }
            // Linear rolloff is supplemented with an explicit outer gate,
            // because Unity's maxDistance alone is not a hard mute contract.
            ScanListener();
            if(Charge!=null)Charge.volume=Audible(Charge.transform.position,600f)?ChargeVolume:0f;
            if(Release!=null)Release.volume=Audible(Release.transform.position,700f)?ReleaseVolume:0f;
        }
        void EnsureSources()
        {
            if(Attempted)return;Attempted=true;
            if(!Instances.Contains(this))Instances.Add(this);
            try
            {
                string folder=HumanoidModel.SafePath(HumanoidModel.AssetDirectory,"humanoid_trial/perlica");
                ChargeClip=Load(folder,"audio/perlica_orbital_charge_endfield.wav");
                ReleaseClip=Load(folder,"audio/perlica_orbital_release_endfield.wav");
                Charge=CreateSource("ChargeLoop",ChargeClip,true,ChargeVolume,600f);
                Release=CreateSource("BeamRelease",ReleaseClip,false,ReleaseVolume,700f);
                Runtime.Log("PERLICA_ORBITAL_ENERGY_AUDIO_READY source=originalEndfieldUltimate; spatialBlend=1; newListener=False; globalAudioUnchanged=True");
            }
            catch(Exception error)
            {
                DestroySources();
                Runtime.Log("PERLICA_ORBITAL_ENERGY_AUDIO_DISABLED reason="+error.GetType().Name+": "+error.Message+"; nativeSkillUnaffected=True");
            }
        }
        AudioSource CreateSource(string name,AudioClip clip,bool loop,float volume,float maximum)
        {
            GameObject node=new GameObject("EF_OrbitalEnergy_"+name);node.hideFlags=HideFlags.DontSave;
            node.transform.parent=transform;
            AudioSource source=node.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=loop;
            source.clip=clip;source.spatialBlend=1f;source.dopplerLevel=0f;source.pitch=1f;
            source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=55f;source.maxDistance=maximum;
            source.volume=volume;source.ignoreListenerPause=false;source.ignoreListenerVolume=false;
            return source;
        }
        static AudioClip Load(string folder,string relative)
        {
            string path=HumanoidAttackAudio.SafePath(folder,relative);
            HumanoidAttackAudio.DecodedWave wave=HumanoidAttackAudio.DecodePcm16Wave(HumanoidAttackAudio.ReadBoundedFile(path,4*1024*1024));
            AudioClip clip=AudioClip.Create("EF_OrbitalEnergy_"+Path.GetFileNameWithoutExtension(path),wave.Samples.Length,1,wave.SampleRate,false);
            if(clip==null)throw new InvalidOperationException("Could not create original energy clip");
            if(!clip.SetData(wave.Samples,0)){Destroy(clip);throw new InvalidOperationException("Could not load original energy samples");}
            return clip;
        }
        static void ScanListener()
        {
            if(Valid(Listener)&&Time.realtimeSinceStartup<NextListenerScan)return;
            NextListenerScan=Time.realtimeSinceStartup+.5f;Listener=null;
            foreach(AudioListener item in UnityEngine.Object.FindObjectsOfType(typeof(AudioListener)))if(Valid(item)){Listener=item;break;}
        }
        static bool Valid(AudioListener listener){return listener!=null&&listener.enabled&&listener.gameObject.activeInHierarchy;}
        static bool Audible(Vector3 position,float maximum)
        {return Valid(Listener)&&HumanoidAttackAudio.WithinMaximumDistance(position,Listener.transform.position,maximum);}
        void DestroySources()
        {
            if(Charge!=null){Charge.Stop();Destroy(Charge.gameObject);}if(Release!=null){Release.Stop();Destroy(Release.gameObject);}
            Charge=Release=null;
            if(ChargeClip!=null)Destroy(ChargeClip);if(ReleaseClip!=null)Destroy(ReleaseClip);
            ChargeClip=ReleaseClip=null;
        }
        void ReleaseOwn(){Cancel();DestroySources();Instances.Remove(this);Attempted=false;}
        void OnDisable(){ReleaseOwn();}
        void OnDestroy(){ReleaseOwn();}
        public static void ResetAll(){foreach(PerlicaOrbitalAudio item in Instances.ToArray())if(item!=null)item.ReleaseOwn();Instances.Clear();Listener=null;NextListenerScan=0f;}
        static bool Finite(Vector3 value){return Finite(value.x)&&Finite(value.y)&&Finite(value.z);}
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
    internal sealed class PerlicaOrbitalAudioPlan
    {
        public bool Armed,LaunchPlayed,ArrivedObserved,LandPlayed,Paused;
        public void Designate(){Armed=true;LaunchPlayed=ArrivedObserved=LandPlayed=Paused=false;}
        public bool Launched(){if(!Armed||LaunchPlayed||ArrivedObserved||LandPlayed)return false;LaunchPlayed=true;return true;}
        public bool Arrived(){if(!Armed||ArrivedObserved)return false;ArrivedObserved=true;return true;}
        public bool Detonated(){if(!Armed||LandPlayed)return false;ArrivedObserved=true;LandPlayed=true;return true;}
        public bool SetPaused(bool pause){if(Paused==pause)return false;Paused=pause;return true;}
        public void Cancel(){Armed=LaunchPlayed=ArrivedObserved=LandPlayed=Paused=false;}
    }
}
