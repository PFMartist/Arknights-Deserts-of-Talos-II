using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Explicit manual test damage, never an automatic combat rule. Keypresses
    // hand off entity IDs only; native DamageEntityCommand changes native HP.
    public static class HumanoidCasualtyProbe
    {
        static readonly object Gate=new object();
        static readonly Queue<Entity> Pending=new Queue<Entity>();
        static readonly CommanderID Player=new CommanderID(1);
        public static void Reset(){lock(Gate)Pending.Clear();}
        public static void Request(Entity entity){if(Runtime.SkirmishMode)return;lock(Gate){if(Pending.Count<16)Pending.Enqueue(entity);}}
        public static void SimTick(object sim)
        {
            if(Runtime.SkirmishMode)return;
            Entity entity;
            while(Take(out entity))
            {
                foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
                {
                    string key=(string)NativeRead.Get(unit,"TypeName");
                    if(!entity.Equals((Entity)NativeRead.Get(unit,"Entity"))||(key!=HumanoidUnits.GuardSquadKey&&key!=HumanoidUnits.PerlicaKey))continue;
                    if((CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity)!=Player)continue;
                    UnitState state=(UnitState)NativeRead.Call(unit,"GetState");if(state.UnitIsDead||state.Hitpoints<=0)continue;
                    int damage=HumanoidUnits.GuardSquadHitpoints/HumanoidUnits.GuardMemberCount;
                    Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
                    object command=NativeRead.Call(factory,"CreateDamageEntityCommand",Player,new Entity[]{entity},damage);
                    bool accepted=(bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount"));
                    Runtime.Log("HUMANOID_MANUAL_CASUALTY_TEST entity="+entity+"; damage="+damage+"; accepted="+accepted+"; explicitCtrlAltK=True; nativeDamageCommand=True");
                    break;
                }
            }
        }
        static bool Take(out Entity entity){lock(Gate){if(Pending.Count>0){entity=Pending.Dequeue();return true;}}entity=Entity.None;return false;}
    }
}
