using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        internal const string ArtilleryAuto="RI_U09_AUTO",ArtilleryBarrage="RI_U09_BARRAGE",ArtilleryPrecise="RI_U09_PRECISE";
        static readonly Dictionary<string,string> ArtilleryWeaponIDs=new Dictionary<string,string>(StringComparer.Ordinal){
            {"MisslePods_FL",ArtilleryAuto},{"MisslePods_FR",ArtilleryAuto},{"MisslePods_BL",ArtilleryAuto},{"MisslePods_BR",ArtilleryAuto},
            {"MisslePods_Front",ArtilleryAuto},{"MisslePods_Back",ArtilleryAuto},
            {"ARTILLERY_BARRAGE_FL",ArtilleryBarrage},{"ARTILLERY_BARRAGE_FR",ArtilleryBarrage},{"ARTILLERY_BARRAGE_BF",ArtilleryBarrage},{"ARTILLERY_BARRAGE_BR",ArtilleryBarrage},
            {"ARTILLERY_BARRAGE_FL_PRECISE",ArtilleryPrecise},{"ARTILLERY_BARRAGE_FR_PRECISE",ArtilleryPrecise},{"ARTILLERY_BARRAGE_BF_PRECISE",ArtilleryPrecise},{"ARTILLERY_BARRAGE_BR_PRECISE",ArtilleryPrecise}};
        static void PrepareArtillery(Context c)
        {
            Array native=(Array)Read(c.Core,"m_WeaponLoadout");
            string[] first={"MisslePods_FL","ARTILLERY_BARRAGE_FL","ARTILLERY_BARRAGE_FL_PRECISE"};
            string[] own={ArtilleryAuto,ArtilleryBarrage,ArtilleryPrecise};
            Array loadout=Array.CreateInstance(native.GetType().GetElementType(),3);
            Array oldWeapons=(Array)Read(c.Custom,"m_Weapons");Array weapons=Array.CreateInstance(oldWeapons.GetType().GetElementType(),3);
            for(int slot=0;slot<first.Length;slot++)
            {
                object selected=null;foreach(object binding in native)if((string)Read(binding,"m_WeaponID")==first[slot])selected=binding;
                if(selected==null)throw new InvalidOperationException("U09 native artillery mode missing: "+first[slot]);
                object copy=CopyData(selected,new Dictionary<object,object>(new IdentityComparer()));
                Write(copy,"m_WeaponID",own[slot]);Write(copy,"m_TurretIndex",0);Write(copy,"m_TurretDefaultAngleOffsetDegrees",0f);
                ConfigureMediumArtilleryWeapon(Read(Read(copy,"m_Weapon"),"m_Simulation"),slot);
                loadout.SetValue(copy,slot);weapons.SetValue(Read(copy,"m_Weapon"),slot);
            }
            // One physical barrel has one native loadout entry per logical mode.
            // Never stack the donor's four independent pods at this muzzle.
            Write(c.Core,"m_WeaponLoadout",loadout);Write(c.Custom,"m_Weapons",weapons);
            ConfigureMediumArtilleryUnit(Read(c.Core,"m_Attributes"),Read(Read(c.Custom,"m_Movement"),"m_Simulation"));
            foreach(ScriptableObject asset in c.Owned)
            {
                HashSet<object> seen=new HashSet<object>(new IdentityComparer());
                foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")
                    field.SetValue(asset,RewriteArtilleryIDs(field.GetValue(asset),seen));
                // Native toggle status contains six historical pod names. Keep
                // exactly one override for each attribute of the single gun.
                object attributes=null;try{attributes=Read(asset,"m_Attributes");}catch(MissingMemberException){}
                if(attributes!=null)
                {
                    Array buffs=null;try{buffs=Read(attributes,"m_Buffs") as Array;}catch(MissingMemberException){}
                    if(buffs!=null)
                    {
                        List<object> reduced=new List<object>();HashSet<string> unique=new HashSet<string>(StringComparer.Ordinal);
                        foreach(object buff in buffs)
                        {
                            string key=(string)Read(buff,"m_Name");
                            if(key==ArtilleryAuto||key==ArtilleryBarrage||key==ArtilleryPrecise)
                            {string signature=key+":"+Read(buff,"m_AttributeID")+":"+Read(buff,"m_Mode");if(!unique.Add(signature))continue;}
                            // Other donor-only weapon scopes have no counterpart.
                            else if(key=="Missile_Front"||key=="Missile_Back"||key=="ARTILLERY_BARRAGE")continue;
                            reduced.Add(buff);
                        }
                        Array output=Array.CreateInstance(buffs.GetType().GetElementType(),reduced.Count);
                        for(int i=0;i<reduced.Count;i++)output.SetValue(reduced[i],i);Write(attributes,"m_Buffs",output);
                        ConfigureMediumArtilleryBuffs(attributes,asset.name);
                    }
                    ConfigureMediumArtilleryAbilityOrStatus(attributes,asset.name);
                }
            }
            PrivatizeArtilleryCommands(c);
            Runtime.Log("RHODES_U09_ARTILLERY_PREPARED nativeBallisticDonor=C_ArtilleryCruiser_MP; privateMediumProfile=True; physicalTurrets=1; physicalMuzzles=1; logicalModes=3; roundsPerBurst=1; bursts=1; damage=160; AoERadius=80; range=450_to_1500; nominalCycleMS=6000; HP=600; armour=2; maxSpeed=70; CU=220; RU=80; seconds=24; population=3; pointDefence=0; nativeProjectilesRetained=True; privateToggleClosure=True; scaledVeterancy=True; runtimeAcceptancePending=True");
        }
        static object RewriteArtilleryIDs(object value,HashSet<object> seen)
        {
            if(value==null)return null;string text=value as string;
            if(text!=null)
            {
                string mapped;if(ArtilleryWeaponIDs.TryGetValue(text,out mapped))return mapped;
                // Numbered abilities are the donor's chained extra pod casts.
                // The single tube fires the first native sequence only.
                if(text.StartsWith("Ability_C_ArtilleryBarrage_",StringComparison.Ordinal)&&
                    (text.EndsWith("_2",StringComparison.Ordinal)||text.EndsWith("_3",StringComparison.Ordinal)||text.EndsWith("_4",StringComparison.Ordinal)))return string.Empty;
                return text;
            }
            Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return value;
            Array array=value as Array;if(array!=null){for(int i=0;i<array.Length;i++)array.SetValue(RewriteArtilleryIDs(array.GetValue(i),seen),i);return array;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo field in Fields(type))field.SetValue(value,RewriteArtilleryIDs(field.GetValue(value),seen));
            return value;
        }
        static void ConfigureArtilleryTurret(Context c,UnitViewAttributes view)
        {
            if(view.TurretDefinitions==null||view.TurretDefinitions.Length<4)throw new InvalidOperationException("U09 expected native artillery pod definitions");
            UnitViewAttributes.TurretAttributes turret=(UnitViewAttributes.TurretAttributes)CopyData(view.TurretDefinitions[0],new Dictionary<object,object>(new IdentityComparer()));
            turret.BoneName="EF_Faction_ArtilleryYaw";turret.PitchBoneName="EF_Faction_ArtilleryPitch";turret.SimTurretOrigin=turret.BoneName;
            turret.CenterEmissionBone="EF_Faction_Muzzle";turret.FXEmissionBones=new string[]{turret.CenterEmissionBone};
            turret.PitchMin=0f;turret.PitchMax=72f;turret.MaxPitchDegreesPerSecond=90f;turret.MaxRollDegreesPerSecond=0f;
            turret.StateAnimationTriggers=new UnitViewAttributes.TurretOperatingAnimationTrigger[0];
            view.TurretDefinitions=new UnitViewAttributes.TurretAttributes[]{turret};
            ConfigureMediumArtilleryDeath(c);
        }
        static void ConfigureMediumArtilleryDeath(Context c)
        {
            if(c.Spec.Key!=ArtilleryKey)throw new InvalidOperationException("Medium artillery death profile is U09-only");
            ScriptableObject ownAsset=Read(c.Custom,"m_Death") as ScriptableObject;
            ScriptableObject originalAsset=Read(c.Source,"m_Death") as ScriptableObject;
            ScriptableObject donorAsset=Read(FindSource("C_HAC_MP"),"m_Death") as ScriptableObject;
            if(ownAsset==null||originalAsset==null||donorAsset==null||!c.Owned.Contains(ownAsset)||
                object.ReferenceEquals(ownAsset,originalAsset)||object.ReferenceEquals(ownAsset,donorAsset))
                throw new InvalidOperationException("U09 death asset is not privately owned");
            UnitDeathAttributes own=Read(ownAsset,"m_Attributes") as UnitDeathAttributes;
            UnitDeathAttributes original=Read(originalAsset,"m_Attributes") as UnitDeathAttributes;
            UnitDeathAttributes donor=Read(donorAsset,"m_Attributes") as UnitDeathAttributes;
            if(own==null||original==null||donor==null||donor.DeathAttributes==null||
                object.ReferenceEquals(own,original)||object.ReferenceEquals(own,donor)||
                object.ReferenceEquals(own.DeathAttributes,original.DeathAttributes)||object.ReferenceEquals(own.DeathAttributes,donor.DeathAttributes))
                throw new InvalidOperationException("U09 death data aliases a native source");
            UnitDeathAttributes.UnitDestructionAttributes source=donor.DeathAttributes;
            if(source.CarcassPrefab==null||source.CarcassPrefab.name!="Carcass_C_HAC"||
                source.InitialBurstPrefab==null||source.InitialBurstPrefab.name!="FX_PRFB_HAC_PreFlash"||
                source.DeathPingPrefab==null||source.DeathPingPrefab.name!="DeathPing")
                throw new InvalidOperationException("Native HAC death presentation differs from the audited medium profile");
            string originalFingerprint=Fingerprint(originalAsset),donorFingerprint=Fingerprint(donorAsset);
            bool originalRecorded=false,donorRecorded=false;
            foreach(SourceRecord record in c.Sources)
            {
                if(object.ReferenceEquals(record.Asset,originalAsset)){originalRecorded=true;if(record.Fingerprint!=originalFingerprint)throw new InvalidOperationException("Native artillery death source drifted");}
                if(object.ReferenceEquals(record.Asset,donorAsset)){donorRecorded=true;if(record.Fingerprint!=donorFingerprint)throw new InvalidOperationException("Native HAC death source drifted");}
            }
            if(!originalRecorded)throw new InvalidOperationException("Original artillery death fingerprint was not retained");
            if(!donorRecorded)c.Sources.Add(new SourceRecord{Asset=donorAsset,Fingerprint=donorFingerprint});
            UnitDeathAttributes.GroundUnitDestructionAttributes ground=own.GroundUnitDestruction;
            UnitDeathAttributes.UnitDestructionAttributes copy=(UnitDeathAttributes.UnitDestructionAttributes)CopyData(source,new Dictionary<object,object>(new IdentityComparer()));
            if(object.ReferenceEquals(copy,source)||copy.CarcassPrefab!=source.CarcassPrefab||copy.InitialBurstPrefab!=source.InitialBurstPrefab||
                copy.DeathPingPrefab!=source.DeathPingPrefab||copy.CannotSkipDeathSequence!=source.CannotSkipDeathSequence||
                copy.ModelSwapDelayTime!=source.ModelSwapDelayTime||copy.ModelSwapAdditionalRandomDelay!=source.ModelSwapAdditionalRandomDelay||
                copy.InitialBurstParentedToUnit!=source.InitialBurstParentedToUnit)
                throw new InvalidOperationException("U09 medium death presentation was not copied exactly");
            // This nested object contains only seven presentation fields. Keep
            // the private ground rig: HAC's legacy wheel names do not exist on
            // the authored U09. Never modify any referenced native FX prefab.
            own.DeathAttributes=copy;
            if(!object.ReferenceEquals(own.GroundUnitDestruction,ground)||Fingerprint(originalAsset)!=originalFingerprint||Fingerprint(donorAsset)!=donorFingerprint)
                throw new InvalidOperationException("U09 death preparation changed ground rig or native source data");
            // Native UnitDeathAttributesAsset.Data directly returns m_Attributes.
            // RegisterOne clears the private UnitAssetContainer aggregate cache
            // after this call, before its final OnEnable and registry insertion.
            Runtime.Log("RHODES_U09_MEDIUM_DEATH_PREPARED donor=C_HAC_MP; carcass=Carcass_C_HAC; burst=FX_PRFB_HAC_PreFlash; ping=DeathPing; privateDeathFields=7; nativeMediumPrefabScale=True; groundRigUnchanged=True; nativeSourcesUnchanged=True; damageDropsScoringUnchanged=True; visualAcceptancePending=True");
        }
        static void VerifyArtillery(Context c)
        {
            UnitAttributes unit=c.Definition.Get<UnitAttributes>();UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");
            if(unit==null||unit.WeaponLoadout.Length!=3||view.TurretDefinitions==null||view.TurretDefinitions.Length!=1)
                throw new InvalidOperationException("U09 must have three modes and exactly one physical turret");
            string[] keys={ArtilleryAuto,ArtilleryBarrage,ArtilleryPrecise};
            for(int i=0;i<keys.Length;i++)if(unit.WeaponLoadout[i].WeaponID!=keys[i]||unit.WeaponLoadout[i].TurretIndex!=0)
                throw new InvalidOperationException("U09 private single-barrel weapon binding drift");
            int modes=0,toggles=0;
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                object data=Read(ability,"m_Attributes");string weapon=(string)Read(Read(data,"m_UseWeapon"),"m_WeaponID");
                if(ability.name==ArtilleryBarrageAbility||ability.name==ArtilleryPreciseAbility)
                {if(weapon!=(ability.name==ArtilleryBarrageAbility?ArtilleryBarrage:ArtilleryPrecise))throw new InvalidOperationException("U09 ability uses a donor pod");modes++;}
                if(ability.name==ArtilleryToggleAbility)toggles++;
            }
            if(modes!=2||toggles!=1)throw new InvalidOperationException("U09 native fire modes or auto-fire toggle missing");
            VerifyArtilleryGlobalCommands(c);
            VerifyMediumArtilleryProfile(c.Definition,0);
            VerifyMediumArtilleryOwnedBuffs(c);
        }
    }
}
