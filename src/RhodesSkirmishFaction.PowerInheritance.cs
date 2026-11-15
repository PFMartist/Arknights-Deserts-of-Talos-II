using System;
using System.Collections.Generic;
using BBI.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesSkirmishFaction
    {
        // The native MP commander carries one global setup packet, independent
        // of faction. Its Set1/Set30/Set5 values override our private carrier
        // before PowerShunt.Create. Remove that exact packet from this newly
        // cloned commander only; never mutate its shared native BuffSet asset.
        static int StripInheritedMultiplayerPower(CommanderAttributes privateCopy, CommanderAttributes source)
        {
            if(privateCopy==null||source==null||ReferenceEquals(privateCopy,source))
                throw new InvalidOperationException("Starting power isolation needs a private commander copy");
            Array original=(Array)NativeRead.Get(privateCopy,"m_StartingBuffs");
            Array native=(Array)NativeRead.Get(source,"m_StartingBuffs");
            if(ReferenceEquals(original,native))throw new InvalidOperationException("Starting buff array aliases native commander");
            List<object> retained=new List<object>();int removed=0;
            foreach(object target in original)
            {
                object asset=NativeRead.Get(target,"m_BuffSet");
                object data=ReferenceEquals(asset,null)?null:NativeRead.Get(asset,"m_Attributes");
                if(IsNativeMultiplayerPowerPacket(target,data))removed++;
                else retained.Add(target);
            }
            if(removed!=1)throw new InvalidOperationException("Expected exactly one audited native MP starting power packet; found "+removed);
            Array output=Array.CreateInstance(original.GetType().GetElementType(),retained.Count);
            for(int i=0;i<retained.Count;i++)output.SetValue(retained[i],i);
            NativeRead.Set(privateCopy,"m_StartingBuffs",output);
            return removed;
        }
        static bool IsNativeMultiplayerPowerPacket(object target,object data)
        {
            if(data==null||(string)NativeRead.Get(target,"m_UnitTypePrefix")!=string.Empty||
                !(bool)NativeRead.Get(target,"m_UseAsPrefix")||Convert.ToInt32(NativeRead.Get(target,"m_UnitClass"))!=0||
                Convert.ToInt32(NativeRead.Get(target,"m_ClassOperator"))!=1)return false;
            Array entries=NativeRead.Get(data,"m_Buffs") as Array;if(entries==null||entries.Length!=6)return false;
            HashSet<string> signatures=new HashSet<string>(StringComparer.Ordinal);
            foreach(object entry in entries)
            {
                if(Convert.ToInt32(NativeRead.Get(entry,"m_Mode"))!=2)return false;
                string signature=(string)NativeRead.Get(entry,"m_Name")+":"+Convert.ToInt32(NativeRead.Get(entry,"m_AttributeID"))+":"+Convert.ToInt32(NativeRead.Get(entry,"m_Value"));
                if(!signatures.Add(signature))return false;
            }
            return signatures.SetEquals(new string[]{"0:8388609:5","1:8388609:5","2:8388609:5","3:8388609:5","POWER:4194305:1","POWER:4194304:30"});
        }
    }
}
