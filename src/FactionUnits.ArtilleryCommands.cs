using System;
using System.Collections;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        internal const string ArtilleryBarrageAbility="Ability_RI_U09_Barrage",ArtilleryPreciseAbility="Ability_RI_U09_Precise",ArtilleryToggleAbility="Ability_RI_U09_ToggleAutoFire";
        internal const string ArtilleryToggleStatus="Status_RI_U09_ToggleAutoFire";
        static string ArtilleryPrivateCommandKey(string original)
        {
            if(original=="Ability_C_ArtilleryBarrage")return ArtilleryBarrageAbility;
            if(original=="Ability_C_ArtilleryBarrage_Precise")return ArtilleryPreciseAbility;
            if(original=="Ability_C_ToggleAutoFire")return ArtilleryToggleAbility;
            if(original=="ToggleAutoFire_StatusEffect")return ArtilleryToggleStatus;
            return null;
        }
        static void NameArtilleryCommandData(object attributes,object view,string original)
        {
            string key=ArtilleryPrivateCommandKey(original);if(key==null)throw new InvalidOperationException("Unknown private U09 command donor");
            ((NamedObjectBase)attributes).InitializeName(key);if(view!=null)((NamedObjectBase)view).InitializeName(key);
            if(view!=null&&original=="Ability_C_ArtilleryBarrage")
            {
                Write(view,"LocalizedTitleStringID","区域炮击");
                Write(view,"LocalizedShortDescriptionStringID","向指定区域发射炮弹");
                Write(view,"LocalizedLongDescriptionStringID","以曲射炮弹打击指定区域，适合轰击集结中的地面部队。存在近距离射击盲区。");
            }
            if(view!=null&&original=="Ability_C_ArtilleryBarrage_Precise")
            {
                Write(view,"LocalizedTitleStringID","精确炮击");
                Write(view,"LocalizedShortDescriptionStringID","向指定目标位置精确射击");
                Write(view,"LocalizedLongDescriptionStringID","向指定目标位置发射单枚炮弹。存在近距离射击盲区，需由友军提供视野。");
            }
        }
        static void PrivatizeArtilleryCommands(Context c)
        {
            int count=0;
            foreach(ScriptableObject asset in c.Owned)
            {
                string original=asset.name,key=ArtilleryPrivateCommandKey(original);if(key==null)continue;
                object attributes=Read(asset,"m_Attributes"),view=null;
                if(original.StartsWith("Ability_",StringComparison.Ordinal))view=Read(asset,"m_View");
                NameArtilleryCommandData(attributes,view,original);asset.name=key;count++;
            }
            if(count!=4)throw new InvalidOperationException("U09 requires exactly3 private commands and1 private toggle status");
        }
        static void RegisterArtilleryCommands(Context c)
        {
            foreach(ScriptableObject asset in c.Owned)
            {
                if(asset.name!=ArtilleryBarrageAbility&&asset.name!=ArtilleryPreciseAbility&&asset.name!=ArtilleryToggleAbility&&asset.name!=ArtilleryToggleStatus)continue;
                if(Registry.GetEntityType(asset.name)!=null)throw new InvalidOperationException("Private U09 command key collision "+asset.name);
                EntityTypeAttributes definition=new EntityTypeAttributes(asset.name);object data=Read(asset,"Data");IEnumerable many=data as IEnumerable;
                if(many!=null){foreach(object item in many)if(item!=null&&!definition.Add(item))throw new InvalidOperationException("Duplicate private U09 command datum "+asset.name);}
                else if(data==null||!definition.Add(data))throw new InvalidOperationException("Private U09 command has no data "+asset.name);
                Registry.Add(definition);
            }
        }
        static void VerifyArtilleryGlobalCommands(Context c)
        {
            foreach(string key in new string[]{ArtilleryBarrageAbility,ArtilleryPreciseAbility,ArtilleryToggleAbility})
            {
                EntityTypeAttributes global=Registry.GetEntityType(key);AbilityAttributes own=c.Definition.Get<AbilityAttributes>(key);
                if(global==null||own==null||!ReferenceEquals(own,global.Get<AbilityAttributes>()))throw new InvalidOperationException("U09 unit and global native command lookup differ "+key);
            }
            EntityTypeAttributes status=Registry.GetEntityType(ArtilleryToggleStatus);
            if(status==null||status.Get<StatusEffectAttributes>()==null)throw new InvalidOperationException("U09 private auto-fire status missing");
        }
    }
}
