using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Explicit F8 test requests only. No tick-based damage, direct HP writes,
    // world selection scan, weapon enable, healing or automatic target orders.
    public static class RhodesManualDamage
    {
        public const string SupportKey="C_SupportCruiser_MP";
        static readonly CommanderID Player=new CommanderID(1);
        static readonly object Gate=new object();
        static readonly Queue<Entity> Pending=new Queue<Entity>();
        static readonly HashSet<Entity> PendingIDs=new HashSet<Entity>();
        static int LastInputFrame=-1;
        const int MaximumPending=32;

        public static void Reset()
        {lock(Gate){Pending.Clear();PendingIDs.Clear();LastInputFrame=-1;}}

        public static void Request(Entity entity)
        {
            if(!Runtime.RhodesCarrier||entity.Equals(Entity.None))return;
            lock(Gate)Enqueue(entity);
        }
        static void Enqueue(Entity entity)
        {if(Pending.Count<MaximumPending&&PendingIDs.Add(entity))Pending.Enqueue(entity);}

        // Runtime calls this only during the active manual Rhodes match.
        // The cached UnitView list is touched only on the F8 key-down frame;
        // native support cruisers do not require a custom model/view driver.
        public static void MainTick()
        {
            if(!Runtime.RhodesCarrier||!Input.GetKeyDown(KeyCode.F8)||UICamera.inputHasFocus||Runtime.PresentationDeltaTime<=0f)return;
            lock(Gate){if(LastInputFrame==Time.frameCount)return;LastInputFrame=Time.frameCount;}
            foreach(UnitView view in UnitViewDiscovery.Get())
            {
                if(view==null||!view.gameObject.activeInHierarchy||view.UnitAttrs==null||!view.Selected||view.CommanderID!=Player||view.EntityID.Equals(Entity.None))continue;
                UnitState state=view.NonInterpolatedTickState;if(state==null||state.UnitIsDead||!AllowedKey(state.TypeID))continue;
                Request(view.EntityID);
            }
        }

        public static void SimTick(object sim)
        {
            HashSet<Entity> requested;
            lock(Gate){if(Pending.Count==0)return;requested=new HashSet<Entity>(Pending);Pending.Clear();PendingIDs.Clear();}
            if(!Runtime.RhodesCarrier||sim==null)return;
            // No pending input means no enumeration. A batch uses one native
            // UnitManager pass, even when both selected ships were requested.
            foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");if(!requested.Remove(entity))continue;
                if(!AllowedKey((string)NativeRead.Get(unit,"TypeName"))||
                    (CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity)!=Player||
                    Convert.ToBoolean(NativeRead.Get(unit,"PresentationOnly")))continue;
                UnitState state=(UnitState)NativeRead.Call(unit,"GetState");
                UnitAttributes attributes=(UnitAttributes)NativeRead.Get(unit,"Attributes");
                if(state==null||state.UnitIsDead||state.Hitpoints<=1||attributes==null||attributes.MaxHealth<=0)continue;
                int damage=CalculateDamage(attributes.MaxHealth,state.Hitpoints);if(damage<=0)continue;
                Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
                object command=NativeRead.Call(factory,"CreateDamageEntityCommand",Player,new Entity[]{entity},damage);
                bool accepted=(bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount"));
                Runtime.Log("RHODES_MANUAL_DAMAGE entity="+entity+"; explicitF8=True; maxHealth="+attributes.MaxHealth+
                    "; healthBeforeNativeCommand="+state.Hitpoints+"; damageRequested="+damage+
                    "; minimumRemainingHP=1; accepted="+accepted+"; nativeDamageCommand=True; directHPWrites=0");
            }
        }
        static int CalculateDamage(int maximum,int current)
        {if(maximum<=0||current<=1)return 0;return Math.Min(Math.Max(1,maximum/4),current-1);}
        static bool AllowedKey(string key){return key==RhodesCarrierUnits.UnitKey||key==SupportKey;}
    }
}
