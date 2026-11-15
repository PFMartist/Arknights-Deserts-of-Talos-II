using System;
using System.Collections.Generic;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        // Called only on an already deep-cloned private closure. Do not map
        // historical aliases to every active weapon: native DEFAULT is exact.
        public static void NormalizePrivateVeterancy(UnitAssetContainer custom,string unitKey)
        {
            ScriptableObject experience=Read(custom,"m_Experience") as ScriptableObject;
            if(experience==null)
            {
                if(unitKey!=HarvesterKey)throw new InvalidOperationException("Combat donor lost native experience "+unitKey);
                return;
            }
            object core=Read(custom,"m_Attributes");
            Array weapons=(Array)Read(core,"m_WeaponLoadout");List<string> ids=new List<string>();foreach(object binding in weapons)ids.Add((string)Read(binding,"m_WeaponID"));
            Array levels=(Array)Read(Read(experience,"m_Attributes"),"m_Levels");
            if(levels.Length!=6)throw new InvalidOperationException("Native rank count changed "+unitKey);
            for(int i=1;i<levels.Length;i++)
            {
                ScriptableObject buff=Read(levels.GetValue(i),"m_Buff") as ScriptableObject;
                if(buff==null)throw new InvalidOperationException("Native rank buff missing "+unitKey+" rank"+i);
                NormalizePrivateRankBuff(Read(buff,"m_Attributes"),ids.ToArray(),unitKey);
                if(unitKey==EngineeringKey&&(i==3||i==4))
                    Write(levels.GetValue(i),"m_BuffTooltipLocID","本级不提供额外维修加成。");
            }
        }
        static void NormalizePrivateRankBuff(object data,string[] actualWeapons,string unitKey)
        {
            Array entries=(Array)Read(data,"m_Buffs");List<object> kept=new List<object>();HashSet<string> unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(object entry in entries)
            {
                int id=Convert.ToInt32(Read(entry,"m_AttributeID")),category=id&unchecked((int)0xffff0000);string scope=(string)Read(entry,"m_Name");
                if((category==262144||category==33554432)&&Array.IndexOf(actualWeapons,scope)<0)continue;
                // The Baserunner donor's gun-display stat cannot grant a
                // replacement repair or weapon bonus to the unarmed U03.
                if(unitKey==EngineeringKey&&id==65548)continue;
                string signature=scope+":"+id+":"+Read(entry,"m_Mode")+":"+Read(entry,"m_Value");
                if(unique.Add(signature))kept.Add(entry);
            }
            Array output=Array.CreateInstance(entries.GetType().GetElementType(),kept.Count);for(int i=0;i<kept.Count;i++)output.SetValue(kept[i],i);Write(data,"m_Buffs",output);
        }
        static void VerifyPrivateVeterancy(Context c)
        {
            if(c.Spec.Key==ArtilleryKey)return; // separately scaled, verified medium profile
            ExperienceAttributes source=Registry.GetEntityType(c.Spec.Donor).Get<ExperienceAttributes>(),own=c.Definition.Get<ExperienceAttributes>();
            if(source==null)
            {
                if(own!=null||c.Spec.Key!=HarvesterKey)throw new InvalidOperationException("Unexpected private rank invention "+c.Spec.Key);
                return;
            }
            if(own==null||object.ReferenceEquals(own,source)||own.Levels.Length!=source.Levels.Length)throw new InvalidOperationException("Private experience isolation failed "+c.Spec.Key);
            for(int rank=0;rank<own.Levels.Length;rank++)
            {
                if(own.Levels[rank].RequiredExperience!=source.Levels[rank].RequiredExperience)throw new InvalidOperationException("Private native XP threshold changed "+c.Spec.Key);
                if(rank==0)continue;
                AttributeBuffSet buff=own.Levels[rank].Buff;if(buff==null||object.ReferenceEquals(buff,source.Levels[rank].Buff))throw new InvalidOperationException("Private native rank set aliases source "+c.Spec.Key);
                foreach(object entry in (Array)Read(buff,"m_Buffs"))
                {
                    int category=Convert.ToInt32(Read(entry,"m_AttributeID"))&unchecked((int)0xffff0000);if(category!=262144&&category!=33554432)continue;
                    bool match=false;foreach(WeaponBinding weapon in c.Definition.Get<UnitAttributes>().WeaponLoadout)if(weapon.WeaponID==(string)Read(entry,"m_Name"))match=true;
                    if(!match)throw new InvalidOperationException("Private rank retains stale weapon scope "+c.Spec.Key);
                }
            }
        }
    }
}
