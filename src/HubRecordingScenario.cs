using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Optional fixed recording layout. Spawn once; native movement/combat/death only.
    public static class HubRecordingScenario
    {
        static bool Loaded, Active, Complete;
        static Vector2r Lead, Wing, Guard;
        static Entity[] Fleet;
        static readonly CommanderID Player=new CommanderID(1), Enemy=new CommanderID(2);
        public static bool Enabled { get { Load();return Active; } }
        public static Vector2r PlayerStart { get { Load();return Lead; } }
        public static Vector2r FacingTarget { get { Load();return Guard; } }

        static void Load()
        {
            if(Loaded)return;Loaded=true;
            if(!Runtime.HubRegion)return;
            string path=Path.Combine(CustomUnits.AssetDirectory,"hub_recording_v01.ini");
            if(!File.Exists(path))return;
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string raw in File.ReadAllLines(path))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#",StringComparison.Ordinal))continue;
                int split=line.IndexOf('=');if(split<=0)throw new InvalidDataException("Malformed recording setting");
                values.Add(line.Substring(0,split).Trim(),line.Substring(split+1).Trim());
            }
            if(values["enabled"]!="true")return;
            Lead=Point(values["friendlyLead"]);Wing=Point(values["friendlyWing"]);Guard=Point(values["enemyGuard"]);
            if((Lead-Wing).Length<Fixed64.FromInt(10)||(Lead-Guard).Length<Fixed64.FromInt(150))
                throw new InvalidDataException("Recording layout has unsafe initial spacing");
            Active=true;
            Runtime.Log("HUB_RECORDING_CONFIG lead="+Lead+"; wing="+Wing+"; guard="+Guard+"; friendlies=2; enemies=1; noRespawn=True; nativeCombat=True");
        }
        static Vector2r Point(string text)
        {
            string[] parts=text.Split(',');if(parts.Length!=2)throw new InvalidDataException("Expected X,Z");
            float x=float.Parse(parts[0],CultureInfo.InvariantCulture),z=float.Parse(parts[1],CultureInfo.InvariantCulture);
            if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(z)||float.IsInfinity(z))throw new InvalidDataException("Nonfinite recording position");
            return new Vector2r(Fixed64.UnsafeFromFloat(x),Fixed64.UnsafeFromFloat(z));
        }
        public static Entity[] EnsureRoster(object sim,Entity initial)
        {
            if(!Enabled)return CustomUnits.AdoptStartingBvm(sim,initial);
            if(Complete)return Fleet;
            object map=NativeRead.Get(sim,"Map");
            foreach(Vector2r point in new[]{Lead,Wing,Guard})ValidateFootprint(map,point);
            object leader=Find(sim,initial);
            if(leader==null)return null;
            if((Position(leader)-Lead).Length>Fixed64.FromInt(8))throw new InvalidOperationException("Recording lead spawn differs: "+Position(leader));
            Entity[] adopted=CustomUnits.AdoptStartingBvm(sim,initial);if(adopted==null)return null;
            Entity second=Spawn(sim,Player,Wing,Guard-Wing,"EF_HUB_RECORDING_WING");
            Entity enemy=Spawn(sim,Enemy,Guard,Lead-Guard,"EF_HUB_RECORDING_GUARD");
            int friendlyCount=0,enemyCount=0;
            foreach(object unit in Units(sim))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");
                CommanderID owner=(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity);
                if(owner!=Player&&owner!=Enemy)continue;
                if((string)NativeRead.Get(unit,"TypeName")!=CustomUnits.UnitKey)throw new InvalidOperationException("Unexpected unit in recording scene");
                if(owner==Player)friendlyCount++;else enemyCount++;
                Runtime.Log("HUB_RECORDING_UNIT entity="+entity+"; owner="+owner+"; position="+Position(unit)+"; hp="+((UnitState)NativeRead.Call(unit,"GetState")).Hitpoints);
            }
            if(friendlyCount!=2||enemyCount!=1)throw new InvalidOperationException("Recording roster is not 2 friendly / 1 hostile");
            Fleet=new[]{initial,second};Complete=true;
            Runtime.Log("HUB_RECORDING_READY friendly=2; hostile=1; guardEntity="+enemy+"; nativeDamageOnly=True; autoRespawn=False; playerMoveOrders=0; scriptedEnemyAdvance=False; mapAndNavigationUnchanged=True");
            return Fleet;
        }
        static void ValidateFootprint(object map,Vector2r point)
        {
            int[,] offsets={{0,0},{3,0},{-3,0},{0,3},{0,-3}};
            for(int i=0;i<offsets.GetLength(0);i++)
            {
                Vector2r p=point+new Vector2r(Fixed64.FromInt(offsets[i,0]),Fixed64.FromInt(offsets[i,1]));
                if(!(bool)NativeRead.Call(map,"MapPositionValid",p,UnitClass.Ground))throw new InvalidOperationException("Recording footprint blocked at "+p);
            }
        }
        static Entity Spawn(object sim,CommanderID owner,Vector2r position,Vector2r direction,string tag)
        {
            var before=new HashSet<Entity>();foreach(object unit in Units(sim))before.Add((Entity)NativeRead.Get(unit,"Entity"));
            Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
            object command=NativeRead.Call(factory,"CreateSpawnUnitsCommand",owner,new[]{CustomUnits.UnitKey},new[]{tag},position,Orientation2.FromDirection(direction));
            if(!(bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount")))throw new InvalidOperationException("Recording native spawn rejected: "+tag);
            Entity result=Entity.None;int count=0;
            foreach(object unit in Units(sim))
            {
                Entity candidate=(Entity)NativeRead.Get(unit,"Entity");if(before.Contains(candidate))continue;
                if((string)NativeRead.Get(unit,"TypeName")!=CustomUnits.UnitKey||(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",candidate)!=owner)
                    throw new InvalidOperationException("Unexpected recording spawned identity");
                if((Position(unit)-position).Length>Fixed64.FromInt(8))throw new InvalidOperationException("Recording native spawn displaced beyond tolerance");
                count++;result=candidate;
            }
            if(count!=1)throw new InvalidOperationException("Recording native spawn did not create exactly one unit");
            return result;
        }
        static IEnumerable Units(object sim){return (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units");}
        static object Find(object sim,Entity id){foreach(object unit in Units(sim))if(((Entity)NativeRead.Get(unit,"Entity")).Equals(id))return unit;return null;}
        static Vector2r Position(object unit){return ((UnitState)NativeRead.Call(unit,"GetState")).StartOfFrameDynamicState.Position;}
    }
}
