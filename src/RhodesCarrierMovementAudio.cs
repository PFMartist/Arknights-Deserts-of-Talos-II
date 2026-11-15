using System;
using System.Collections.Generic;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        // Sound-only carrier definitions are native UnitView frills. Keeping the
        // original Sound/FXType/bone/LOD lets its engine updater, polyphony and
        // Fabric hierarchy retain rpm, distance, visibility and pause handling.
        // No custom audio source, update loop, animation trigger or visual FX.
        static readonly string[] MovementAudioNames={"SFX - Engine","SFX - Engine Start Up","SFX - Braking"};
        static readonly string[] MovementAudioSounds={"sfx/engines/coal/units/CommandCarrier","sfx/engines/coal/start/Carrier","sfx/engines/coal/brake/Carrier"};
        static readonly int[] MovementAudioFrills={540,550,220};
        const string MovementAudioBone="RIG_Carr_ROOT";

        static EffectDefinition[] CopyNativeMovementAudio(UnitViewAttributes native)
        {
            EffectDefinition[] source=FindNativeMovementAudio(native);
            EffectDefinition[] result=new EffectDefinition[source.Length];
            Dictionary<object,object> copies=new Dictionary<object,object>(new IdentityComparer());
            for(int index=0;index<source.Length;index++)
            {
                result[index]=(EffectDefinition)CopyData(source[index],copies);
                if(object.ReferenceEquals(result[index],source[index])||object.ReferenceEquals(result[index].BoneRef,source[index].BoneRef))
                    throw new InvalidOperationException("Rhodes movement audio data aliases native carrier");
                CheckMovementAudioDefinition(result[index],index);
            }
            Runtime.Log("RHODES_MOVEMENT_AUDIO_PREPARED donor=C_Carrier_MP; soundOnlyDefinitions=3; engineRpmAndIdle=Native; startAndBrake=Native; shutdownSound=NotAuthoredByDonor; emitter="+MovementAudioBone+
                "; visualFXRestored=False; nativeFabricHierarchyAndPausePreset=True; perFrameDriverAdded=False; runtimeListeningAcceptancePending=True");
            return result;
        }

        static EffectDefinition[] FindNativeMovementAudio(UnitViewAttributes native)
        {
            if(native==null||native.UnitEffectDefs==null)throw new InvalidOperationException("Native carrier movement audio definitions missing");
            EffectDefinition[] result=new EffectDefinition[MovementAudioNames.Length];
            foreach(EffectDefinition value in native.UnitEffectDefs)
            {
                if(value==null)continue;
                int index=Array.IndexOf(MovementAudioNames,value.Name);
                if(index<0)continue;
                if(result[index]!=null)throw new InvalidOperationException("Duplicate native carrier movement audio: "+value.Name);
                CheckMovementAudioDefinition(value,index);result[index]=value;
            }
            for(int index=0;index<result.Length;index++)
                if(result[index]==null)throw new InvalidOperationException("Native carrier movement audio absent: "+MovementAudioNames[index]);
            return result;
        }

        static void CheckMovementAudioDefinition(EffectDefinition value,int index)
        {
            if(value==null||value.Name!=MovementAudioNames[index]||value.Sound!=MovementAudioSounds[index]||(int)value.FXType!=MovementAudioFrills[index]||
                value.FXPrefab!=null||(int)value.ActiveAtLOD!=15||value.PolyGroup!=string.Empty||value.BoneRef==null||value.BoneRef.Length!=1||value.BoneRef[0]!=MovementAudioBone||
                (int)value.RaycastDir!=0||value.FollowNLIPSScale||value.InheritRaycastNormal)
                throw new InvalidOperationException("Native carrier sound-only contract changed: "+MovementAudioNames[index]);
        }

        // Registration verification only. Native UnitView owns live emitters,
        // movement/stop/death/visibility/reset, parameters and mixer transitions.
        static void VerifyMovementAudio(UnitViewAttributes view,UnitViewAttributes native)
        {
            EffectDefinition[] source=FindNativeMovementAudio(native);
            if(view.UnitEffectDefs==null||view.UnitEffectDefs.Length<source.Length||object.ReferenceEquals(view.UnitEffectDefs,native.UnitEffectDefs))
                throw new InvalidOperationException("Rhodes movement audio whitelist is incomplete or shared");
            for(int index=0;index<source.Length;index++)
            {
                CheckMovementAudioDefinition(view.UnitEffectDefs[index],index);
                if(object.ReferenceEquals(view.UnitEffectDefs[index],source[index])||object.ReferenceEquals(view.UnitEffectDefs[index].BoneRef,source[index].BoneRef))
                    throw new InvalidOperationException("Rhodes movement audio shares mutable native definition data");
            }
            int groundCount=NativeGroundEffects54.VerifyCarrierPrefix(view,native,source.Length);
            int trackCount=NativeGroundTracks55.VerifyCarrierPrefix(view,native,source.Length+groundCount);
            int statusCount=NativeGroundStatuses55.VerifyCarrierPrefix(view,native,source.Length+groundCount+trackCount);
            int nativeLightCount=RhodesNativeLightFactory.VerifySuffix(view,source.Length+groundCount+trackCount+statusCount);
            RhodesBeaconFactory.VerifySuffix(view,source.Length+groundCount+trackCount+statusCount+nativeLightCount);
            int emitters=0;
            foreach(Transform bone in view.ModelPrefab.GetComponentsInChildren<Transform>(true))
                if(bone.name==MovementAudioBone)emitters++;
            if(emitters!=1)throw new InvalidOperationException("Rhodes native movement audio emitter missing or ambiguous: "+emitters);
        }
    }
}
