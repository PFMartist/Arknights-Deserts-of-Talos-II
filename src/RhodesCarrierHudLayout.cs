using System;
using System.Collections.Generic;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.HUD;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        static void PrepareProductionPageCommands(Context carrier)
        {
            foreach(string context in RhodesHudLayoutPolicy.Contexts)
            {
                string key=RhodesHudLayoutPolicy.Menu(context),sourceName="Ability_C_Carrier_ContextMenu_"+context;
                ScriptableObject source=null;
                foreach(ScriptableObject ability in (Array)Read(carrier.Source,"m_Abilities"))if(ability!=null&&ability.name==sourceName)source=ability;
                if(source==null)throw new InvalidOperationException("Native carrier production page is absent: "+sourceName);
                ScriptableObject own=CloneAsset(carrier,source,key);RemapAsset(carrier,own);
                object data=Read(own,"m_Attributes"),view=Read(own,"m_View");
                ((NamedObjectBase)data).InitializeName(key);((NamedObjectBase)view).InitializeName(key);
                if((string)Read(Read(data,"m_ChangeContext"),"m_TargetContext")!=context)throw new InvalidOperationException("Native ChangeContext target mismatch");
                Write(Read(data,"m_ResearchDeps"),"m_DependentResearch",new string[0]);SetProductionEnum(data,"m_StartsRemovedIn",0);
                string title=context=="Support"?"勤务与采集":context=="Armored"?"地面突击":context=="Ranged"?"远程与防空":context=="Strike"?"航空单位":"返回生产分类";
                KeyCode hotkey=(KeyCode)RhodesHudLayoutPolicy.MenuHotkey(context);
                Write(view,"LocalizedTitleStringID",title);Write(view,"LocalizedShortDescriptionStringID",title);
                Write(view,"Hotkey",hotkey);Write(view,"ToggledHotkey",KeyCode.None);Write(view,"BindingIsExclusive",false);
                carrier.Commands.Add(own);CallIfPresent(own,"OnEnable");Registry.Add(ProductionAbilityDefinition(own));
            }
        }
        static void RebuildProductionPages(Context carrier)
        {
            Dictionary<string,string> products=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(ProductionItem item in carrier.Products)products.Add(item.UnitKey,item.AbilityKey);
            HashSet<string> commands=new HashSet<string>(StringComparer.Ordinal);
            foreach(ScriptableObject command in carrier.Commands)commands.Add(command.name);
            if(commands.Contains(AircraftProductionKey))products.Add(AircraftKey,AircraftProductionKey);
            if(commands.Contains("Ability_EF_Rhodes_ProduceHeavyBomber"))products.Add("EF_Rhodes_HeavyBomber_MP","Ability_EF_Rhodes_ProduceHeavyBomber");
            object hud=Read(carrier.Hud,"m_Attributes");Array contexts=(Array)Read(hud,"m_HUDContexts");
            HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            string[] common={"Ability_Common_MoveAbility","Ability_Common_AttackMoveAbility","Ability_Common_StopAbility","Ability_Common_Hold","Ability_Common_SetRally",SupportAbilityKey,"Ability_Common_DisableResourceController"};
            foreach(object context in contexts)
            {
                string name=(string)Read(context,"m_ContextName");if(!seen.Add(name))throw new InvalidOperationException("Duplicate private HUD context "+name);
                string[] order=RhodesHudLayoutPolicy.Page(name,products);
                for(int i=0;i<common.Length;i++)if(commands.Contains(common[i]))order[i]=common[i];
                ApplyAviationCommandSlots(order,commands);
                Write(context,"m_AbilityOrder",order);ClearArray(context,"m_AdditionalAbilities");
            }
            if(seen.Count!=5)throw new InvalidOperationException("Rhodes HUD requires the five native production contexts");
            Runtime.Log("RHODES_PRODUCTION_PAGES_READY nativeContexts=5; slotsPerContext=20; support=U01_U02_U03; assault=U04_U05; ranged=U06_U07_U09; aviation=U08_F01_B01; nativeHangarDeploySlots=15_B01_16_F01_17_U08; nativeHangarRecallSlot=18; nativeAlwaysActiveSlot19Empty=True; bomberLaunchHotkey=X; nativeChangeContext=True; onlyCurrentPageHotkeys=True; privateMenuIds=True; runtimeVisualAcceptancePending=True");
        }
        static void ApplyAviationCommandSlots(string[] order,ICollection<string> commands)
        {
            if(order==null||order.Length!=20)throw new InvalidOperationException("Rhodes aviation requires the native 20-slot HUD");
            // Native AllGrids consumes Common5, Abilities5, Production5,
            // HangarDeploy3, HangarRecall1, AlwaysActive1 in that order.
            // Kapisi assigns bomber/interceptor/gunship to15/16/17. Slot19
            // is the separate carrier-launch widget, not a fourth air button.
            string[] air={HeavyBomberLaunchKey,AircraftLaunchKey,TiltLaunchKey,AircraftRecallKey};
            for(int i=0;i<air.Length;i++)order[15+i]=commands.Contains(air[i])?air[i]:string.Empty;
            order[19]=string.Empty;
        }
        static void VerifyAviationCommandSlots(string[] order,ICollection<string> commands)
        {
            if(order==null||order.Length!=20||!string.IsNullOrEmpty(order[19]))throw new InvalidOperationException("Rhodes aviation occupied the native carrier-launch widget");
            string[] air={HeavyBomberLaunchKey,AircraftLaunchKey,TiltLaunchKey,AircraftRecallKey};
            for(int i=0;i<air.Length;i++)
            {
                int count=0;foreach(string key in order)if(key==air[i])count++;
                if(commands.Contains(air[i])?(order[15+i]!=air[i]||count!=1):count!=0)
                    throw new InvalidOperationException("Rhodes aviation command is outside its native hangar slot: "+air[i]);
            }
        }
        static void VerifyProductionPages(Context carrier)
        {
            object hud=Read(carrier.Hud,"m_Attributes");
            Dictionary<string,ScriptableObject> commands=new Dictionary<string,ScriptableObject>(StringComparer.Ordinal);
            foreach(ScriptableObject command in carrier.Commands)commands.Add(command.name,command);
            foreach(ProductionItem product in carrier.Products)commands.Add(product.AbilityKey,product.Ability);
            foreach(object context in (Array)Read(hud,"m_HUDContexts"))
            {
                string name=(string)Read(context,"m_ContextName");string[] order=(string[])Read(context,"m_AbilityOrder");
                if(order==null||order.Length!=20)throw new InvalidOperationException("HUD page lost its native 20-slot geometry");
                VerifyAviationCommandSlots(order,commands.Keys);
                Dictionary<KeyCode,string> hotkeys=new Dictionary<KeyCode,string>();
                foreach(string key in order)
                {
                    if(string.IsNullOrEmpty(key))continue;ScriptableObject command;
                    if(!commands.TryGetValue(key,out command))throw new InvalidOperationException("HUD references an unprepared ability: "+key);
                    AbilityViewAttributes view=Read(command,"m_View") as AbilityViewAttributes;
                    if(view==null)throw new InvalidOperationException("Ability view missing: "+key);
                    KeyCode hotkey=(KeyCode)Read(view,"Hotkey");
                    if(key==HeavyBomberLaunchKey&&hotkey!=KeyCode.X)throw new InvalidOperationException("Heavy-bomber launch lost its X hotkey");
                    if(hotkey!=KeyCode.None){string other;if(hotkeys.TryGetValue(hotkey,out other))throw new InvalidOperationException("HUD hotkey collision on "+name+": "+key+" / "+other+" / "+hotkey);hotkeys.Add(hotkey,key);}
                }
                foreach(ProductionItem product in carrier.Products)
                    if((Array.IndexOf(order,product.AbilityKey)>=0)!=(name==RhodesHudLayoutPolicy.ProductPage(product.UnitKey)))
                        throw new InvalidOperationException("Product is on the wrong HUD page: "+product.UnitKey+" / "+name);
                bool fighterPresent=Array.IndexOf(order,AircraftProductionKey)>=0;
                if(fighterPresent!=(name=="Strike"))throw new InvalidOperationException("Interceptor production is not exclusive to aviation page");
                if(commands.ContainsKey("Ability_EF_Rhodes_ProduceHeavyBomber")&&
                    (Array.IndexOf(order,"Ability_EF_Rhodes_ProduceHeavyBomber")>=0)!=(name=="Strike"))
                    throw new InvalidOperationException("Heavy-bomber production is not exclusive to aviation page");
            }
        }
        static void ConfigureSeparateAirRows(object controller)
        {
            object settings=Read(controller,"mSettings");if(settings==null)return;
            RhodesOperatorStatusUI.Bind(controller);
            string[] originalFighter=(string[])Read(settings,"m_HangarInterceptorTypes");
            string[] originalBomber=(string[])Read(settings,"m_HangarBomberTypes");
            string[] originalGunship=(string[])Read(settings,"m_HangarGunShipTypes");
            string[] fighter=RhodesHudLayoutPolicy.AircraftTypes(originalFighter,AircraftKey,FactionUnits.TiltStrikeKey,AircraftKey);
            string[] bomber=RhodesHudLayoutPolicy.AircraftTypes(originalBomber,AircraftKey,FactionUnits.TiltStrikeKey,null);
            string[] gunship=RhodesHudLayoutPolicy.AircraftTypes(originalGunship,AircraftKey,FactionUnits.TiltStrikeKey,FactionUnits.TiltStrikeKey);
            // B01 uses the native bomber grid once, even if this bounded bind
            // repeats after a session transition. Native factions are retained.
            string bomberKey="EF_Rhodes_HeavyBomber_MP";
            fighter=RhodesHudLayoutPolicy.AircraftTypes(fighter,bomberKey,null,null);
            gunship=RhodesHudLayoutPolicy.AircraftTypes(gunship,bomberKey,null,null);
            bomber=RhodesHudLayoutPolicy.AircraftTypes(bomber,bomberKey,null,bomberKey);
            // U08 ground cargo uses its own native-slot management adapter;
            // the original carrier-only bomber grid remains an aircraft row.
            if(RhodesHudLayoutPolicy.Equal(originalFighter,fighter)&&RhodesHudLayoutPolicy.Equal(originalBomber,bomber)&&RhodesHudLayoutPolicy.Equal(originalGunship,gunship))return;
            object copy=CopyData(settings,new Dictionary<object,object>(new IdentityComparer()));
            Write(copy,"m_HangarInterceptorTypes",fighter);Write(copy,"m_HangarBomberTypes",bomber);Write(copy,"m_HangarGunShipTypes",gunship);
            Write(controller,"mSettings",copy);
            Runtime.Log("RHODES_HANGAR_UI_CLASSIFIED fighterNativeGrid=True; tiltNativeGunshipGrid=True; passengerDedicatedNativeSlotPanel=True; passengerPortraitSource=EndfieldNativeAlpha; customAircraftTypesExclusiveAcrossRows=True; nativeOtherTypesPreserved=True; privateControllerSettings=True; simulationBayAndAircraftClassUnchanged=True; runtimeVisualAcceptancePending=True");
        }
    }
}
