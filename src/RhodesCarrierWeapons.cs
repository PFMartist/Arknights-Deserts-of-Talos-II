using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Events;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // OFFLINE CANDIDATE. Requires the V05 articulated prefab contract described
    // in WEAPONS_HANDOFF.md. Never clears an existing loadout or native asset.
    public static partial class RhodesCarrierUnits
    {
        public const string MainForeWeaponID="EF_RHODES_MAIN_FORE";
        public const string MainAftWeaponID="EF_RHODES_MAIN_AFT";
        public static string[] MainWeaponIDs { get { return new string[]{MainForeWeaponID,MainAftWeaponID}; } }
        public static string[] CiwsWeaponIDs { get { return new string[]{"EF_RHODES_CIWS_PF","EF_RHODES_CIWS_PA","EF_RHODES_CIWS_SF","EF_RHODES_CIWS_SA"}; } }
        const float CiwsFieldOfViewDegrees=220f;
        static float CiwsDefaultYawDegrees(int station)
        {
            switch(station){case 0:return 60f;case 1:return 120f;case 2:return -60f;case 3:return -120f;}
            throw new ArgumentOutOfRangeException("station");
        }

        // Invoke after Configure(c) has produced the PRIVATE V05 prefab and
        // before final RemapAsset/OnEnable. Both main guns start disabled; the
        // native passive unlock status supplied by research enables them.
        static void PrepareWeapons(Context c)
        {
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");
            if(view.ModelPrefab==null)throw new InvalidOperationException("Rhodes weapons require the private V05 prefab first");
            if(view.ModelPrefab==c.NativePrefab)throw new InvalidOperationException("Refusing to bind weapons to a native carrier prefab");
            RhodesWeaponPresentationBinding binding=view.ModelPrefab.GetComponent<RhodesWeaponPresentationBinding>();
            if(binding!=null)throw new InvalidOperationException("Rhodes weapons already prepared on this prefab");
            binding=view.ModelPrefab.AddComponent<RhodesWeaponPresentationBinding>();
            binding.LeftPitch=new Transform[2];binding.RightPitch=new Transform[2];
            binding.LeftPitchRest=new Quaternion[2];binding.RightPitchRest=new Quaternion[2];
            binding.Recoil=new Transform[4];binding.RecoilRest=new Vector3[4];
            binding.Muzzle=new Transform[4];binding.MainIDs=MainWeaponIDs;
            binding.MaximumRecoilMetres=.8f*Settings.Length/600f;

            WeaponAttributesAsset main=PrivateRhodesWeapon(c,"C_Battlecruiser_Weapon_G2G_MP",UnitKey+"_Weapon_Main");
            object sim=Read(main,"m_Simulation");
            Write(sim,"m_BaseDamagePerRound",300);Write(sim,"m_AreaOfEffectMeters",35f);
            // Native burst timing/projectile/FX remain the cruiser donor's.
            // Presentation alternates its two muzzle markers, never damage.
            Write(sim,"m_ExcludeFromAutoTargetAcquisition",false);Write(sim,"m_ExcludeFromAutoFire",false);
            Write(sim,"m_FiringRecoil",0f);Write(sim,"m_LineOfSightRequired",true);
            Write(sim,"m_TargetStyle",WeaponTargetStyle.RandomSpray);
            SetRhodesTargetExclusion(sim,UnitClass.Air);
            SetRhodesRanges(sim,350,new int[]{600,1100,CarrierMainBatteryCombatRange},new float[]{80f,70f,60f});
            Write(Read(sim,"m_Turret"),"m_FieldOfView",270f);
            Write(Read(sim,"m_Turret"),"m_FieldOfFire",3f);
            Write(Read(sim,"m_Turret"),"m_RotationSpeed",24f);
            ((WeaponViewAttributes)Read(main,"m_View")).EmissionStrategy=WeaponEffectEmissionStrategy.Sequential;
            WeaponAttributesAsset aft=CloneMainWeaponSibling(c,main,UnitKey+"_Weapon_Main_Aft_LowCharge");
            Write(Read(aft,"m_Simulation"),"m_BaseDamagePerRound",300);

            string[] stations={"FORE","AFT"};
            for(int station=0;station<stations.Length;station++)
            {
                string suffix=stations[station];string yaw="V03_PIVOT_GN_Yaw_"+suffix;
                string left="V03_PIVOT_GN_Pitch_"+suffix+"_LEFT",right="V03_PIVOT_GN_Pitch_"+suffix+"_RIGHT";
                RequireRhodesWeaponBone(view.ModelPrefab,yaw);
                Transform lp=RequireRhodesWeaponBone(view.ModelPrefab,left),rp=RequireRhodesWeaponBone(view.ModelPrefab,right);
                // Keep the canonical source rest pose unchanged. The one
                // default-angle source is the native WeaponAssetBinding below;
                // UnitView.TurretData sets an ABSOLUTE initial world rotation.
                // A second prefab yaw assignment would not be arithmetically
                // added, but would alter native rest/cache branching needlessly.
                binding.LeftPitch[station]=lp;binding.RightPitch[station]=rp;
                binding.LeftPitchRest[station]=lp.localRotation;binding.RightPitchRest[station]=rp.localRotation;
                string[] emit={"V03_HP_GN_Muzzle_"+suffix+"_LEFT","V03_HP_GN_Muzzle_"+suffix+"_RIGHT"};
                for(int side=0;side<2;side++)
                {
                    int index=station*2+side;string sideName=side==0?"LEFT":"RIGHT";
                    binding.Muzzle[index]=RequireRhodesWeaponBone(view.ModelPrefab,emit[side]);
                    binding.Recoil[index]=RequireRhodesWeaponBone(view.ModelPrefab,"V03_PIVOT_GN_Recoil_"+suffix+"_"+sideName);
                    binding.RecoilRest[index]=binding.Recoil[index].localPosition;
                }
                string center="EF_RHODES_MAIN_"+suffix+"_CENTER";
                GameObject centerObject=new GameObject(center);centerObject.layer=lp.gameObject.layer;
                centerObject.transform.parent=lp;
                centerObject.transform.position=(binding.Muzzle[station*2].position+binding.Muzzle[station*2+1].position)*.5f;
                centerObject.transform.rotation=binding.Muzzle[station*2].rotation;
                centerObject.transform.localScale=Vector3.one;
                int turret=AppendRhodesTurret(c,RhodesTurret(yaw,left,yaw,center,emit,-25f,30f,18f));
                AppendRhodesWeapon(c,station==0?main:aft,MainWeaponIDs[station],turret,0f,true);
            }

            WeaponAttributesAsset ciws=PrivateRhodesWeapon(c,"G_Carrier_Weapon_PD_Enhanced_MP",UnitKey+"_Weapon_CIWS");
            sim=Read(ciws,"m_Simulation");
            Write(sim,"m_BaseDamagePerRound",6);Write(sim,"m_AreaOfEffectMeters",0f);
            Write(sim,"m_RateOfFire",2);Write(sim,"m_NumberOfBursts",4);Write(sim,"m_DamagePacketsPerShot",1);
            Write(sim,"m_BurstPeriodMinTime",1200);Write(sim,"m_BurstPeriodMaxTime",1600);
            Write(sim,"m_CooldownTime",750);Write(sim,"m_ReloadTime",1500);
            Write(sim,"m_WindUpTime",250);Write(sim,"m_WindDownTime",500);
            Write(sim,"m_ExcludeFromAutoTargetAcquisition",false);Write(sim,"m_ExcludeFromAutoFire",false);
            Write(sim,"m_FiringRecoil",0f);Write(sim,"m_LineOfSightRequired",true);
            // The source serializes TargetStyle=2, which is not a declared enum
            // in this build. Use the defined native tracer style explicitly.
            Write(sim,"m_TargetStyle",WeaponTargetStyle.TurretImpactRun);
            // The native donor already uses unrestricted air/ground targeting
            // and the default target-priority weights. Remove our former
            // air-only exclusion on this private clone, keeping native enemy,
            // visibility, line-of-sight and outward-arc checks in authority.
            Array ciwsModifiers=Read(sim,"m_Modifiers") as Array;
            if(ciwsModifiers==null)throw new InvalidOperationException("Rhodes CIWS modifier contract missing");
            Write(sim,"m_Modifiers",Array.CreateInstance(ciwsModifiers.GetType().GetElementType(),0));
            SetRhodesRanges(sim,0,new int[]{250,500,750},new float[]{70f,60f,50f});
            Write(Read(sim,"m_Turret"),"m_FieldOfView",CiwsFieldOfViewDegrees);
            Write(Read(sim,"m_Turret"),"m_FieldOfFire",6f);
            Write(Read(sim,"m_Turret"),"m_RotationSpeed",180f);
            PrepareCiwsTargetPriorities(c,ciws);
            string[] ciwsStations={"PortFore","PortAft","StarboardFore","StarboardAft"};
            for(int station=0;station<ciwsStations.Length;station++)
            {
                string s=ciwsStations[station],yaw="PIVOT_CIWS_"+s+"_Yaw",pitch="PIVOT_CIWS_"+s+"_Pitch",muzzle="HP_CIWS_"+s+"_Muzzle";
                RequireRhodesWeaponBone(view.ModelPrefab,yaw);RequireRhodesWeaponBone(view.ModelPrefab,pitch);RequireRhodesWeaponBone(view.ModelPrefab,muzzle);
                int turret=AppendRhodesTurret(c,RhodesTurret(yaw,pitch,yaw,muzzle,new string[]{muzzle},-65f,80f,100f));
                // These are native simulation offsets, not Unity yaw.
                // WeaponFireOperation rotates CCW in its XY plane, while
                // SimOrientationToUnityOrientation maps yaw=90-angle. Thus
                // port (-modelX) uses positive angles. The port fore sector
                // follows the requested -30..150 baseline with twenty degrees
                // of margin at each end: -50..170. Aft is 10..230; mirror
                // these on starboard. Keep overlap where the pair changes.
                // Native line-of-sight still rejects occluded targets; nearby
                // coverage also depends on each station's physical position.
                AppendRhodesWeapon(c,ciws,CiwsWeaponIDs[station],turret,CiwsDefaultYawDegrees(station),true);
            }
            object actuation=Read(view.LODTunings,"TurretActuationActiveLODs");
            Write(view.LODTunings,"TurretActuationActiveLODs",Enum.ToObject(actuation.GetType(),15));
            Runtime.Log("RHODES_WEAPONS_PREPARED mainBindings=2; ciwsBindings=4; nativeDamage=True; nativeTurretActuation=True; mainDisabledUntilNativeResearchStatus=True; CIWSAirAndGroundRequiresPowerLevel2=True; CIWSPitch=-65..80; CIWSOutwardFOV=220; CIWSDefaultYaw=60,120,-60,-120; CIWSNativePowerProfile28=True; antiMissileNotClaimed=True; mainDefaultYaw=0; mainForwardFOV=270; pitchReviewRange=-25..30; mainMinimumRange=350; mainBaseMaximumRange=1600; independentAftAndChargeRangeIncrease=200Each; nativeBattlecruiserTimingProjectileFXAudioRetained=True; sourceAssetsUnchangedCheckPending=True; runtimeAcceptancePending=True");
        }
        static WeaponAttributesAsset CloneMainWeaponSibling(Context c,WeaponAttributesAsset source,string name)
        {
            WeaponAttributesAsset clone=(WeaponAttributesAsset)RhodesAssetClone59.CreateEmpty(source.GetType());
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(source),copied));
            clone.name=name;clone.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(clone);
            ((BBI.Core.NamedObjectBase)Read(clone,"m_Simulation")).InitializeName(name);
            ((BBI.Core.NamedObjectBase)Read(clone,"m_View")).InitializeName(name);
            return clone;
        }

        static UnitViewAttributes.TurretAttributes RhodesTurret(string yaw,string pitch,string origin,string center,string[] muzzle,float min,float max,float speed)
        {
            UnitViewAttributes.TurretAttributes value=new UnitViewAttributes.TurretAttributes();
            value.BoneName=yaw;value.PitchBoneName=pitch;value.SimTurretOrigin=origin;
            value.CenterEmissionBone=center;value.FXEmissionBones=(string[])muzzle.Clone();
            value.PitchMin=min;value.PitchMax=max;value.MaxPitchDegreesPerSecond=speed;
            value.MaxRollDegreesPerSecond=0f;value.StateAnimationTriggers=new UnitViewAttributes.TurretOperatingAnimationTrigger[0];
            return value;
        }

        static WeaponAttributesAsset PrivateRhodesWeapon(Context c,string donorName,string customName)
        {
            WeaponAttributesAsset source=null;
            foreach(WeaponAttributesAsset item in Resources.FindObjectsOfTypeAll(typeof(WeaponAttributesAsset)))
                if(item!=null&&item.name==donorName){
                    EntityTypeAttributes native=Registry.GetEntityType(donorName);
                    if(native!=null&&!object.ReferenceEquals(native.Get<WeaponAttributes>(),Read(item,"m_Simulation")))continue;
                    if(source!=null&&!object.ReferenceEquals(source,item))throw new InvalidOperationException("Ambiguous Rhodes weapon donor "+donorName);
                    source=item;
                }
            // This upgrade-only donor is in Resources, outside the startup
            // registry's loaded asset closure. Load only its verified path;
            // never load an entire faction or insert its source into Registry.
            if(source==null&&donorName=="G_Carrier_Weapon_PD_Enhanced_MP")
            {
                source=Resources.Load("units/gaalsi/g_carrier_mp/g_carrier_weapon_pd_enhanced_mp",typeof(WeaponAttributesAsset)) as WeaponAttributesAsset;
                if(source!=null)
                {
                    EntityTypeAttributes native=Registry.GetEntityType(donorName);
                    if(source.name!=donorName||(native!=null&&!object.ReferenceEquals(native.Get<WeaponAttributes>(),Read(source,"m_Simulation"))))
                        throw new InvalidOperationException("Explicit Rhodes weapon resource identity mismatch: "+donorName);
                    Runtime.Log("RHODES_WEAPON_RESOURCE_LOADED donor="+donorName+"; exactWhitelistedResource=True; nativeRegistryWrite=False; privateCloneFollows=True");
                }
            }
            if(source==null)throw new InvalidOperationException("Rhodes native weapon donor not loaded: "+donorName);
            WeaponAttributesAsset clone=(WeaponAttributesAsset)CloneAsset(c,source,customName);
            ScriptableObject priority=Read(source,"m_TargetPriorization") as ScriptableObject;
            if(priority!=null)CloneAsset(c,priority,UnitKey+"_WeaponPriorities");
            if(object.ReferenceEquals(Read(source,"m_Simulation"),Read(clone,"m_Simulation"))||object.ReferenceEquals(Read(source,"m_View"),Read(clone,"m_View")))
                throw new InvalidOperationException("Rhodes weapon data still aliases native donor "+donorName);
            return clone;
        }

        static void ConfigureCiwsTargetPriorityData(object data)
        {
            // Native target cost is minimized: this is a negative reward for
            // an in-arc target, not a new class filter or targeting processor.
            Write(data,"m_TargetWithinFOVBias",5000f);
            Write(data,"m_AutoTargetStickyBias",250f);
            Write(data,"m_ManualTargetStickyBias",1750f);
        }
        static void PrepareCiwsTargetPriorities(Context c,WeaponAttributesAsset weapon)
        {
            ScriptableObject source=Read(weapon,"m_TargetPriorization") as ScriptableObject;
            if(source==null)throw new InvalidOperationException("Rhodes CIWS target priorities missing");
            // Main guns can use the same native priority source. Do not call
            // CloneAsset or replace its Context.Map entry: that would remap
            // the main guns to this CIWS-only policy on the final asset pass.
            ScriptableObject own=RhodesAssetClone59.CreateEmpty(source.GetType());
            own.name=UnitKey+"_CIWS_TargetPriorities";own.hideFlags=HideFlags.HideAndDontSave;
            Dictionary<object,object> copies=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")
                field.SetValue(own,CopyData(field.GetValue(source),copies));
            c.Owned.Add(own);
            bool recorded=false;foreach(SourceRecord record in c.Sources)if(object.ReferenceEquals(record.Asset,source)){recorded=true;break;}
            if(!recorded)c.Sources.Add(new SourceRecord{Asset=source,Fingerprint=Fingerprint(source),Definition=Registry.GetEntityType(source.name)});
            ConfigureCiwsTargetPriorityData(Read(own,"m_Attributes"));
            Write(weapon,"m_TargetPriorization",own);
        }
        static void VerifyCiwsTargetPriorityData(TargetPriorizationAttributes data)
        {
            if(data==null||data.TargetWithinFOVBias!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(5000)||
                data.AutoTargetStickyBias!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(250)||
                data.ManualTargetStickyBias!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(1750)||
                data.WeaponEffectivenessWeight!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(5)||
                data.DistanceWeight!=BBI.Core.Utility.FixedPoint.Fixed64.FromConstFloat(.5f)||
                data.AngleWeight!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(25)||
                data.TargetPriorityWeight!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(1)||
                data.TargetThreatWeight!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(7)||
                data.TargetSameCommanderBias!=BBI.Core.Utility.FixedPoint.Fixed64.Zero)
                throw new InvalidOperationException("Rhodes private CIWS target priorities differ");
        }

        static void SetRhodesTargetExclusion(object simulation,UnitClass excluded)
        {
            Array prior=Read(simulation,"m_Modifiers") as Array;
            if(prior==null)throw new InvalidOperationException("Native weapon modifier array contract missing");
            Type type=prior.GetType().GetElementType();object modifier=Activator.CreateInstance(type,true);
            Write(modifier,"m_TargetClass",excluded);Write(modifier,"m_ClassOperator",FlagOperator.Or);
            Write(modifier,"m_Modifier",WeaponModifierType.CanNotTarget);Write(modifier,"m_Amount",0);
            Array values=Array.CreateInstance(type,1);values.SetValue(modifier,0);Write(simulation,"m_Modifiers",values);
        }
        static void SetRhodesRanges(object simulation,int min,int[] distances,float[] accuracy)
        {
            Array bands=Read(simulation,"m_Ranges") as Array;
            if(bands==null||bands.Length!=distances.Length||accuracy.Length!=distances.Length)
                throw new InvalidOperationException("Unexpected Rhodes donor range-band contract");
            for(int i=0;i<bands.Length;i++){
                object band=bands.GetValue(i);Write(band,"m_MinDistance",min);Write(band,"m_Distance",distances[i]);Write(band,"m_Accuracy",accuracy[i]);
            }
        }

        // Shared with support module; callers must use the returned index.
        // Never assumes that repair/main/CIWS have a particular append order.
        static int AppendRhodesTurret(Context c,UnitViewAttributes.TurretAttributes definition)
        {
            if(definition==null||string.IsNullOrEmpty(definition.SimTurretOrigin))throw new ArgumentException("Rhodes turret origin is required");
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");
            UnitViewAttributes.TurretAttributes[] old=view.TurretDefinitions??new UnitViewAttributes.TurretAttributes[0];
            UnitViewAttributes.TurretAttributes[] next=new UnitViewAttributes.TurretAttributes[old.Length+1];Array.Copy(old,next,old.Length);next[old.Length]=definition;view.TurretDefinitions=next;
            return old.Length;
        }
        static void AppendRhodesWeapon(Context c,WeaponAttributesAsset weapon,string bindingID,int turretIndex,float defaultYawDegrees,bool disabledOnSpawn)
        {
            if(weapon==null||!c.Owned.Contains(weapon)||string.IsNullOrEmpty(bindingID))throw new ArgumentException("Rhodes weapon must be a private owned clone with an ID");
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");
            if(turretIndex<0||view.TurretDefinitions==null||turretIndex>=view.TurretDefinitions.Length)throw new ArgumentOutOfRangeException("turretIndex");
            Array old=Read(c.Core,"m_WeaponLoadout") as Array;
            if(old==null)throw new InvalidOperationException("Rhodes core weapon loadout has no typed array");
            foreach(object item in old)if((string)Read(item,"m_WeaponID")==bindingID)throw new InvalidOperationException("Duplicate Rhodes weapon binding "+bindingID);
            object created=Activator.CreateInstance(old.GetType().GetElementType(),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                new object[]{bindingID,weapon,"",turretIndex,defaultYawDegrees,disabledOnSpawn},CultureInfo.InvariantCulture);
            Array next=Array.CreateInstance(old.GetType().GetElementType(),old.Length+1);Array.Copy(old,next,old.Length);next.SetValue(created,old.Length);Write(c.Core,"m_WeaponLoadout",next);
            WeaponAttributesAsset[] extra=Read(c.Custom,"m_Weapons") as WeaponAttributesAsset[];
            List<WeaponAttributesAsset> weapons=new List<WeaponAttributesAsset>(extra??new WeaponAttributesAsset[0]);
            if(!weapons.Contains(weapon))weapons.Add(weapon);Write(c.Custom,"m_Weapons",weapons.ToArray());
        }

        // Invoke after final private OnEnable passes, before unit registration.
        // Includes EF_RHODES_REPAIR supplied by the support module.
        static void FinishWeapons(Context c)
        {
            WeaponAttributesAsset[] assets=Read(c.Custom,"m_Weapons") as WeaponAttributesAsset[];
            if(assets==null)throw new InvalidOperationException("Rhodes private weapon registry list missing");
            List<EntityTypeAttributes> definitions=new List<EntityTypeAttributes>();
            foreach(WeaponAttributesAsset asset in assets)
            {
                if(asset==null||!c.Owned.Contains(asset))throw new InvalidOperationException("Rhodes weapon registry contains a shared/native asset");
                if(Registry.GetEntityType(asset.name)!=null)throw new InvalidOperationException("Rhodes weapon registry key collision "+asset.name);
                EntityTypeAttributes definition=new EntityTypeAttributes(asset.name);
                foreach(object data in (IEnumerable)Read(asset,"Data"))if(data!=null&&!definition.Add(data))throw new InvalidOperationException("Duplicate Rhodes weapon datum "+asset.name);
                definitions.Add(definition);
            }
            VerifyWeapons(c,false);
            foreach(EntityTypeAttributes definition in definitions)Registry.Add(definition);
            VerifyWeapons(c,true);
        }
        static void VerifyWeapons(Context c,bool registered)
        {
            UnitAttributes core=(UnitAttributes)Read(c.Core,"m_Attributes");
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");
            Array serialized=Read(c.Core,"m_WeaponLoadout") as Array;
            if(serialized==null||core.WeaponLoadout==null||core.WeaponLoadout.Length!=serialized.Length)throw new InvalidOperationException("Rhodes cached/serialized loadout mismatch");
            HashSet<string> ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(WeaponBinding binding in core.WeaponLoadout){
                if(binding==null||!ids.Add(binding.WeaponID)||binding.TurretIndex<0||binding.TurretIndex>=view.TurretDefinitions.Length)throw new InvalidOperationException("Rhodes weapon ID/turret binding malformed");
                object item=serialized.GetValue(binding.WeaponBindingIndex);WeaponAttributesAsset asset=Read(item,"m_Weapon") as WeaponAttributesAsset;
                if(asset==null||!c.Owned.Contains(asset)||!object.ReferenceEquals(binding.Weapon,Read(asset,"m_Simulation")))throw new InvalidOperationException("Rhodes cached binding not linked to private weapon");
                if(registered&&!object.ReferenceEquals(Registry.GetEntityType(asset.name).Get<WeaponAttributes>(),binding.Weapon))throw new InvalidOperationException("Rhodes weapon native registry mismatch "+asset.name);
                int ciwsStation=Array.IndexOf(CiwsWeaponIDs,binding.WeaponID);
                if(binding.WeaponID==MainForeWeaponID||binding.WeaponID==MainAftWeaponID)
                {
                    if(binding.Weapon.BaseDamagePerRound!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(300)||!binding.DisabledOnSpawn)
                        throw new InvalidOperationException("R19 native main base damage/spawn gate differs: "+binding.WeaponID);
                }
                if(ciwsStation>=0)
                {
                    object simulation=Read(asset,"m_Simulation");Array modifiers=Read(simulation,"m_Modifiers") as Array;
                    ScriptableObject priority=Read(asset,"m_TargetPriorization") as ScriptableObject;
                    TargetPriorizationAttributes priorityData=binding.Weapon.TargetPriorizationAttributes;
                    if(priority==null||priority.name!=UnitKey+"_CIWS_TargetPriorities"||!c.Owned.Contains(priority)||
                        !object.ReferenceEquals(priorityData,Read(priority,"m_Attributes")))
                        throw new InvalidOperationException("Rhodes CIWS priority asset/cache is not private");
                    VerifyCiwsTargetPriorityData(priorityData);
                    if(Convert.ToInt32(Read(simulation,"m_BaseDamagePerRound"))!=6||Convert.ToInt32(Read(simulation,"m_RateOfFire"))!=2||
                        Convert.ToInt32(Read(simulation,"m_NumberOfBursts"))!=4||Convert.ToInt32(Read(simulation,"m_DamagePacketsPerShot"))!=1||
                        Convert.ToInt32(Read(simulation,"m_BurstPeriodMinTime"))!=1200||Convert.ToInt32(Read(simulation,"m_BurstPeriodMaxTime"))!=1600||
                        Convert.ToInt32(Read(simulation,"m_CooldownTime"))!=750||Convert.ToInt32(Read(simulation,"m_ReloadTime"))!=1500||
                        Convert.ToInt32(Read(simulation,"m_WindUpTime"))!=250||Convert.ToInt32(Read(simulation,"m_WindDownTime"))!=500)
                        throw new InvalidOperationException("CIWS native base cycle differs from power profile28");
                    foreach(WeaponBinding other in core.WeaponLoadout)if(Array.IndexOf(MainWeaponIDs,other.WeaponID)>=0&&
                        object.ReferenceEquals(other.Weapon.TargetPriorizationAttributes,priorityData))
                        throw new InvalidOperationException("Rhodes CIWS target priorities leaked to a main gun");
                    UnitViewAttributes.TurretAttributes turret=view.TurretDefinitions[binding.TurretIndex];
                    if(modifiers==null||modifiers.Length!=0||!(bool)Read(simulation,"m_LineOfSightRequired")||
                        Convert.ToSingle(Read(Read(simulation,"m_Turret"),"m_FieldOfView"),CultureInfo.InvariantCulture)!=CiwsFieldOfViewDegrees||
                        Math.Abs(BBI.Core.Utility.FixedPoint.Fixed64.UnsafeFloatValue(binding.DefaultTurretAngleOffsetRadians)-CiwsDefaultYawDegrees(ciwsStation)*(float)(Math.PI/180.0))>.0001f||
                        turret.PitchMin!=-65f||turret.PitchMax!=80f||!binding.DisabledOnSpawn)
                        throw new InvalidOperationException("Rhodes CIWS must retain native air/ground targeting, outward arc and depression: "+binding.WeaponID);
                }
            }
            foreach(string id in MainWeaponIDs)if(!ids.Contains(id))throw new InvalidOperationException("Rhodes main binding missing "+id);
            foreach(string id in CiwsWeaponIDs)if(!ids.Contains(id))throw new InvalidOperationException("Rhodes CIWS binding missing "+id);
            foreach(SourceRecord source in c.Sources)if(Fingerprint(source.Asset)!=source.Fingerprint)throw new InvalidOperationException("Native asset changed during Rhodes weapon preparation "+source.Asset.name);
        }
        static Transform RequireRhodesWeaponBone(GameObject prefab,string name)
        {
            Transform found=null;
            foreach(Transform node in prefab.GetComponentsInChildren<Transform>(true))if(node.name==name){if(found!=null)throw new InvalidOperationException("Duplicate Rhodes weapon transform "+name);found=node;}
            if(found==null)throw new InvalidOperationException("Articulated V05 Rhodes node missing: "+name+"; a static material-batched GLB is not sufficient");return found;
        }
        // Add to ObserveViews ONLY for the private Rhodes binding.
        static void ObserveWeaponView(UnitView view)
        {
            RhodesWeaponPresentationBinding binding=view.GetComponentInChildren<RhodesWeaponPresentationBinding>();
            if(binding==null)return;RhodesWeaponPresentation driver=view.GetComponent<RhodesWeaponPresentation>();
            if(driver==null)driver=view.gameObject.AddComponent<RhodesWeaponPresentation>();driver.Initialize(view,binding);
        }
    }

    // Unity clones transform references with the private model prefab.
    public sealed class RhodesWeaponPresentationBinding:MonoBehaviour
    {
        public Transform[] LeftPitch,RightPitch,Recoil,Muzzle;
        public Quaternion[] LeftPitchRest,RightPitchRest;
        public Vector3[] RecoilRest;
        public string[] MainIDs;
        public float MaximumRecoilMetres;
    }

    // Presentation only: second pitch joint and recoil follow actual native
    // rounds. The native simulation alone chooses targets, fires and damages.
    public sealed class RhodesWeaponPresentation:IsolatedUnitPresentation
    {
        static IEventReceiver Receiver;
        static readonly object Gate=new object();
        static readonly Dictionary<Entity,RhodesWeaponPresentation> Instances=new Dictionary<Entity,RhodesWeaponPresentation>();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> Handler=OnNativeRound;
        UnitView View;RhodesWeaponPresentationBinding Binding;Entity Entity;
        RhodesV05ModuleDriver DeploymentModules;
        int[] MainBindingIndices=new int[2],Barrel=new int[2],Queued=new int[2];
        readonly RhodesMainTurretReadOnlyDiagnostic TurretDiagnostic=new RhodesMainTurretReadOnlyDiagnostic();
        readonly RhodesTurretArcReference TurretArcReference=new RhodesTurretArcReference();
        // Native simulation stays authoritative. These fields only bound an
        // existing UnitView presentation-cache consistency repair.
        static readonly MethodInfo NativeTurretCacheRefresh=typeof(UnitView).GetMethod("UpdateTurretData",
            BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(UnitState),typeof(int)},null);
        const int MaximumCacheRefreshFailures=3,MaximumCacheRefreshLogRows=10;
        readonly int[] MainTurretIndices=new int[2],CacheRefreshFailures=new int[2];
        readonly bool[] CacheRefreshStateKnown=new bool[2],CacheRefreshLastEnabled=new bool[2];
        Entity CacheRefreshEntity;bool HaveCacheRefreshEntity;
        int LastCacheRefreshTick=int.MinValue,CacheRefreshLogRows;
        float[] RecoilAge=new float[]{100f,100f,100f,100f};bool Initialized;int LastComposedFrame=-1;
        int LastMainReferenceEpoch=-1;
        public void Initialize(UnitView view,RhodesWeaponPresentationBinding binding)
        {
            if(Initialized&&View==view&&Entity.Equals(view.EntityID)){if(DeploymentModules==null)DeploymentModules=view.GetComponent<RhodesV05ModuleDriver>();return;}
            Release();LastComposedFrame=-1;View=view;Binding=binding;Entity=view.EntityID;DeploymentModules=view.GetComponent<RhodesV05ModuleDriver>();
            if(binding.LeftPitch.Length!=2||binding.RightPitch.Length!=2||binding.Recoil.Length!=4||binding.Muzzle.Length!=4)throw new InvalidOperationException("Rhodes visual weapon rig incomplete");
            for(int i=0;i<2;i++){
                MainBindingIndices[i]=-1;Barrel[i]=0;Queued[i]=0;
                foreach(WeaponBinding weapon in view.UnitAttrs.WeaponLoadout)if(weapon.WeaponID==binding.MainIDs[i])MainBindingIndices[i]=weapon.WeaponBindingIndex;
                if(MainBindingIndices[i]<0)throw new InvalidOperationException("Rhodes native main binding absent from spawned view");
            }
            InitializeMainTurretCacheRefresh(view);
            TurretArcReference.Initialize(view);
            LastMainReferenceEpoch=-1;
            TurretDiagnostic.Initialize(view,MainBindingIndices);
            lock(Gate)Instances[Entity]=this;Initialized=true;EnsureReceiver();
        }
        public static void ResetAll()
        {
            List<RhodesWeaponPresentation> current;
            lock(Gate)current=new List<RhodesWeaponPresentation>(Instances.Values);
            foreach(RhodesWeaponPresentation instance in current)if(instance!=null)instance.Release();
            lock(Gate)Instances.Clear();
            if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=null;
        }
        static void EnsureReceiver()
        {
            IEventReceiver current=ShipbreakersMain.SimToPresentationEventSystem;if(object.ReferenceEquals(current,Receiver))return;
            if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=current;
            if(Receiver!=null)Receiver.AddHandler<WeaponTriggerEvent>(Handler);
        }
        static void OnNativeRound(WeaponTriggerEvent message)
        {
            if(message==null||(message.Trigger!=WeaponFireTrigger.FireRoundHit&&message.Trigger!=WeaponFireTrigger.FireRoundMiss))return;
            lock(Gate){RhodesWeaponPresentation instance;if(!Instances.TryGetValue(message.Entity,out instance))return;
                instance.TurretArcReference.NoteRound(message.WeaponBindingIndex);
                for(int i=0;i<2;i++)if(instance.MainBindingIndices[i]==message.WeaponBindingIndex){
                    instance.Queued[i]=Math.Min(32,instance.Queued[i]+1);instance.TurretDiagnostic.NoteMainRound(i);}}
        }
        // The carrier host owns the final pose composition order after its
        // modules and ground corrections. No independent Unity LateUpdate writer.
        public void ApplyFrame(int frame)
        {
            if(PresentationFailed||!enabled)return;
            try{ApplyFrameCore(frame);}
            catch(Exception error){DisablePresentation(View,"carrier_weapons",error);}
        }
        void ApplyFrameCore(int frame)
        {
            if(LastComposedFrame==frame)return;LastComposedFrame=frame;
            if(!Initialized||View==null||Binding==null)return;
            if(!Entity.Equals(View.EntityID)){Release();return;}
            EnsureReceiver();float dt=Runtime.PresentationDeltaTime;if(dt<=0f)return;
            RefreshNativeMainTurretCache(dt);
            int epoch=DeploymentModules==null?0:DeploymentModules.MainReferenceEpoch;
            if(epoch!=LastMainReferenceEpoch)
            {if(LastMainReferenceEpoch>=0)TurretArcReference.RebindAfterTransport();LastMainReferenceEpoch=epoch;}
            TurretArcReference.Step(dt);
            TurretDiagnostic.Step(dt);
            for(int i=0;i<2;i++){
                Binding.RightPitch[i].localRotation=Binding.RightPitchRest[i]*Quaternion.Inverse(Binding.LeftPitchRest[i])*Binding.LeftPitch[i].localRotation;
                int count;lock(Gate){count=Queued[i];Queued[i]=0;}
                for(int round=0;round<count;round++){int index=i*2+Barrel[i];RecoilAge[index]=0f;Barrel[i]=1-Barrel[i];}
            }
            for(int i=0;i<4;i++){
                RecoilAge[i]+=dt;float age=RecoilAge[i];float amount=age<.06f?age/.06f:(age<.46f?1f-(age-.06f)/.4f:0f);
                Vector3 displacement=Binding.Muzzle[i].forward*(Binding.MaximumRecoilMetres*Mathf.Clamp01(amount));
                Transform recoil=Binding.Recoil[i];recoil.localPosition=Binding.RecoilRest[i]-recoil.parent.InverseTransformVector(displacement);
            }
            if(DeploymentModules!=null)DeploymentModules.ReapplyGunTransit();
        }
        void InitializeMainTurretCacheRefresh(UnitView view)
        {
            // Each main must be the only binding on its turret. Never repair
            // a shared-turret weapon swap using a guessed main binding.
            for(int main=0;main<2;main++)
            {
                WeaponBinding binding=view.UnitAttrs.WeaponLoadout[MainBindingIndices[main]];
                int count=0;
                foreach(WeaponBinding candidate in view.UnitAttrs.WeaponLoadout)
                    if(candidate!=null&&candidate.TurretIndex==binding.TurretIndex)count++;
                if(binding.WeaponID!=Binding.MainIDs[main]||binding.TurretIndex<0||count!=1)
                    throw new InvalidOperationException("Rhodes main turret must have one unique native binding");
                MainTurretIndices[main]=binding.TurretIndex;
            }
            if(MainTurretIndices[0]==MainTurretIndices[1])
                throw new InvalidOperationException("Rhodes mains must not share a native turret");
            if(!HaveCacheRefreshEntity||!CacheRefreshEntity.Equals(Entity))
            {
                CacheRefreshEntity=Entity;HaveCacheRefreshEntity=true;
                LastCacheRefreshTick=int.MinValue;CacheRefreshLogRows=0;
                for(int main=0;main<2;main++)
                {CacheRefreshFailures[main]=0;CacheRefreshStateKnown[main]=false;}
            }
        }

        // LateUpdate is after native presentation updates. Refresh the same
        // native cache using the current immutable UnitState, then let the
        // engine's next normal UpdateTurrets perform all yaw/pitch actuation.
        // No scene scan, new component/hook, simulation flag or pose write.
        void RefreshNativeMainTurretCache(float deltaTime)
        {
            if(!Initialized||View==null||Binding==null||!View.gameObject.activeInHierarchy||
                float.IsNaN(deltaTime)||float.IsInfinity(deltaTime)||deltaTime<=0f||
                Entity.Equals(Entity.None)||!Entity.Equals(View.EntityID)||
                !HaveCacheRefreshEntity||!CacheRefreshEntity.Equals(Entity))return;
            UnitState state=View.NonInterpolatedTickState;
            if(state==null||state.UnitIsDead||!state.EntityID.Equals(Entity)||
                state.TypeID!=RhodesCarrierUnits.UnitKey||!state.OwnerCommander.Equals(View.CommanderID)||
                state.AllWeapons==null||View.UnitAttrs==null||View.UnitAttrs.WeaponLoadout==null)return;
            int tick=ShipbreakersMain.CurrentSimTick;
            if(tick<0||tick==LastCacheRefreshTick)return;
            LastCacheRefreshTick=tick;
            for(int main=0;main<2;main++)
            {
                int bindingIndex=MainBindingIndices[main],turretIndex=MainTurretIndices[main];
                if(bindingIndex<0||bindingIndex>=state.AllWeapons.Length||
                    bindingIndex>=View.UnitAttrs.WeaponLoadout.Length)continue;
                WeaponBinding binding=View.UnitAttrs.WeaponLoadout[bindingIndex];
                WeaponState weapon=state.AllWeapons[bindingIndex];
                if(binding==null||weapon==null||binding.WeaponID!=Binding.MainIDs[main]||
                    weapon.WeaponBindingID!=binding.WeaponID||binding.TurretIndex!=turretIndex)continue;
                // A target change never resets the retry budget. Only a real
                // native enabled-state transition permits another bounded try.
                if(!CacheRefreshStateKnown[main]||CacheRefreshLastEnabled[main]!=weapon.Enabled)
                {CacheRefreshStateKnown[main]=true;CacheRefreshLastEnabled[main]=weapon.Enabled;CacheRefreshFailures[main]=0;}
                if(CacheRefreshFailures[main]>=MaximumCacheRefreshFailures)continue;
                int beforeActive=-2;float beforeSpeed=0f;
                try
                {
                    WeaponBinding expected=WeaponHelperShared.GetFirstEnabledWeaponBindingForTurret(state,turretIndex);
                    if(weapon.Enabled?(expected==null||expected.WeaponID!=binding.WeaponID||
                        expected.WeaponBindingIndex!=bindingIndex||expected.TurretIndex!=turretIndex):expected!=null)
                        throw new InvalidOperationException("Native enabled-weapon reader disagrees with unique main state");
                    int expectedActive=expected==null?-1:expected.WeaponBindingIndex;
                    float expectedSpeed=expected==null?0f:
                        BBI.Core.Utility.FixedPoint.Fixed64.UnsafeFloatValue(expected.Weapon.Turret.RotationSpeed);
                    if(float.IsNaN(expectedSpeed)||float.IsInfinity(expectedSpeed))
                        throw new InvalidOperationException("Native expected main turret speed is non-finite");
                    ReadMainTurretCache(turretIndex,out beforeActive,out beforeSpeed);
                    bool speedMatches=expected==null||Math.Abs(beforeSpeed-expectedSpeed)<=.001f;
                    if(beforeActive==expectedActive&&speedMatches)
                    {CacheRefreshFailures[main]=0;continue;}
                    if(NativeTurretCacheRefresh==null||NativeTurretCacheRefresh.ReturnType!=typeof(void))
                        throw new MissingMethodException("Native UnitView.UpdateTurretData(UnitState,int) unavailable");
                    // One invocation at most per turret/native tick; there are
                    // exactly two unique main turrets, so the entity cap is 2.
                    NativeTurretCacheRefresh.Invoke(View,new object[]{state,turretIndex});
                    int afterActive;float afterSpeed;
                    ReadMainTurretCache(turretIndex,out afterActive,out afterSpeed);
                    if(afterActive!=expectedActive||(expected!=null&&Math.Abs(afterSpeed-expectedSpeed)>.001f))
                        throw new InvalidOperationException("Native turret cache refresh readback did not match expected binding/speed");
                    CacheRefreshFailures[main]=0;
                    LogMainTurretCacheRefresh(main,tick,"refreshed",beforeActive,beforeSpeed,afterActive,afterSpeed,null);
                    continue;
                }
                catch(Exception error)
                {
                    CacheRefreshFailures[main]++;
                    Exception cause=error is TargetInvocationException&&error.InnerException!=null?error.InnerException:error;
                    LogMainTurretCacheRefresh(main,tick,
                        CacheRefreshFailures[main]>=MaximumCacheRefreshFailures?"stoppedForEnabledState":"refreshFailed",
                        beforeActive,beforeSpeed,-2,0f,cause);
                }
            }
        }
        void ReadMainTurretCache(int turretIndex,out int active,out float speed)
        {
            Array cache=NativeRead.Get(View,"mTurretData") as Array;
            if(cache==null||turretIndex<0||turretIndex>=cache.Length)
                throw new InvalidOperationException("Native main turret cache missing");
            object turret=cache.GetValue(turretIndex);
            active=(int)NativeRead.Get(turret,"ActiveWeaponBindingIndex");
            speed=(float)NativeRead.Get(turret,"RotationSpeed");
            if(float.IsNaN(speed)||float.IsInfinity(speed))
                throw new InvalidOperationException("Native main turret cache speed is non-finite");
        }
        void LogMainTurretCacheRefresh(int main,int tick,string reason,int beforeActive,float beforeSpeed,int afterActive,float afterSpeed,Exception error)
        {
            if(CacheRefreshLogRows>=MaximumCacheRefreshLogRows)return;CacheRefreshLogRows++;
            Runtime.Log("RHODES_MAIN_TURRET_CACHE entity="+Entity+"; main="+main+
                "; binding="+MainBindingIndices[main]+"; turret="+MainTurretIndices[main]+"; tick="+tick+
                "; reason="+reason+"; beforeActive="+beforeActive+"; beforeSpeed="+beforeSpeed+
                "; afterActive="+afterActive+"; afterSpeed="+afterSpeed+"; failuresForEnabledState="+CacheRefreshFailures[main]+
                "; method=NativeUpdateTurretData; simulationWrites=False; poseWrites=False; maxNativeCallsPerTurretTick=1; maxNativeCallsPerEntityTick=2"+
                (error==null?string.Empty:"; exception="+error.GetType().Name+"; message="+error.Message.Replace("\r"," ").Replace("\n"," ")));
        }
        void OnDisable(){Release();}
        void OnDestroy(){Release();}
        void Release()
        {
            if(!Initialized)return;
            TurretArcReference.Release();
            TurretDiagnostic.Release();
            lock(Gate){RhodesWeaponPresentation prior;if(Instances.TryGetValue(Entity,out prior)&&object.ReferenceEquals(prior,this))Instances.Remove(Entity);}
            if(Binding!=null&&Binding.Recoil!=null)for(int i=0;i<Binding.Recoil.Length;i++)if(Binding.Recoil[i]!=null)Binding.Recoil[i].localPosition=Binding.RecoilRest[i];
            Initialized=false;
            bool empty;lock(Gate)empty=Instances.Count==0;
            if(empty&&Receiver!=null){Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=null;}
        }
    }
}
