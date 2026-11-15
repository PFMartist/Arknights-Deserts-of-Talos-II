using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.UI;

namespace EndfieldDoK.MapUnitLab
{
    // Version-pinned component and command ABI. Business code uses roles,
    // never a substring in an ability's name. All accesses remain read-only.
    public static class NativeUnitBindings
    {
        public const int Unit=2,OwningCommander=5,WeaponOperations=6,World=10,Movement=12,Production=15,Goals=19,Detectable=21,PowerShunt=22,Hangar=23,AirSortie=32;
        public const int WeaponDamageAttribute=262148,DamageReceivedAttribute=65550,SensorRangeAttribute=65542,ContactRangeAttribute=65543;
        public const int MaximumSpeedAttribute=131072,MinimumCruiseSpeedAttribute=131074;
        public enum CommandRole { Unknown,Stop,Move,AttackTarget,AttackMove,Return }
        static readonly object Gate=new object();
        static readonly Dictionary<string,CommandRole> Roles=new Dictionary<string,CommandRole>(StringComparer.Ordinal);
        static object RoleRegistry;
        static readonly Type EntityExtensions=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityComponentExtensions",true);
        static readonly MethodInfo ScheduleAbility=ResolveScheduleAbility();

        static MethodInfo ResolveScheduleAbility()
        {
            Type[] signature={typeof(string),typeof(List<Entity>),typeof(List<Entity>),typeof(Vector2r),typeof(AbilityTargetingType),typeof(SelectedUnitManager.AddMode)};
            MethodInfo method=typeof(UnitInterfaceController).GetMethod("TryScheduleUseAbilityCommand",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,signature,null);
            if(method==null||method.IsStatic||method.ReturnType!=typeof(bool))throw new InvalidOperationException("Native unit ability scheduler ABI differs");
            if((int)AbilityClass.Attack!=10||(int)AbilityClass.MoveTo!=50||(int)AbilityClass.ReturnToCarrier!=47||(int)AbilityClass.Stop!=80)
                throw new InvalidOperationException("Native ability role ABI differs");
            return method;
        }
        public static CommandRole Classify(AbilityClass type,bool hasTarget)
        {
            if(type==AbilityClass.Stop)return CommandRole.Stop;
            if(type==AbilityClass.MoveTo)return CommandRole.Move;
            if(type==AbilityClass.Attack)return hasTarget?CommandRole.AttackTarget:CommandRole.AttackMove;
            if(type==AbilityClass.ReturnToCarrier)return CommandRole.Return;
            return CommandRole.Unknown;
        }
        public static AbilityAttributes FindAbility(object sim,string key)
        {
            if(sim==null||string.IsNullOrEmpty(key))return null;
            object settings=NativeRead.Get(sim,"Settings"),types=settings==null?null:NativeRead.Get(settings,"EntityTypes");
            EntityTypeAttributes definition=types==null?null:NativeRead.Call(types,"GetEntityType",key) as EntityTypeAttributes;
            return definition==null?null:definition.Get<AbilityAttributes>();
        }
        public static CommandRole ResolveRole(object sim,string key,bool hasTarget)
        {
            if(sim==null||string.IsNullOrEmpty(key))return CommandRole.Unknown;
            object settings=NativeRead.Get(sim,"Settings"),types=settings==null?null:NativeRead.Get(settings,"EntityTypes");
            CommandRole role;
            lock(Gate)
            {
                if(!object.ReferenceEquals(RoleRegistry,types)){RoleRegistry=types;Roles.Clear();}
                if(!Roles.TryGetValue(key,out role))
                {
                    AbilityAttributes ability=FindAbility(sim,key);
                    role=ability==null?CommandRole.Unknown:Classify(ability.AbilityType,false);
                    if(Roles.Count<512)Roles[key]=role;
                }
            }
            return role==CommandRole.AttackMove&&hasTarget?CommandRole.AttackTarget:role;
        }
        public static CommanderID OwnerOf(Entity entity)
        {return entity==Entity.None||!(bool)NativeRead.Call(EntityExtensions,"IsValid",entity)?CommanderID.None:(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity);}
        public static bool SameOwner(Entity left,Entity right)
        {CommanderID owner=OwnerOf(left);return owner!=CommanderID.None&&owner==OwnerOf(right);}

        // Cargo-manager buttons use precisely the same native scheduler
        // and ability validation as the command bar. No local Pending bypass.
        public static bool ScheduleOperatorAbility(UnitInterfaceController ui,Entity source,string ability)
        {
            if(ui==null||source==Entity.None||(ability!=RhodesOperatorTransport.UnloadAbilityKey&&ability!=RhodesOperatorTransport.RecallAbilityKey&&ability!=RhodesOperatorTransport.ReturnAbilityKey&&
                !RhodesEmbarkedOperatorSkills.IsAutoToggleAbilityKey(ability)))return false;
            BBI.Game.Commands.SimStateFrame frame=ShipbreakersMain.CurrentSimFrame;
            UnitState state=frame==null?null:frame.FindObject<UnitState>(source);
            ICommanderInteractionProvider interaction=NativeRead.Get(ui,"mInteractionProvider") as ICommanderInteractionProvider;
            if(state==null||state.UnitIsDead||interaction==null||!interaction.LocalCommanderCanControl(state.OwnerCommander))return false;
            if(ability==RhodesOperatorTransport.ReturnAbilityKey?!RhodesOperatorRoster.Contains(state.TypeID):state.TypeID!=FactionUnits.TiltStrikeKey)return false;
            List<Entity> sources=new List<Entity>(1);sources.Add(source);
            AbilityTargetingType targeting=RhodesEmbarkedOperatorSkills.IsAutoToggleAbilityKey(ability)?AbilityTargetingType.Self:AbilityTargetingType.None;
            return (bool)ScheduleAbility.Invoke(ui,new object[]{ability,sources,null,Vector2r.Zero,targeting,SelectedUnitManager.AddMode.NewSet});
        }
    }

    public struct CommanderIdentityKey:IEquatable<CommanderIdentityKey>
    {
        public readonly CommanderID Commander;
        public readonly string Type;
        public CommanderIdentityKey(CommanderID commander,string type){Commander=commander;Type=type??string.Empty;}
        public bool Equals(CommanderIdentityKey other){return Commander==other.Commander&&string.Equals(Type,other.Type,StringComparison.Ordinal);}
        public override bool Equals(object other){return other is CommanderIdentityKey&&Equals((CommanderIdentityKey)other);}
        public override int GetHashCode(){return unchecked(Commander.GetHashCode()*397)^StringComparer.Ordinal.GetHashCode(Type??string.Empty);}
    }
}
