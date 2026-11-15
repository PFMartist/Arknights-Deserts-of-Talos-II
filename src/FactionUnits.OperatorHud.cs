using System;
using System.Collections.Generic;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        // Called once after all U08 skills and cargo commands exist. Native
        // FilterAbilities produces compact orders; a short array is not an
        // occupied cell. Clone every context separately, including repeated
        // references, and publish only after all final orders are complete.
        internal static string PrepareOperatorTransportHud(object hud,string[] productKeys)
        {
            if(hud==null||productKeys==null)throw new ArgumentNullException("U08 HUD layout inputs");
            HashSet<string> owned=new HashSet<string>(StringComparer.Ordinal);
            for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
            {owned.Add(RhodesEmbarkedOperatorSkills.ProxyAbilityKey(kind));owned.Add(RhodesEmbarkedOperatorSkills.AutoToggleAbilityKey(kind));}
            owned.Add(RhodesOperatorTransport.UnloadAbilityKey);owned.Add(RhodesOperatorTransport.RecallAbilityKey);
            for(int index=0;index<productKeys.Length;index++)
                if(string.IsNullOrEmpty(productKeys[index])||!owned.Add(productKeys[index]))throw new ArgumentException("U08 product HUD keys must be distinct");
            Array contexts=(Array)Read(hud,"m_HUDContexts");
            if(contexts==null)throw new InvalidOperationException("U08 native HUD contexts missing");
            Array result=Array.CreateInstance(contexts.GetType().GetElementType(),contexts.Length);
            List<string> diagnostics=new List<string>();
            for(int index=0;index<contexts.Length;index++)
            {
                object source=contexts.GetValue(index);if(source==null){result.SetValue(null,index);continue;}
                object context=CopyData(source,new Dictionary<object,object>(new IdentityComparer()));
                string[] prior=(string[])Read(source,"m_AbilityOrder");
                List<string> order=prior==null?new List<string>():new List<string>(prior);
                while(order.Count<Math.Max(15,12+productKeys.Length))order.Add(string.Empty);
                for(int cell=0;cell<order.Count;cell++)if(IsOwnedOperatorHudKey(order[cell],owned))order[cell]=string.Empty;
                List<int> reserved=new List<int>();reserved.Add(6);reserved.Add(7);reserved.Add(8);
                for(int cell=10;cell<12+productKeys.Length;cell++)reserved.Add(cell);
                List<string> displaced=new List<string>();List<string> occupied=new List<string>();
                foreach(int cell in reserved)
                {
                    if(!string.IsNullOrEmpty(order[cell])){displaced.Add(order[cell]);occupied.Add(cell+":"+order[cell]);}
                    order[cell]=string.Empty;
                }
                order[6]=RhodesEmbarkedOperatorSkills.ProxyAbilityKey(0);order[7]=RhodesEmbarkedOperatorSkills.ProxyAbilityKey(1);order[8]=RhodesEmbarkedOperatorSkills.ProxyAbilityKey(2);
                for(int product=0;product<productKeys.Length;product++)order[10+product]=productKeys[product];
                order[10+productKeys.Length]=RhodesOperatorTransport.UnloadAbilityKey;
                order[11+productKeys.Length]=RhodesOperatorTransport.RecallAbilityKey;
                foreach(string key in displaced)PlacePreservedOperatorHudCommand(order,key,reserved);
                SanitizeOperatorAdditionalHud(context,order,reserved,owned);
                Write(context,"m_AbilityOrder",order.ToArray());result.SetValue(context,index);
                diagnostics.Add("context["+index+"]="+(string)Read(context,"m_ContextName")+"; inputLength="+(prior==null?-1:prior.Length)+
                    "; shortForSkillCells="+(prior==null||prior.Length<8)+"; occupiedReservedCells="+occupied.Count+
                    "; relocatedNative="+string.Join(",",occupied.ToArray())+"; outputLength="+order.Count);
            }
            Write(hud,"m_HUDContexts",result);return string.Join(" | ",diagnostics.ToArray());
        }
        static bool IsOwnedOperatorHudKey(string key,HashSet<string> owned)
        {return !string.IsNullOrEmpty(key)&&(owned.Contains(key)||RhodesOperatorTransport.IsAbility(key)||RhodesEmbarkedOperatorSkills.IsProxyAbilityKey(key)||RhodesEmbarkedOperatorSkills.IsAutoToggleAbilityKey(key));}
        static void PlacePreservedOperatorHudCommand(List<string> order,string key,List<int> reserved)
        {
            foreach(int cell in new int[]{8,9,14})
                if(cell<order.Count&&!reserved.Contains(cell)&&string.IsNullOrEmpty(order[cell])){order[cell]=key;return;}
            for(int cell=0;cell<order.Count;cell++)
                if(!reserved.Contains(cell)&&string.IsNullOrEmpty(order[cell])){order[cell]=key;return;}
            order.Add(key);
        }
        static void SanitizeOperatorAdditionalHud(object context,List<string> order,List<int> reserved,HashSet<string> owned)
        {
            Array original=Read(context,"m_AdditionalAbilities") as Array;if(original==null)return;
            List<object> retained=new List<object>();
            foreach(object slot in original)
            {
                if(slot==null){retained.Add(null);continue;}
                bool ownedBase=IsOwnedOperatorHudKey((string)Read(slot,"m_BaseAbilityTypeName"),owned);
                string[] prior=Read(slot,"m_AvailableAbilities") as string[];List<string> available=new List<string>();
                if(prior!=null)foreach(string key in prior)if(!IsOwnedOperatorHudKey(key,owned))available.Add(key);
                // An obsolete custom base must not keep a second skill or a
                // cargo-only toggle in the native command grid. Preserve any
                // native alternatives from such a slot in ordinary cells.
                if(ownedBase){foreach(string key in available)if(!string.IsNullOrEmpty(key)&&!order.Contains(key))PlacePreservedOperatorHudCommand(order,key,reserved);continue;}
                Write(slot,"m_AvailableAbilities",available.ToArray());retained.Add(slot);
            }
            Array result=Array.CreateInstance(original.GetType().GetElementType(),retained.Count);
            for(int index=0;index<retained.Count;index++)result.SetValue(retained[index],index);
            Write(context,"m_AdditionalAbilities",result);
        }
    }
}
