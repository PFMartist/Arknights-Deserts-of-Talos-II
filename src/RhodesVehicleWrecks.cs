using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BBI.Core.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using BBI.Unity.Game.World;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // One optional visual preparation queue per native registry generation.
    // Native UnitView destruction, audio, air crash, scoring, FX pool, carcass
    // queue and lifetime remain authoritative. Nothing is scheduled on SimTick.
    internal static class RhodesVehicleWrecks
    {
        sealed class Spec
        {
            internal readonly string Id,Key;internal readonly bool Carrier;
            internal Spec(string id,string key,bool carrier){Id=id;Key=key;Carrier=carrier;}
        }
        sealed class Item
        {
            internal Spec Spec;internal UnitDeathAttributes Own,Source;
            internal UnitDeathAttributes.UnitDestructionAttributes Death;
            internal GameObject Original,Donor,Prefab;internal DeathSnapshot OwnBefore,SourceBefore;
            internal Material Template;internal RhodesLoadedModel[] Models;internal int Loading;
            internal string ContactColliderPath,Stage;internal Type ContactColliderType;
            internal object ConfiguredPool;
        }
        sealed class DeathSnapshot
        {
            readonly GameObject Carcass,Burst,Ping;readonly bool Skip,Parented;readonly float Delay,RandomDelay;
            internal DeathSnapshot(UnitDeathAttributes.UnitDestructionAttributes d)
            {Carcass=d.CarcassPrefab;Burst=d.InitialBurstPrefab;Ping=d.DeathPingPrefab;Skip=d.CannotSkipDeathSequence;Parented=d.InitialBurstParentedToUnit;Delay=d.ModelSwapDelayTime;RandomDelay=d.ModelSwapAdditionalRandomDelay;}
            internal void Require(UnitDeathAttributes.UnitDestructionAttributes d,bool carcass)
            {
                if((carcass&&!object.ReferenceEquals(Carcass,d.CarcassPrefab))||!object.ReferenceEquals(Burst,d.InitialBurstPrefab)||!object.ReferenceEquals(Ping,d.DeathPingPrefab)||
                    Skip!=d.CannotSkipDeathSequence||Parented!=d.InitialBurstParentedToUnit||Delay!=d.ModelSwapDelayTime||RandomDelay!=d.ModelSwapAdditionalRandomDelay)
                    throw new InvalidOperationException("Death presentation fields changed outside the optional carcass reference");
            }
        }
        static readonly Spec[] Specs={
            new Spec("U04",FactionUnits.EscortKey,false),new Spec("F01",RhodesCarrierUnits.AircraftKey,false),
            new Spec("U08",FactionUnits.TiltStrikeKey,false),new Spec("U01",FactionUnits.HarvesterKey,false),
            new Spec("U05",FactionUnits.SuppressionKey,false),new Spec("U07",FactionUnits.AirDefenceKey,false),
            new Spec("U09",FactionUnits.ArtilleryKey,false),new Spec("B01",RhodesCarrierUnits.HeavyBomberKey,false),
            new Spec("U06",FactionUnits.RailgunKey,false),new Spec("U02",FactionUnits.SurveyKey,false),
            new Spec("U03",FactionUnits.EngineeringKey,false),new Spec("C01",RhodesCarrierUnits.UnitKey,true)};
        static readonly string[] CarrierAssetIds={"C01_base","C01_aviation","C01_gunnery"};
        static EntityTypeCollection Registry;
        static UnitAssetContainer CarrierToken;
        static readonly List<Item> Items=new List<Item>();
        static readonly Dictionary<string,Item> ById=new Dictionary<string,Item>(StringComparer.Ordinal);
        static GameObject Holder;static Item Pending;static RhodesModelBuildJob Job;
        static int Next,GenerationRetiredAt=-1;static bool GenerationFailed,RetirementFailed;
        static readonly HashSet<string> Warnings=new HashSet<string>();
        const BindingFlags InstanceFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly Func<Collider,Transform> ColliderNode=delegate(Collider collider){return collider.transform;};
        static readonly Func<Transform,Transform> ParentNode=delegate(Transform node){return node.parent;};
        static readonly Predicate<Transform> HasBody=delegate(Transform node){return node.GetComponent<Rigidbody>()!=null;};

        internal static void MainTick(object main)
        {
            if(!Runtime.RhodesCarrier||!FactionUnits.Ready||!RhodesCarrierUnits.Ready)return;
            UnitAssetContainer token=RhodesCarrierUnits.WreckContainer(RhodesCarrierUnits.UnitKey,false);
            if(object.ReferenceEquals(CarrierToken,token)&&(RhodesWreckPrefabPolicy.QueueDrained(Next,Specs.Length,Job!=null,Pending!=null)||GenerationFailed||RetirementFailed))return;
            try
            {
                EntityTypeCollection current=NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes") as EntityTypeCollection;
                if(current==null)return;
                if(!object.ReferenceEquals(Registry,current))
                {
                    if(Registry!=null&&Runtime.SkirmishMode&&!RhodesSkirmishRuntime.ReadyForMatch)return;
                    // Existing definitions lease the templates across matches.
                    // Their meshes/materials are released only at registry change.
                    if(!ReleaseGeneration())return;
                    Registry=current;CarrierToken=token;Next=0;GenerationFailed=false;RetirementFailed=false;Warnings.Clear();
                    RhodesDeploymentState.ClearWreckMemoForRegistry();
                }
                if(GenerationFailed||RetirementFailed)return;
                if(Job==null)
                {
                    if(Next>=Specs.Length)return;
                    Spec spec=Specs[Next++];
                    try{Pending=Capture(spec);BeginPendingModel();}
                    catch(Exception error){CancelPending();Warn(spec.Id,error,"capture_or_begin_model");return;}
                }
                // Exactly one bounded importer step per existing main tick:
                // managed IO/decode uses the established worker queue; Unity
                // nodes/materials/mesh uploads stay on the presentation thread.
                try
                {
                    if(Job.Step())return;
                    RhodesLoadedModel model=Job.TakeResult();Job.Dispose();Job=null;
                    Pending.Models[Pending.Loading++]=model;RhodesWreckContract.Require(model.Package);
                    if(Pending.Loading<Pending.Models.Length){BeginPendingModel();return;}
                    Complete(Pending);Items.Add(Pending);ById.Add(Pending.Spec.Id,Pending);Pending=null;
                    if(Next==Specs.Length)Runtime.Log("RHODES_WRECK_PREPARATION_COMPLETE preparedUnitTypes="+Items.Count+"; requestedUnitTypes=12; requestedAuthoredModels=14; nativeDeathAndFXLifetime=True; gameplayAndVisualAcceptancePending=True");
                }
                catch(Exception error)
                {
                    string id=Pending==null?"queue":Pending.Spec.Id,stage=Pending==null?"queue":Pending.Stage;CancelPending();Warn(id,error,stage);
                }
            }
            catch(Exception error){CancelPending();GenerationFailed=true;Warn("generation",error);}
        }
        static string Asset(string id){return Path.Combine(CustomUnits.AssetDirectory,"rhodes_wrecks_v01/"+id+"/wreck.bin");}
        static string ModelId(Spec spec,int index){return spec.Carrier?CarrierAssetIds[index]:spec.Id;}
        static void BeginPendingModel()
        {Pending.Stage="load_"+ModelId(Pending.Spec,Pending.Loading);Job=RhodesModelBuilder.BeginLoadWreck(Asset(ModelId(Pending.Spec,Pending.Loading)),Pending.Template,Pending.Donor.layer);}
        static Item Capture(Spec spec)
        {
            RequirePoolContract();
            if(spec.Carrier)RhodesWreckNativeBinding.RequireCarrierCacheContract();
            for(int i=0;i<(spec.Carrier?3:1);i++)
            {string file=Asset(ModelId(spec,i));if(!File.Exists(file))throw new FileNotFoundException("Optional authored wreck not installed",file);}
            UnitAssetContainer own=FactionUnits.GetContainer(spec.Key),source=FactionUnits.GetSource(spec.Key);
            if(own==null){own=RhodesCarrierUnits.WreckContainer(spec.Key,false);source=RhodesCarrierUnits.WreckContainer(spec.Key,true);}
            UnitDeathAttributes data=Death(own),native=Death(source);
            EntityTypeAttributes definition=Registry.GetEntityType(spec.Key);
            if(own==null||source==null||object.ReferenceEquals(own,source)||data==null||native==null||data.DeathAttributes==null||native.DeathAttributes==null||
                object.ReferenceEquals(data,native)||object.ReferenceEquals(data.DeathAttributes,native.DeathAttributes)||definition==null||!object.ReferenceEquals(definition.Get<UnitDeathAttributes>(),data))
                throw new InvalidOperationException("Current private death datum ownership could not be established");
            GameObject donor=data.DeathAttributes.CarcassPrefab;
            // C01 deliberately has no old carcass. Its native carrier donor
            // supplies only the visual lifecycle; its null burst/ping stay null.
            if(spec.Carrier&&donor==null)donor=native.DeathAttributes.CarcassPrefab;
            if(donor==null||donor.GetComponent<FXElement>()==null)throw new InvalidOperationException("Native carcass donor/FXElement missing");
            FXCarcassBase[] corpses=donor.GetComponentsInChildren<FXCarcassBase>(true);
            if(corpses.Length!=1||corpses[0].gameObject!=donor||(spec.Carrier?corpses[0].GetType()!=typeof(FXCarrierCarcass):corpses[0].GetType()!=typeof(FXCarcass)))
                throw new InvalidOperationException("Native ground/air/carrier carcass type contract differs");
            Material material=null;
            foreach(Renderer r in donor.GetComponentsInChildren<Renderer>(true))if(r is MeshRenderer||r is SkinnedMeshRenderer)
                {Material candidate=r.sharedMaterial;if(candidate!=null&&candidate.shader!=null&&candidate.shader.isSupported){material=candidate;break;}}
            if(material==null)throw new InvalidOperationException("Native carcass material template unavailable");
            Collider contact=null;
            if(!spec.Carrier)
            {
                Rigidbody body=donor.GetComponent<Rigidbody>();if(body==null)throw new InvalidOperationException("Native carcass root Rigidbody missing");
                contact=RootContactCollider(donor);
            }
            return new Item{Spec=spec,Own=data,Source=native,Death=data.DeathAttributes,Original=data.DeathAttributes.CarcassPrefab,Donor=donor,Template=material,
                OwnBefore=new DeathSnapshot(data.DeathAttributes),SourceBefore=new DeathSnapshot(native.DeathAttributes),
                ContactColliderPath=contact==null?null:ContactPath(donor.transform,contact.transform),ContactColliderType=contact==null?null:contact.GetType(),Models=new RhodesLoadedModel[spec.Carrier?3:1]};
        }
        static Collider RootContactCollider(GameObject root)
        {return RhodesWreckPrefabPolicy.RequireRootCollider(root.GetComponentsInChildren<Collider>(true),root.transform,ColliderNode,ParentNode,HasBody);}
        static string ContactPath(Transform root,Transform node)
        {
            List<string> names=new List<string>();
            for(int depth=0;depth<256;depth++)
            {
                if(node==root){names.Reverse();return string.Join("/",names.ToArray());}
                if(node==null)throw new InvalidOperationException("Native contact collider lies outside its own prefab");
                names.Add(node.name);node=node.parent;
            }
            throw new InvalidOperationException("Native contact collider path exceeds its inspection budget");
        }
        static UnitDeathAttributes Death(UnitAssetContainer container)
        {return container==null?null:NativeRead.Get(NativeRead.Get(container,"m_Death"),"m_Attributes") as UnitDeathAttributes;}

        static void Complete(Item item)
        {
            item.Stage="validate_before_clone";
            if(item.Donor==null)throw new InvalidOperationException("Native carcass donor expired before preparation completed");
            foreach(RhodesLoadedModel model in item.Models)
            {
                if(model==null||model.Root==null)throw new InvalidOperationException("Authored wreck model expired before preparation completed");
                RhodesWreckContract.Require(model.Package);
            }
            item.OwnBefore.Require(item.Death,true);item.SourceBefore.Require(item.Source.DeathAttributes,true);
            if(!object.ReferenceEquals(Registry.GetEntityType(item.Spec.Key).Get<UnitDeathAttributes>(),item.Own))throw new InvalidOperationException("Wreck generation expired before publication");
            if(Holder==null){Holder=new GameObject("EF_Rhodes_WreckTemplates");Holder.SetActive(false);Holder.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Holder);}
            GameObject root=null;
            try
            {
                // The carrier donor also contains FX helper meshes, so its
                // aggregate bounds are not a ground datum. C01 is explicitly
                // authored at ground zero and has no body to settle it later.
                float bottom=item.Spec.Carrier?0f:GeometryBounds(item.Donor).min.y;
                item.Stage="clone_native_donor";
                root=(GameObject)UnityEngine.Object.Instantiate(item.Donor);root.SetActive(false);
                root.name="EF_Rhodes_Wreck_"+item.Spec.Id;root.hideFlags=HideFlags.HideAndDontSave;
                root.transform.parent=Holder.transform;root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;
                FXCarcassBase corpse=root.GetComponent<FXCarcassBase>();
                if(corpse==null)throw new InvalidOperationException("Private carcass clone lost its native lifecycle component");
                PhysicMaterial contactMaterial=null;
                if(!item.Spec.Carrier)
                {
                    item.Stage="read_private_contact";
                    Collider contact=RootContactCollider(root);
                    if(contact.GetType()!=item.ContactColliderType||ContactPath(root.transform,contact.transform)!=item.ContactColliderPath)
                        throw new InvalidOperationException("Private carcass clone changed its authored root collider contract");
                    // Read from the private instance before replacing geometry.
                    // sharedMaterial preserves its borrowed native reference;
                    // null legitimately requests Unity's default material.
                    // Never call Collider.material (which can instantiate it).
                    contactMaterial=contact.sharedMaterial;
                }
                // The export is a baked static assembly. Do not simulate the
                // donor's now-invisible wheels, turrets and aircraft fragments.
                item.Stage="strip_private_donor";
                foreach(MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    // This native script has no namespace in the supported
                    // game assembly. Missing-script slots can also be null.
                    if(component!=null&&component.GetType().FullName=="FXExplosionBodies")UnityEngine.Object.DestroyImmediate(component);
                foreach(Collider collider in root.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
                foreach(Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))if(body.gameObject!=root)UnityEngine.Object.DestroyImmediate(body);
                foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if(renderer is MeshRenderer||renderer is SkinnedMeshRenderer)UnityEngine.Object.DestroyImmediate(renderer);
                foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))UnityEngine.Object.DestroyImmediate(filter);
                foreach(LODGroup lod in root.GetComponentsInChildren<LODGroup>(true))UnityEngine.Object.DestroyImmediate(lod);
                GameObject[] variantRoots=item.Spec.Carrier?new GameObject[3]:null;
                float[] variantHeights=item.Spec.Carrier?new float[3]:null;
                Bounds bounds=item.Models[0].PresentationBounds;
                string[] hashes=new string[item.Models.Length];
                item.Stage="attach_authored_visuals";
                for(int i=0;i<item.Models.Length;i++)
                {
                    RhodesLoadedModel model=item.Models[i];Transform visual=model.Root.transform;
                    visual.parent=root.transform;visual.localPosition=new Vector3(0,bottom,0);visual.localRotation=Quaternion.identity;visual.localScale=Vector3.one;
                    model.Root.name="AuthoredWreck_"+ModelId(item.Spec,i);hashes[i]=model.Package.ContentSha256;
                    // Every new pooled clone's native Awake must cache all three
                    // renderer sets. No frame/render/yield occurs between native
                    // Instantiate and synchronous BroadcastMessage(OnSpawned),
                    // whose binding selects exactly one root for this instance.
                    model.Root.SetActive(true);
                    if(i>0)bounds.Encapsulate(model.PresentationBounds);
                    if(variantRoots!=null){variantRoots[i]=model.Root;variantHeights[i]=model.PresentationBounds.size.y;}
                }
                bounds.center+=new Vector3(0,bottom,0);
                BoxCollider physicalCollider=null;
                Renderer primaryRenderer=null;
                if(!item.Spec.Carrier)
                {
                    item.Stage="native_primary_components";
                    Rigidbody body=root.GetComponent<Rigidbody>();if(body==null)throw new InvalidOperationException("Native physical carcass requires root Rigidbody");
                    BoxCollider box=root.AddComponent<BoxCollider>();box.center=bounds.center;box.size=bounds.size;box.isTrigger=false;box.sharedMaterial=contactMaterial;
                    physicalCollider=box;
                    // The native helper only searches the root and its direct
                    // children, despite its InChildren name. Authored renderers
                    // are deeper in their LOD hierarchy. Supply the primary
                    // Renderer needed by FXCarcass.Awake without moving those
                    // meshes or adding a draw: this disabled component has no
                    // MeshFilter or materials. Native mRenderer is read only in
                    // Awake; our pooled binding sets the full fade height.
                    MeshRenderer primary=root.AddComponent<MeshRenderer>();primary.enabled=false;primary.sharedMaterials=new Material[0];primaryRenderer=primary;
                    // Keep the native template's isKinematic/useGravity values.
                    // The inactive holder already prevents template simulation;
                    // FXCarcass and FXElement cache reset retain native control.
                }
                else ((FXCarrierCarcass)corpse).CarcassHeight=bounds.size.y;
                // Do not invoke native Awake below an inactive holder: its
                // default GetComponentsInChildren excludes inactive descendants.
                // Refresh only this template's caches explicitly. Newly pooled
                // roots have no inactive parent and run native Awake normally.
                item.Stage="refresh_native_caches";
                RefreshTemplateCaches(root,corpse,primaryRenderer,physicalCollider,bounds.size.y);
                RhodesWreckNativeBinding binding=root.AddComponent<RhodesWreckNativeBinding>();binding.FadeHeight=bounds.size.y;binding.WreckId=item.Spec.Id;
                binding.CarrierVariants=variantRoots;binding.CarrierFadeHeights=variantHeights;
                root.SetActive(true);item.Prefab=root;
                item.SourceBefore.Require(item.Source.DeathAttributes,true);item.OwnBefore.Require(item.Death,true);
                // UnitView caches the private UnitDeathAttributes object, then
                // reads this field at destruction time (native IL audit 50).
                item.Stage="publish_private_reference";item.Death.CarcassPrefab=root;
                item.OwnBefore.Require(item.Death,false);item.SourceBefore.Require(item.Source.DeathAttributes,true);
                Runtime.Log("RHODES_WRECK_PREPARED id="+item.Spec.Id+"; key="+item.Spec.Key+"; modelSha256="+string.Join(",",hashes)+
                    "; nativeClass="+corpse.GetType().Name+"; donor="+item.Donor.name+"; authoredRootContactCollider="+(item.Spec.Carrier?"not_applicable":item.ContactColliderPath==""?"root":item.ContactColliderPath)+
                    "; contactMaterialConfigured="+(contactMaterial!=null)+"; contactSource=privateCloneSharedMaterial; rootPrimaryRendererNoMesh="+(primaryRenderer!=null)+"; nativeBodyDefaultsPreserved=True; atlasMaterialsPrivate=True; nativeBurstAudioPingDelayRetained=True; nativeCarcassPoolLifetime=True; extraUpdate=False; visualAcceptancePending=True");
            }
            catch
            {
                if(object.ReferenceEquals(item.Death.CarcassPrefab,root))item.Death.CarcassPrefab=item.Original;
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);item.Prefab=null;throw;
            }
        }
        static Bounds GeometryBounds(GameObject root)
        {
            bool any=false;Bounds result=new Bounds();
            foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh!=null)
                Encapsulate(root.transform,filter.transform,filter.sharedMesh.bounds,ref result,ref any);
            foreach(SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Encapsulate(root.transform,renderer.transform,renderer.localBounds,ref result,ref any);
            if(!any)throw new InvalidOperationException("Native carcass geometry bounds unavailable");return result;
        }
        static void Encapsulate(Transform root,Transform child,Bounds bounds,ref Bounds result,ref bool any)
        {
            for(int i=0;i<8;i++)
            {
                Vector3 p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                p=root.InverseTransformPoint(child.TransformPoint(p));if(!any){result=new Bounds(p,Vector3.zero);any=true;}else result.Encapsulate(p);
            }
        }
        static void RefreshTemplateCaches(GameObject root,FXCarcassBase corpse,Renderer first,Collider collider,float height)
        {
            SetNativeField(typeof(FXCarcassBase),corpse,"mRenderers",root.GetComponentsInChildren<Renderer>(true));
            SetNativeField(typeof(FXCarcassBase),corpse,"mHeight",height);
            if(corpse is FXCarcass)
            {
                SetNativeField(typeof(FXCarcass),corpse,"mRenderer",first);
                SetNativeField(typeof(FXCarcass),corpse,"mCollider",collider);
                SetNativeField(typeof(FXCarcass),corpse,"mRigidBody",root.GetComponent<Rigidbody>());
            }
            // This cache is not serialized. The template is never spawned in
            // place; FXElement.Awake constructs it on each actual pooled root.
            SetNativeField(typeof(FXElement),root.GetComponent<FXElement>(),"mCache",null);
        }
        static void SetNativeField(Type type,object owner,string name,object value)
        {
            FieldInfo field=type.GetField(name,InstanceFlags);
            if(field==null||field.IsDefined(typeof(SerializeField),false))throw new InvalidOperationException("Native transient cache contract differs: "+type.Name+"."+name);
            field.SetValue(owner,value);
        }
        static void CancelPending()
        {
            if(Job!=null)Job.Dispose();Job=null;
            if(Pending!=null&&Pending.Models!=null)foreach(RhodesLoadedModel model in Pending.Models)if(model!=null)model.Dispose();Pending=null;
        }
        // Called only after native shutdown and the existing SimGate barrier.
        internal static void EndMatch()
        {
            if(Job!=null){CancelPending();if(Next>0)Next--;}
            foreach(Item item in Items)
                try{RetirePool(item.Prefab.transform);item.ConfiguredPool=null;}catch(Exception error){RetirementFailed=true;Warn(item.Spec.Id+"_retire",error);}
        }
        static bool ReleaseGeneration()
        {
            CancelPending();
            if(Items.Count>0&&GenerationRetiredAt<0)
            {
                foreach(Item item in Items)
                    try{if(item.Prefab!=null)RetirePool(item.Prefab.transform);}
                    catch(Exception error){RetirementFailed=true;Warn(item.Spec.Id+"_lease",error);return false;}
                // Native SelfDestruct uses Object.Destroy, which completes at
                // the frame boundary. Keep every model lease until then.
                GenerationRetiredAt=Time.frameCount;return false;
            }
            if(Items.Count>0&&Time.frameCount<=GenerationRetiredAt)return false;
            // Every private native pool is gone before destroying any shared
            // model lease; no donor/cached mesh or foreign prefab is destroyed.
            foreach(Item item in Items)
            {
                if(object.ReferenceEquals(item.Death.CarcassPrefab,item.Prefab))item.Death.CarcassPrefab=item.Original;
                if(item.Prefab!=null)UnityEngine.Object.DestroyImmediate(item.Prefab);
                foreach(RhodesLoadedModel model in item.Models)if(model!=null)model.Dispose();
            }
            Items.Clear();ById.Clear();if(Holder!=null)UnityEngine.Object.DestroyImmediate(Holder);Holder=null;Registry=null;CarrierToken=null;GenerationRetiredAt=-1;return true;
        }
        static void RequirePoolContract()
        {
            Type type=typeof(FXElement).Assembly.GetType("PathologicalGames.PrefabPool",true);
            foreach(string name in new string[]{"limitAmount","limitFIFO","limitInstances"})
            {
                FieldInfo field=type.GetField(name,InstanceFlags);
                if(field==null||field.FieldType!=(name=="limitAmount"?typeof(int):typeof(bool)))throw new MissingFieldException(type.FullName,name);
            }
        }
        internal static void NativeSpawned(string id)
        {
            Item item;if(id==null||!ById.TryGetValue(id,out item)||item.ConfiguredPool!=null)return;
            try
            {
                MethodInfo getter=typeof(FXElement).GetMethod("FXPool",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                object pool=getter==null?null:getter.Invoke(null,null);
                PropertyInfo property=pool==null?null:pool.GetType().GetProperty("prefabPools",BindingFlags.Instance|BindingFlags.Public);
                IDictionary pools=property==null?null:property.GetValue(pool,null) as IDictionary;
                object owned=pools==null?null:pools[item.Prefab.transform];
                if(owned==null)throw new InvalidOperationException("Spawned authored wreck has no native prefab pool");
                // A real native hard cap on active+inactive instances. FIFO is
                // deliberately off: forcibly recycling a live corpse behind
                // FXQueue would leave that native queue holding duplicate refs.
                SetPool(owned,"limitAmount",item.Spec.Carrier?4:32);SetPool(owned,"limitFIFO",false);SetPool(owned,"limitInstances",true);
                item.ConfiguredPool=owned;
                Runtime.Log("RHODES_WRECK_POOL_BOUNDED id="+id+"; nativePoolInstanceLimit="+(item.Spec.Carrier?4:32)+"; allTwelvePoolsMaximum=356; liveFIFOReuse=False; nativeGlobalCarcassQueueRetained=True; templateAtlasShared=True; atlasCPUReadable=False");
            }
            catch(Exception error){Warn(id+"_pool",error);}
        }
        static void SetPool(object pool,string name,object value)
        {FieldInfo field=pool.GetType().GetField(name,InstanceFlags);if(field==null)throw new MissingFieldException(pool.GetType().FullName,name);field.SetValue(pool,value);}
        static void RetirePool(Transform template)
        {
            if(object.ReferenceEquals(template,null))return;
            if(FXManager.Manager!=null&&template!=null)
            {
                MethodInfo getter=typeof(FXElement).GetMethod("FXPool",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                object pool;
                try{if(getter==null)throw new MissingMethodException("Native FXPool");pool=getter.Invoke(null,null);}
                catch(TargetInvocationException e){if(e.InnerException is KeyNotFoundException)pool=null;else throw;}
                if(pool!=null)
                {
                    PropertyInfo property=pool.GetType().GetProperty("prefabPools",BindingFlags.Instance|BindingFlags.Public);
                    IDictionary pools=property==null?null:property.GetValue(pool,null) as IDictionary;
                    if(pools==null)throw new InvalidOperationException("Native private prefab-pool map unavailable");
                    if(pools.Contains(template))
                    {
                        object owned=pools[template];Type type=owned.GetType();
                        MethodInfo stop=type.GetMethod("StopPreload",InstanceFlags),destruct=type.GetMethod("SelfDestruct",InstanceFlags);
                        FieldInfo spawned=type.GetField("_spawned",InstanceFlags);
                        MethodInfo despawn=pool.GetType().GetMethod("Despawn",InstanceFlags,null,new Type[]{typeof(Transform),typeof(object)},null);
                        if(stop==null||destruct==null||spawned==null||despawn==null)throw new InvalidOperationException("Native owned-pool retirement contract differs");
                        stop.Invoke(owned,null);List<Transform> active=new List<Transform>();
                        foreach(object value in (IEnumerable)spawned.GetValue(owned)){Transform t=value as Transform;if(t!=null)active.Add(t);}
                        foreach(Transform t in active)
                        {FXCarcassBase corpse=t.GetComponent<FXCarcassBase>();if(corpse!=null)FXManager.Manager.CarcassQueue.Remove(corpse);despawn.Invoke(pool,new object[]{t,t});}
                        pools.Remove(template);destruct.Invoke(owned,null);
                    }
                }
            }
            FieldInfo tracker=typeof(FXElement).GetField("sFXElementToCategoryTracker",BindingFlags.Static|BindingFlags.NonPublic);
            IDictionary categories=tracker==null?null:tracker.GetValue(null) as IDictionary;
            if(categories!=null&&categories.Contains(template))categories.Remove(template);
        }
        static void Warn(string id,Exception error,string stage="lifecycle")
        {
            if(!Warnings.Add(id))return;
            // One bounded diagnostic per id/generation, including empty-message
            // NullReferenceException stacks and reflection inner exceptions.
            string detail=error.ToString().Replace("\r"," ").Replace("\n"," | ");if(detail.Length>6000)detail=detail.Substring(0,6000);
            Runtime.Log("RHODES_WRECK_UNAVAILABLE id="+id+"; stage="+stage+"; previousNativeCarcassRetained=True; optionalVisualOnly=True; error="+detail);
        }
        internal static void CarrierSelectionWarning(Exception error){Warn("C01_form",error);}
    }

    // One-time native cache adjustment only. Static authored pieces need the
    // full assembly height for sinking, not the first LOD chunk's height.
    // There is deliberately no Update, coroutine, scene scan or per-death bake.
    public sealed class RhodesWreckNativeBinding:MonoBehaviour
    {
        public float FadeHeight;
        public string WreckId;
        public GameObject[] CarrierVariants;
        public float[] CarrierFadeHeights;
        static readonly FieldInfo Height=typeof(FXCarcassBase).GetField("mHeight",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        static readonly FieldInfo Cache=typeof(FXElement).GetField("mCache",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        static readonly FieldInfo InitialStates=Cache==null?null:Cache.FieldType.GetField("mInitialGameObjectStates",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        bool CarrierVisualsFailed;
        internal static void RequireCarrierCacheContract()
        {
            if(Cache==null||InitialStates==null||InitialStates.FieldType!=typeof(KeyValuePair<GameObject,bool>[])||
                Cache.IsDefined(typeof(SerializeField),false)||InitialStates.IsDefined(typeof(SerializeField),false))
                throw new InvalidOperationException("Native private FX initial-GameObject-state cache contract differs");
        }
        void OnSpawned(object dependencies)
        {
            if(CarrierVariants!=null&&CarrierVariants.Length==3)
            {
                int selected=0;
                try
                {
                    FXSpawnDependencies deps=dependencies as FXSpawnDependencies;
                    FXCarcassBase.CarcassSpawnInfo info;
                    if(deps!=null&&deps.Get<FXCarcassBase.CarcassSpawnInfo>(out info)&&info!=null&&info.SpawningUnit!=null)
                    {
                        // Entity comes from SpawnSettings; owner comes from the
                        // view's cached current UnitState. Neither queries Sim.
                        UnitView source=info.SpawningUnit.GetComponent<UnitView>();
                        if(source!=null&&source.GetComponent<RhodesCarrierBinding>()!=null)
                        {
                            RhodesDeploymentSnapshot state;
                            bool known=RhodesDeploymentState.TryGetForWreck(source.EntityID,source.CommanderID,out state);
                            selected=RhodesCarrierWreckSelection.FormIndex(known,state);
                            Runtime.Log("RHODES_CARRIER_WRECK_FORM entity="+source.EntityID+"; owner="+source.CommanderID+"; selected="+selected+
                                "; committedSnapshotFound="+known+"; settledSilhouettesOnly=True; sharedDeathDatumChanged=False; nativePoolKey=C01");
                        }
                    }
                }
                catch(Exception error){RhodesVehicleWrecks.CarrierSelectionWarning(error);}
                if(!CarrierVisualsFailed)
                    try{CommitCarrierMask(RhodesCarrierWreckSelection.VisibleMask(selected));}
                    catch(Exception error){QuarantineCarrierVisuals(error);}
                if(CarrierFadeHeights!=null&&CarrierFadeHeights.Length==3)FadeHeight=CarrierFadeHeights[selected];
            }
            FXCarcassBase corpse=GetComponent<FXCarcassBase>();
            if(corpse!=null&&Height!=null&&FadeHeight>0f)Height.SetValue(corpse,FadeHeight);
            RhodesVehicleWrecks.NativeSpawned(WreckId);
        }
        void OnDespawned(object data)
        {
            // Teardown can already have discarded the native cache or one of
            // the roots. Hide surviving visuals; do not rewrite saved cache or
            // quarantine a normal shutdown. OnSpawned commits the next selected
            // mask to cache and visuals in either native callback order.
            if(CarrierVariants!=null)ApplyCarrierMask(0);
        }
        void CommitCarrierMask(int mask)
        {
            RequireCarrierCacheContract();
            FXElement element=GetComponent<FXElement>();object cache=element==null?null:Cache.GetValue(element);
            KeyValuePair<GameObject,bool>[] states=cache==null?null:InitialStates.GetValue(cache) as KeyValuePair<GameObject,bool>[];
            // Native FXElement.OnSpawned.ResetCache can run before OR after this
            // sibling OnSpawned callback. Commit this instance's three saved GO
            // states as well as visibility so either order yields one form.
            RhodesCarrierWreckSelection.UpdateCachedRoots(states,CarrierVariants,mask);
            ApplyCarrierMask(mask);
        }
        void QuarantineCarrierVisuals(Exception error)
        {
            CarrierVisualsFailed=true;RhodesVehicleWrecks.CarrierSelectionWarning(error);
            // A changed/absent native cache must never leave all silhouettes
            // drawable if native ResetCache subsequently reactivates their GOs.
            // Touch only this instance's authored visual subtrees, no native FX.
            foreach(GameObject root in CarrierVariants)if(root!=null)
            {
                root.SetActive(false);
                foreach(LODGroup lod in root.GetComponentsInChildren<LODGroup>(true))lod.enabled=false;
                foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            }
        }
        void ApplyCarrierMask(int mask)
        {
            // Finish synchronously within the original native pool broadcast.
            // Renderer caches still contain all variants for later pool reuse;
            // inactive roots prevent FOW/LOD/fade from revealing a second form.
            for(int i=0;i<CarrierVariants.Length;i++)
            {
                GameObject root=CarrierVariants[i];bool shown=(mask&(1<<i))!=0;
                if(root!=null&&root.activeSelf!=shown)root.SetActive(shown);
            }
        }
    }
}
