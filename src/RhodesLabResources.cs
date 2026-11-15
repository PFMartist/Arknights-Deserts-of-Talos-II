using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using ResourceAttributes=BBI.Game.Data.ResourceAttributes;

namespace EndfieldDoK.MapUnitLab
{
    // One-time native map entities, not periodic resource grants. Native
    // Resource/Harvester/Storage and the normal resource view own everything
    // after creation, including depletion, delivery, icons and fog of war.
    public static class RhodesLabResources
    {
        public const int PatchCount=2,CUNodeCount=8,RUNodeCount=4,CUPerNode=6000,RUPerNode=2000;
        static bool Complete;
        static readonly Type Creator=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.SceneEntityCreator",true);
        static readonly Type ResourceComponent=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.Resource",true);
        static readonly MethodInfo ReadResource=FindResourceReader();
        sealed class Template
        {
            public string Key;
            public ResourceAttributes Attributes;
            public DetectableAttributes Detectable;
            public int StartingAmount,MaxHarvesters;
        }
        public static void Reset(){Complete=false;}
        public static void Ensure(object sim,Vector2r origin,Orientation2 orientation)
        {
            if(Complete)return;
            if(sim==null)throw new ArgumentNullException("sim");
            EntityTypeCollection registry=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");
            if(registry==null)throw new InvalidOperationException("Native resource definitions are unavailable");
            Template cu=FindTemplate(registry,ResourceType.Resource1),ru=FindTemplate(registry,ResourceType.Resource2);
            Vector2r forward=orientation.Forward,right=orientation.Right,left=new Vector2r(-right.X,-right.Y);
            int[] patchForward={800,1500},patchLeft={650,1050};
            int[] cuForward={-100,-100,100,100},cuLeft={-100,100,-100,100};
            HashSet<Entity> created=new HashSet<Entity>();
            for(int patch=0;patch<PatchCount;patch++)
            {
                for(int i=0;i<4;i++)
                    Spawn(sim,registry,cu,ResourceType.Resource1,CUPerNode,
                        Point(origin,forward,patchForward[patch]+cuForward[i],left,patchLeft[patch]+cuLeft[i]),orientation,
                        "EF_RHODES_CU_"+patch+"_"+i,created);
                for(int i=0;i<2;i++)
                    Spawn(sim,registry,ru,ResourceType.Resource2,RUPerNode,
                        Point(origin,forward,patchForward[patch]+280,left,patchLeft[patch]+(i==0?-100:100)),orientation,
                        "EF_RHODES_RU_"+patch+"_"+i,created);
            }
            if(created.Count!=CUNodeCount+RUNodeCount)throw new InvalidOperationException("Native resource node count mismatch");
            VerifyTemplate(registry,cu);VerifyTemplate(registry,ru);Complete=true;
            Runtime.Log("RHODES_RESOURCE_RANGE_READY patches="+PatchCount+"; CU_nodes="+CUNodeCount+"; RU_nodes="+RUNodeCount+
                "; CU_perNode="+CUPerNode+"; RU_perNode="+RUPerNode+"; CU_nativeType="+cu.Key+"; RU_nativeType="+ru.Key+
                "; nativeResourceComponentVerified=True; nativeModelAndIconRequired=True; neutralEntities=True; nativeGatherReturnAndDeposit=True"+
                "; nativeDefinitionsUnchanged=True; grantToCommanderBank=False; scriptedGatherOrders=0; recurringObserver=False; depletionAndDeliveryRuntimeAcceptancePending=True");
        }
        static Template FindTemplate(EntityTypeCollection registry,ResourceType type)
        {
            List<string> keys=new List<string>();
            foreach(string key in registry.GetAllEntityTypeNames())keys.Add(key);
            keys.Sort(StringComparer.Ordinal);
            foreach(string key in keys)
            {
                EntityTypeAttributes entity=registry.GetEntityType(key);
                ResourceAttributes attributes=entity.Get<ResourceAttributes>();
                if(attributes==null||attributes.ResourceType!=type||entity.Get<UnitAttributes>()!=null)continue;
                DetectableAttributes detectable=entity.Get<DetectableAttributes>();
                SceneEntityViewAttributes view=entity.Get<SceneEntityViewAttributes>();
                SceneEntityHUDAttributes hud=entity.Get<SceneEntityHUDAttributes>();
                // ReferenceEquals avoids Unity object/native calls from the
                // simulation thread. The registered assets themselves stay intact.
                if(detectable==null||view==null||hud==null||object.ReferenceEquals(view.ModelPrefab,null)||object.ReferenceEquals(hud.IconPrefab,null))continue;
                return new Template{Key=key,Attributes=attributes,Detectable=detectable,
                    StartingAmount=attributes.StartingAmount,MaxHarvesters=attributes.MaxHarvesters};
            }
            throw new InvalidOperationException("No registered native "+type+" node with a real model, icon and detectable definition");
        }
        static void Spawn(object sim,EntityTypeCollection registry,Template template,ResourceType type,int amount,Vector2r point,Orientation2 orientation,string tag,HashSet<Entity> created)
        {
            int harvesters=Math.Max(1,Math.Min(3,template.MaxHarvesters>0?template.MaxHarvesters:3));
            ResourceAttributes attributes=new ResourceAttributesData(type,amount,harvesters);
            ResourcePointDescriptor descriptor=SceneEntityDescriptor.CreateResourcePointDescriptor(template.Key,point,false,Fixed64.Zero,
                orientation,Vector3r.Zero,new string[]{tag},false,null,attributes,template.Detectable,false,Vector2r.Zero,Fixed64.Zero);
            Entity entity=(Entity)NativeRead.Call(Creator,"CreateResourcePointFromDescriptor",descriptor);
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            if(entity.Equals(Entity.None)||!created.Add(entity)||system==null||!system.ContainsEntity(entity))
                throw new InvalidOperationException("Native resource creation failed or duplicated "+tag);
            // Slot 11 is the actual native resource component ID in this
            // pinned build's SceneEntityCreator.CreateResourcePoint IL.
            object resource=ReadResource.Invoke(system,new object[]{entity,11});
            if(resource==null||(ResourceType)NativeRead.Get(resource,"ResourceType")!=type||
                (int)NativeRead.Get(resource,"RemainingAmount")!=amount||(int)NativeRead.Get(resource,"MaxAmount")!=amount||
                (bool)NativeRead.Get(resource,"Disabled")||(bool)NativeRead.Get(resource,"Depleted"))
                throw new InvalidOperationException("Created resource is not an active native harvestable node: "+tag);
            VerifyTemplate(registry,template);
            Runtime.Log("RHODES_RESOURCE_NODE_READY tag="+tag+"; entity="+entity+"; type="+type+"; position="+point+
                "; amount="+amount+"; maxHarvesters="+harvesters+"; nativeType="+template.Key+"; nativeTerrainHeightAndFOW=True");
        }
        static void VerifyTemplate(EntityTypeCollection registry,Template template)
        {
            ResourceAttributes current=registry.GetEntityType(template.Key).Get<ResourceAttributes>();
            if(!object.ReferenceEquals(current,template.Attributes)||current.StartingAmount!=template.StartingAmount||current.MaxHarvesters!=template.MaxHarvesters)
                throw new InvalidOperationException("Native resource template was modified");
        }
        static MethodInfo FindResourceReader()
        {
            foreach(MethodInfo method in typeof(IEntitySystem).GetMethods())
                if(method.Name=="GetComponent"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==2)
                    return method.MakeGenericMethod(ResourceComponent);
            throw new InvalidOperationException("Pinned native resource component reader unavailable");
        }
        static Vector2r Point(Vector2r origin,Vector2r a,int alongA,Vector2r b,int alongB)
        {
            Vector2r point=origin+a*Fixed64.FromInt(alongA)+b*Fixed64.FromInt(alongB);
            if(Math.Abs(Fixed64.UnsafeFloatValue(point.X))>3700f||Math.Abs(Fixed64.UnsafeFloatValue(point.Y))>3700f)
                throw new InvalidOperationException("Resource test point exceeds the training-map safety margin: "+point);
            return point;
        }
    }
}
