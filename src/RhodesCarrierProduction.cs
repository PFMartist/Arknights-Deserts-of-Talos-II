using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BBI.Core;
using BBI.Core.Data;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        public const string TankProductionKey="Ability_EF_Rhodes_ProduceBVM";
        public const string SquadProductionKey="Ability_EF_Rhodes_ProduceGuardSquad";
        public const int TankResource1Cost=400,TankResource2Cost=50,TankPopCapCost=3;
        public const int SquadResource1Cost=120,SquadResource2Cost=0,SquadPopCapCost=2;
        public const float TankProductionSeconds=20f,SquadProductionSeconds=10f;
        public const int ProductionQueueCount=1,ProductionQueueDepth=8;
        public static string[] ProductionAbilityKeys { get { List<string> keys=new List<string>();foreach(FactionUnits.UnitSpec spec in FactionUnits.Specs)keys.Add(spec.ProductionKey);return keys.ToArray(); } }

        sealed class ProductionItem
        {
            public string AbilityKey,UnitKey,Title,NonProductionFingerprint;
            public int Resource1,Resource2,Population;
            public float Seconds;
            public KeyCode Hotkey;
            public string[] Dependencies;
            public ScriptableObject Ability;
            public UnitAttributes Unit;
            public EntityTypeAttributes Definition;
        }

        static void PrepareProduction(Context c)
        {
            if(!FactionUnits.Ready)throw new InvalidOperationException("Register all private faction units before Rhodes production");
            if(c.Products.Count!=0)throw new InvalidOperationException("Rhodes production may be prepared only once per registry");
            ScriptableObject donor=null,rally=null;
            foreach(ScriptableObject ability in (Array)Read(c.Source,"m_Abilities"))
            {
                if(ability==null)continue;
                AbilityAttributes attributes=Read(ability,"m_Attributes") as AbilityAttributes;
                if(attributes==null)continue;
                if(attributes.AbilityType==AbilityClass.ProduceUnit&&attributes.ProduceUnit!=null&&attributes.ProduceUnit.UnitTypeToProduce==CustomUnits.ControlKey)
                { if(donor!=null)throw new InvalidOperationException("Multiple native carrier LAV production templates found");donor=ability; }
                if(attributes.AbilityType==AbilityClass.SetRallyPoint)
                { if(rally!=null)throw new InvalidOperationException("Multiple native carrier rally commands found");rally=ability; }
            }
            if(donor==null)throw new InvalidOperationException("Current native carrier lacks its real C_Escort_MP production ability");
            // A rally command is retained unchanged as a private local datum;
            // no shared native ability or global faction data is patched.
            if(rally!=null)
            {
                bool found=false;foreach(ScriptableObject command in c.Commands)if(command.name==rally.name)found=true;
                if(!found){ScriptableObject copy=CloneAsset(c,rally,rally.name);RemapAsset(c,copy);c.Commands.Add(copy);}
            }
            PrepareProductionPageCommands(c);
            foreach(FactionUnits.UnitSpec spec in FactionUnits.Specs)
            {
                string[] dependencies=string.IsNullOrEmpty(spec.Research)?new string[0]:new string[]{spec.Research};
                AddProduct(c,donor,new ProductionItem{AbilityKey=spec.ProductionKey,UnitKey=spec.Key,Title=spec.Title,Resource1=spec.Resource1,Resource2=spec.Resource2,Population=spec.Population,Seconds=spec.Seconds,Hotkey=spec.Hotkey,Dependencies=dependencies});
            }
            Array previous=(Array)Read(c.Custom,"m_Abilities");
            Array all=Array.CreateInstance(previous.GetType().GetElementType(),c.Commands.Count+c.Products.Count);int next=0;
            foreach(ScriptableObject command in c.Commands)all.SetValue(command,next++);
            foreach(ProductionItem product in c.Products)all.SetValue(product.Ability,next++);
            Write(c.Custom,"m_Abilities",all);
            object core=Read(c.Core,"m_Attributes");
            Write(core,"m_NumProductionQueues",ProductionQueueCount);Write(core,"m_ProductionQueueDepth",ProductionQueueDepth);Write(core,"m_ShowProductionQueues",true);
            ConfigureProductionHUD(c);
            RhodesProductionExit.Prepare(c.Custom,Settings.Length);
            Runtime.Log("RHODES_PRODUCTION_PREPARED sourceTemplate="+donor.name+"; products="+FactionUnits.Specs.Length+"; queueCount=1; queueDepth=8; nativeQueue=True; bypassQueue=False"+
                "; targetUnitCostAndTimeOwned=True; nativeResearchAndOriginalCarrierUnchanged=True; rallyRetained="+(rally!=null)+
                "; formalFactionRoster=True; prototypeBVMAndSquadExcluded=True; U08RequiresC05=True; fighterUsesAviation1=True; economics=AdjustableNativeTrialDefaults; scriptedProductionSpawn=False; runtimeAcceptancePending=True");
        }

        static void AddProduct(Context c,ScriptableObject donor,ProductionItem item)
        {
            if(Registry.GetEntityType(item.AbilityKey)!=null)throw new InvalidOperationException("Private production ability key collision: "+item.AbilityKey);
            item.Definition=Registry.GetEntityType(item.UnitKey);item.Unit=item.Definition==null?null:item.Definition.Get<UnitAttributes>();
            FactionUnits.UnitSpec spec=FactionUnits.FindSpec(item.UnitKey);if(spec==null)throw new InvalidOperationException("Unknown faction product "+item.UnitKey);
            UnitAttributes native=Registry.GetEntityType(spec.Donor).Get<UnitAttributes>();
            if(item.Unit==null||object.ReferenceEquals(item.Unit,native)||item.Unit.GetType().FullName!="BBI.Game.Data.UnitAttributesData")
                throw new InvalidOperationException("Production target must be an already registered private custom unit: "+item.UnitKey);
            item.NonProductionFingerprint=FingerprintNonProduction(item.Unit);
            Write(item.Unit,"m_Resource1Cost",item.Resource1);Write(item.Unit,"m_Resource2Cost",item.Resource2);
            Write(item.Unit,"m_ProductionTime",item.Seconds);Write(item.Unit,"m_PopCapCost",item.Population);
            // One source template intentionally produces two independent clones.
            // Do not use the one-to-one asset map to return the first clone twice.
            bool recorded=false;foreach(SourceRecord record in c.Sources)if(object.ReferenceEquals(record.Asset,donor))recorded=true;
            if(!recorded)c.Sources.Add(new SourceRecord{Asset=donor,Fingerprint=Fingerprint(donor),Definition=Registry.GetEntityType(donor.name)});
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(donor.GetType());
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(donor.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(donor),copied));
            clone.name=item.AbilityKey;clone.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(clone);item.Ability=clone;
            RemapAsset(c,clone);
            object data=Read(clone,"m_Attributes"),view=Read(clone,"m_View");
            ((NamedObjectBase)data).InitializeName(item.AbilityKey);
            ((NamedObjectBase)view).InitializeName(item.AbilityKey);
            Write(Read(data,"m_ProduceUnit"),"m_UnitTypeToProduce",item.UnitKey);Write(Read(data,"m_ProduceUnit"),"m_BypassQueue",false);
            Write(Read(data,"m_ResearchDeps"),"m_DependentResearch",item.Dependencies);
            Write(Read(data,"m_Cost"),"m_Resource1Cost",0);Write(Read(data,"m_Cost"),"m_Resource2Cost",0);
            SetProductionEnum(data,"m_StartsRemovedIn",0);Write(data,"m_CooldownTimeSecs",0f);Write(data,"m_WarmupTimeSecs",0f);Write(data,"m_SharedCooldownChannel",0);
            Write(data,"m_CasterMovesToTarget",false);Write(data,"m_IsToggleable",false);Write(data,"m_CastOnDeath",false);
            Write(Read(data,"m_Charges"),"m_ChargesInventoryID",string.Empty);
            Write(Read(data,"m_Autocast"),"m_AutocastEnabled",false);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",false);
            object activation=Read(data,"m_ActivationDependencies");
            SetProductionEnum(activation,"m_ManeuveringDependencies",0);SetProductionEnum(activation,"m_CommanderHasUnitWithTargetables",0);Write(activation,"m_TakeNoDamageForMS",0);
            Write(view,"LocalizedTitleStringID","生产："+item.Title);
            Write(view,"LocalizedShortDescriptionStringID",spec.Description);
            Write(view,"LocalizedLongDescriptionStringID",spec.Description);
            Write(view,"Hotkey",item.Hotkey);Write(view,"BindingIsExclusive",false);Write(view,"ForcePassiveVisuals",false);
            UnitHUDAttributes hud=item.Definition.Get<UnitHUDAttributes>();
            if(hud!=null&&hud.IconSettings!=null&&!string.IsNullOrEmpty(hud.IconSettings.UnitProductionIconName))Write(view,"IconName",hud.IconSettings.UnitProductionIconName);
            object voice=Read(view,"VOFeedback");Write(voice,"DialogueCode",string.Empty);Write(view,"VOFeedback",voice);
            CallIfPresent(clone,"OnEnable");
            AbilityAttributes attributes=Read(clone,"m_Attributes") as AbilityAttributes;
            if(attributes==null||attributes.Name!=item.AbilityKey||attributes.AbilityType!=AbilityClass.ProduceUnit)
                throw new InvalidOperationException("Private production clone identity/type did not initialize");
            Registry.Add(ProductionAbilityDefinition(clone));c.Products.Add(item);
        }

        static EntityTypeAttributes ProductionAbilityDefinition(ScriptableObject asset)
        {
            EntityTypeAttributes definition=new EntityTypeAttributes(asset.name);object data=null;
            IEnumerable many=asset as IEnumerable;if(many==null){data=Read(asset,"Data");many=data as IEnumerable;}
            if(many!=null){foreach(object item in many)if(item!=null&&!definition.Add(item))throw new InvalidOperationException("Duplicate production datum "+item.GetType().FullName);}
            else if(data==null||!definition.Add(data))throw new InvalidOperationException("Production ability has no registry datum");
            return definition;
        }
        static void SetProductionEnum(object data,string field,int value)
        {object old=Read(data,field);Write(data,field,old.GetType().IsEnum?Enum.ToObject(old.GetType(),value):(object)value);}
        static void ConfigureProductionHUD(Context c)
        {RebuildProductionPages(c);}
        static void VerifyProduction(Context c)
        {
            if(c.Products.Count!=FactionUnits.Specs.Length)throw new InvalidOperationException("Rhodes must have the exact faction production roster");
            UnitAttributes carrier=(UnitAttributes)Read(c.Core,"m_Attributes");
            if(carrier.NumProductionQueues!=ProductionQueueCount||carrier.ProductionQueueDepth!=ProductionQueueDepth||!carrier.ShowProductionQueues)
                throw new InvalidOperationException("Rhodes native production queues are not enabled");
            foreach(ProductionItem product in c.Products)
            {
                if(!object.ReferenceEquals(Registry.GetEntityType(product.UnitKey),product.Definition)||!object.ReferenceEquals(product.Definition.Get<UnitAttributes>(),product.Unit))
                    throw new InvalidOperationException("Production target registration identity changed");
                if(product.Unit.Resource1Cost!=product.Resource1||product.Unit.Resource2Cost!=product.Resource2||product.Unit.PopCapCost!=product.Population||Math.Abs(Fixed64.UnsafeFloatValue(product.Unit.ProductionTime)-product.Seconds)>.001f)
                    throw new InvalidOperationException("Registered production target costs/time/population differ");
                if(FingerprintNonProduction(product.Unit)!=product.NonProductionFingerprint)throw new InvalidOperationException("Production setup changed custom unit combat/movement/core fields outside production metadata");
                EntityTypeAttributes standalone=Registry.GetEntityType(product.AbilityKey);AbilityAttributes attributes=Read(product.Ability,"m_Attributes") as AbilityAttributes;
                if(standalone==null||!object.ReferenceEquals(standalone.Get<AbilityAttributes>(),attributes)||!object.ReferenceEquals(standalone.Get<AbilityViewAttributes>(),Read(product.Ability,"m_View"))||attributes==null||attributes.Name!=product.AbilityKey||attributes.AbilityType!=AbilityClass.ProduceUnit||attributes.ProduceUnit.UnitTypeToProduce!=product.UnitKey||attributes.ProduceUnit.BypassQueue)
                    throw new InvalidOperationException("Standalone production registry or queued target is wrong");
                string[] deps=attributes.ResearchDependencies.RequiredResearch;
                if(deps.Length!=product.Dependencies.Length||attributes.Cost.Resource1Cost!=0||attributes.Cost.Resource2Cost!=0)
                    throw new InvalidOperationException("Production has incorrect research gates or duplicate cast cost");
                for(int i=0;i<deps.Length;i++)if(deps[i]!=product.Dependencies[i])throw new InvalidOperationException("Faction production prerequisite drift");

            }
            for(int i=0;i<c.Products.Count;i++)for(int j=i+1;j<c.Products.Count;j++)
                if(object.ReferenceEquals(Read(c.Products[i].Ability,"m_Attributes"),Read(c.Products[j].Ability,"m_Attributes"))||object.ReferenceEquals(Read(c.Products[i].Ability,"m_View"),Read(c.Products[j].Ability,"m_View")))
                    throw new InvalidOperationException("Faction products share mutable ability/view data");
            VerifyProductionPages(c);
            RhodesProductionExit.Verify(c.Custom,Settings.Length);
        }
        static string FingerprintNonProduction(UnitAttributes unit)
        {
            StringBuilder text=new StringBuilder();HashSet<object> seen=new HashSet<object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(unit.GetType()))
            {
                if(field.Name=="m_Resource1Cost"||field.Name=="m_Resource2Cost"||field.Name=="m_ProductionTime"||field.Name=="m_PopCapCost")continue;
                text.Append(field.DeclaringType.FullName).Append('.').Append(field.Name).Append('=');AppendValue(text,field.GetValue(unit),seen);
            }
            using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
        }
    }
}
