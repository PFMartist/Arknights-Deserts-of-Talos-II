using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Game;
using BBI.Unity.Game.UI;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // The native controller still owns every feedback object and its pool lease.
    // Private operator outro prefabs contain only visual data, so bound their
    // final feedback here without expiring pending targeting/approach commands.
    internal static class RhodesOperatorReticleLifetime
    {
        const BindingFlags InstanceFields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly FieldInfo ControllerField=typeof(UnitInterfaceController).GetField("mTargetingReticleController",InstanceFields);
        static readonly FieldInfo FeedbackField=typeof(TargetingReticleController).GetField("mFeedbackSprites",InstanceFields);
        static readonly FieldInfo AbilityField=typeof(ReticleSprite).GetField("mAbilityTypeName",InstanceFields);
        static readonly FieldInfo OutroField=typeof(ReticleSprite).GetField("mOutroSpriteTransform",InstanceFields);
        static readonly FieldInfo CancelledField=typeof(ReticleSprite).GetField("mIsCancelledAbilityFeedback",InstanceFields);
        sealed class Entry
        {
            internal Transform Outro;
            internal int Deadline,Seen;
        }
        static readonly Dictionary<ReticleSprite,Entry> Tracked=new Dictionary<ReticleSprite,Entry>();
        static readonly List<ReticleSprite> Stale=new List<ReticleSprite>();
        static UnitInterfaceController Owner;
        static TargetingReticleController Controller;
        static List<ReticleSprite> Feedback;
        static bool Validated;
        static int LastTick=int.MinValue,Generation,Reports;

        internal static void ObservePanel(object panel)
        {
            UnitInterfaceController owner=panel as UnitInterfaceController;
            if(owner==null||object.ReferenceEquals(Owner,owner))return;
            Owner=owner;ResetSession();
        }
        static void ValidateField(FieldInfo field,Type expected,string name)
        {
            if(field==null||field.IsStatic||field.FieldType!=expected)
                throw new InvalidOperationException("Native operator reticle ABI differs: "+name);
        }
        static void Validate()
        {
            if(Validated)return;
            ValidateField(ControllerField,typeof(TargetingReticleController),"UnitInterfaceController.mTargetingReticleController");
            ValidateField(FeedbackField,typeof(List<ReticleSprite>),"TargetingReticleController.mFeedbackSprites");
            ValidateField(AbilityField,typeof(string),"ReticleSprite.mAbilityTypeName");
            ValidateField(OutroField,typeof(Transform),"ReticleSprite.mOutroSpriteTransform");
            ValidateField(CancelledField,typeof(bool),"ReticleSprite.mIsCancelledAbilityFeedback");
            Validated=true;
        }
        static bool IsOperatorAbility(string key)
        {
            return key=="EF_Perlica_OrbitalStrike"||key=="EF_Laevatain_ChainFlame"||key=="EF_Gilberta_GravityWell"||
                key=="Ability_EF_U08_LaevatainSkill"||key=="Ability_EF_U08_GilbertaSkill";
        }
        internal static void MainTick()
        {
            if(Owner==null)return;
            Validate();
            TargetingReticleController current=ControllerField.GetValue(Owner) as TargetingReticleController;
            if(!object.ReferenceEquals(Controller,current))
            {
                ResetSession();Controller=current;
                if(Controller!=null)Feedback=FeedbackField.GetValue(Controller) as List<ReticleSprite>;
            }
            if(Feedback==null)return;
            int tick=ShipbreakersMain.CurrentSimTick;
            if(tick==LastTick)return;
            if(tick<LastTick){Tracked.Clear();Stale.Clear();Generation=0;}
            LastTick=tick;Generation++;
            for(int i=Feedback.Count-1;i>=0;i--)
            {
                ReticleSprite reticle=Feedback[i];if(reticle==null)continue;
                string ability=AbilityField.GetValue(reticle) as string;
                if(!IsOperatorAbility(ability)||(bool)CancelledField.GetValue(reticle))continue;
                Transform outro=OutroField.GetValue(reticle) as Transform;
                // No outro means the native ability is still pending or has
                // already been destroyed. Never time out that confirmed state.
                if(outro==null)continue;
                Entry entry;
                if(!Tracked.TryGetValue(reticle,out entry)||!object.ReferenceEquals(entry.Outro,outro))
                {
                    entry=new Entry{Outro=outro,Deadline=ShipbreakersMain.GetFutureFrameTick(1f)};
                    Tracked[reticle]=entry;
                }
                entry.Seen=Generation;
                if(tick<entry.Deadline)continue;
                // Destroy clears both native Transform references and returns
                // their leases. The controller removes its now-inactive list
                // entry on its normal update; do not mutate that list ourselves.
                reticle.Destroy();Tracked.Remove(reticle);
                if(Reports++<12)Runtime.Log("RHODES_OPERATOR_RETICLE_OUTRO_RETIRED ability="+ability+"; nativeDestroy=True; pendingReticleUntouched=True; recurringSceneScan=False");
            }
            Stale.Clear();
            foreach(KeyValuePair<ReticleSprite,Entry> item in Tracked)
                if(item.Value.Seen!=Generation)Stale.Add(item.Key);
            foreach(ReticleSprite item in Stale)Tracked.Remove(item);
            Stale.Clear();
        }
        internal static void ResetSession()
        {
            // UnitInterfaceController may persist across matches. Keep that
            // lifecycle-observed owner and bind its replacement controller later.
            Controller=null;Feedback=null;Tracked.Clear();Stale.Clear();
            LastTick=int.MinValue;Generation=0;Reports=0;
        }
    }
}
