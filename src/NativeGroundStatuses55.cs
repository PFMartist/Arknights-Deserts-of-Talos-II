using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Immutable construction metadata cloned with the authored unit wrapper.
    public sealed class NativeGroundStatusBinding55:MonoBehaviour
    {
        public string UnitKey;
        public string[] DefinitionNames,AliasNames;
        public int[] FrillTypes;
    }

    // Native UnitFXController and OneTimeFXSpawner own live state, visibility,
    // LOD, pause, FX parameters, pooling and death. This adds no frame host.
    internal static class NativeGroundStatuses55
    {
        const string RootName="EF_NativeGroundStatuses55";
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly Dictionary<string,Transform> Templates=new Dictionary<string,Transform>(StringComparer.Ordinal);
        static GameObject Holder;
        internal static bool HasDeferredRetirement {get;private set;}

        internal static bool StatusType(int type,bool artifact)
        {
            switch(type)
            {
                case 500:case 800:case 810:case 820:case 1110:case 1215:case 1220:case 1230:case 1850:case 2630:return true;
                case 700:return artifact;
                default:return false;
            }
        }

        internal static void AppendFaction(UnitViewAttributes target,UnitViewAttributes source,FactionModelBinding binding)
        {
            if(binding==null||binding.Air)return;
            UnitViewAttributes donor=source;
            try
            {
                if(binding.UnitKey==FactionUnits.ArtilleryKey)
                {
                    object container=FactionUnits.GetSource(FactionUnits.AirDefenceKey);
                    donor=container==null?null:NativeRead.Get(NativeRead.Get(container,"m_View"),"Data") as UnitViewAttributes;
                    if(donor==null)throw new InvalidOperationException("Medium AA status donor missing for U09");
                }
                Append(target,donor,binding.GeometryRoot,binding.ModelBounds,binding.UnitKey,binding.UnitKey==FactionUnits.SurveyKey,false);
            }
            catch(Exception e){Warn(binding.UnitKey,e);}
        }

        internal static void AppendCarrier(UnitViewAttributes target,UnitViewAttributes source,RhodesCarrierBinding binding)
        {
            try
            {
                if(binding==null||!binding.IsV05)throw new InvalidOperationException("Owned carrier geometry missing");
                // Authored selection bounds describe the hull envelope without
                // using padded render bounds or guessing a deck height.
                Append(target,source,binding.GeometryRoot,binding.PickBounds,RhodesCarrierUnits.UnitKey,false,true);
            }
            catch(Exception e){Warn("RhodesCarrier",e);}
        }

        static void Append(UnitViewAttributes target,UnitViewAttributes source,Transform geometry,Bounds targetBounds,string key,bool artifact,bool carrier)
        {
            if(target==null||source==null||object.ReferenceEquals(target,source)||target.ModelPrefab==null||source.ModelPrefab==null||
                geometry==null||!geometry.IsChildOf(target.ModelPrefab.transform)||Memberwise==null)
                throw new InvalidOperationException("Status feedback requires a private authored view");
            if(geometry.Find(RootName)!=null)return;
            if(HasDeferredRetirement)throw new InvalidOperationException("Private status pool retirement is pending");
            if(!UnitScale(geometry.lossyScale))throw new InvalidOperationException("Status geometry is not metre space");
            Bounds sourceBounds=GeometryBounds(source.ModelPrefab.transform);
            NativeStatusBounds55 oldBox=Box(sourceBounds),newBox=Box(targetBounds);
            if(!NativeGroundStatusLayout55.Valid(oldBox)||!NativeGroundStatusLayout55.Valid(newBox))
                throw new InvalidOperationException("Status layout bounds are invalid");
            List<EffectDefinition> result=new List<EffectDefinition>();List<string> aliases=new List<string>();List<int> types=new List<int>();
            EffectDefinition[] previous=target.UnitEffectDefs;bool committed=false;
            GameObject root=new GameObject(RootName);root.layer=geometry.gameObject.layer;root.transform.parent=geometry;
            root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;
            try
            {
                int originalIndex=0,empty=0;
                foreach(EffectDefinition original in source.UnitEffectDefs)
                {
                    int index=originalIndex++;
                    if(original==null||!StatusType((int)original.FXType,artifact)||original.FXPrefab==null)continue;
                    // Authored None/empty death placeholders are not working
                    // smoke and must not be made active to fill a checklist.
                    if(original.FXPrefab.name=="FX_PRFB_Empty_Effect"||original.FXPrefab.GetComponentsInChildren<ParticleSystem>(true).Length==0)
                    {empty++;continue;}
                    if(original.BoneRef==null||original.BoneRef.Length==0)throw new InvalidOperationException("Status source has no emitters: "+original.Name);
                    if(original.FXPrefab.GetComponent<FXElement>()==null)throw new InvalidOperationException("Native status prefab has no FXElement");
                    if(!UnitScale(original.FXPrefab.localScale)||original.FXPrefab.localPosition.sqrMagnitude>.000001f)
                        throw new InvalidOperationException("Status prefab root offset/scale contract changed");
                    for(int boneIndex=0;boneIndex<original.BoneRef.Length;boneIndex++)
                    {
                        Transform oldBone=FindUnique(source.ModelPrefab.transform,original.BoneRef[boneIndex]);
                        Vector3 oldPoint=source.ModelPrefab.transform.InverseTransformPoint(oldBone.position);
                        Quaternion oldRotation=Quaternion.Inverse(source.ModelPrefab.transform.rotation)*oldBone.rotation;
                        NativeStatusVector55 mapped;
                        if(!NativeGroundStatusLayout55.TryMapBone(oldBox,newBox,Vector(oldPoint),out mapped))
                            throw new InvalidOperationException("Status bone coordinate mapping failed");
                        string alias="EF_Status55_"+key+"_D"+index+"_B"+boneIndex;
                        GameObject node=new GameObject(alias);node.layer=geometry.gameObject.layer;node.transform.parent=root.transform;
                        node.transform.localPosition=UnityVector(mapped);node.transform.localRotation=oldRotation;node.transform.localScale=Vector3.one;
                        Transform template=Template(key,index,boneIndex,original.FXPrefab,oldPoint,oldRotation,oldBox,newBox);
                        EffectDefinition copy=(EffectDefinition)Memberwise.Invoke(original,null);
                        copy.Name="EF_Status55_"+key+"_D"+index+"_B"+boneIndex+"_"+original.Name;
                        copy.BoneRef=new string[]{alias};copy.FXPrefab=template;
                        result.Add(copy);aliases.Add(alias);types.Add((int)original.FXType);
                    }
                }
                if(result.Count==0)throw new InvalidOperationException("Native status whitelist empty");
                List<EffectDefinition> all=new List<EffectDefinition>();if(target.UnitEffectDefs!=null)all.AddRange(target.UnitEffectDefs);
                foreach(EffectDefinition existing in all)if(existing!=null&&StatusType((int)existing.FXType,artifact))
                    throw new InvalidOperationException("Status feedback already exists outside the owned group");
                all.AddRange(result);
                NativeGroundStatusBinding55 tag=root.AddComponent<NativeGroundStatusBinding55>();tag.UnitKey=key;
                tag.DefinitionNames=new string[result.Count];for(int i=0;i<result.Count;i++)tag.DefinitionNames[i]=result[i].Name;
                tag.AliasNames=aliases.ToArray();tag.FrillTypes=types.ToArray();
                target.UnitEffectDefs=all.ToArray();committed=true;
                Runtime.Log("RHODES_NATIVE_STATUSES55_READY key="+key+"; definitions="+result.Count+"; sourceEmitterCountRetained=True; privatePrefabLayouts=True; sourceBoneAndPrefabRotationsRetained=True; layout=NativeMeshBoundsToAuthoredBounds; mediumU09="+(key==FactionUnits.ArtilleryKey)+"; artifact="+artifact+
                    "; emptyPlaceholdersSkipped="+empty+"; carrier="+carrier+"; nativeHealthStatusZeroHealthEvents=True; nativeFowLODParamsPoolAndDeath=True; particleCurvesAndBudgetsUnchanged=True; thermalAndWeaponFXExcluded=True; simulationWrites=0; customUpdates=0; actualVisualAcceptancePending=True");
            }
            catch
            {
                if(committed)target.UnitEffectDefs=previous;
                UnityEngine.Object.DestroyImmediate(root);throw;
            }
        }

        static Transform Template(string key,int index,int boneIndex,Transform original,Vector3 oldBone,Quaternion oldRotation,NativeStatusBounds55 oldBox,NativeStatusBounds55 newBox)
        {
            string cache=key+"/"+index+"/"+boneIndex+"/"+original.GetInstanceID()+"/"+BoundsKey(newBox);
            Transform existing;if(Templates.TryGetValue(cache,out existing))return existing;
            if(Holder==null)
            {
                Holder=new GameObject("EF_NativeStatusTemplates55");Holder.SetActive(false);Holder.hideFlags=HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(Holder);
            }
            GameObject clone=(GameObject)UnityEngine.Object.Instantiate(original.gameObject);clone.SetActive(false);
            clone.name="EF_Status55_"+key+"_D"+index+"_B"+boneIndex+"_"+original.name;
            clone.hideFlags=HideFlags.HideAndDontSave;clone.transform.parent=Holder.transform;
            clone.transform.localPosition=original.localPosition;clone.transform.localRotation=original.localRotation;clone.transform.localScale=original.localScale;
            try
            {
                AdaptChildren(original,clone.transform,original,clone.transform,Vector(oldBone),Rotation(oldRotation),Rotation(original.localRotation),oldBox,newBox);
                Component[] before=original.GetComponentsInChildren<Component>(true),after=clone.GetComponentsInChildren<Component>(true);
                if(before.Length!=after.Length)throw new InvalidOperationException("Private status clone component inventory differs");
                for(int i=0;i<before.Length;i++)if(before[i].GetType()!=after[i].GetType())throw new InvalidOperationException("Private status component type differs");
                ParticleSystem[] oldParticles=original.GetComponentsInChildren<ParticleSystem>(true),newParticles=clone.GetComponentsInChildren<ParticleSystem>(true);
                if(oldParticles.Length!=newParticles.Length)throw new InvalidOperationException("Status particle system count changed");
                for(int i=0;i<oldParticles.Length;i++)if(oldParticles[i].maxParticles!=newParticles[i].maxParticles)
                    throw new InvalidOperationException("Status particle budget changed");
                Templates.Add(cache,clone.transform);return clone.transform;
            }
            catch{UnityEngine.Object.DestroyImmediate(clone);throw;}
        }

        static void AdaptChildren(Transform original,Transform copy,Transform oldRoot,Transform newRoot,
            NativeStatusVector55 bone,NativeStatusRotation55 boneRotation,NativeStatusRotation55 prefabRotation,
            NativeStatusBounds55 oldBox,NativeStatusBounds55 newBox)
        {
            if(original.childCount!=copy.childCount)throw new InvalidOperationException("Status hierarchy changed while cloning");
            for(int i=0;i<original.childCount;i++)
            {
                Transform a=original.GetChild(i),b=copy.GetChild(i);
                if(a.name!=b.name)throw new InvalidOperationException("Status clone sibling order differs");
                NativeStatusVector55 mappedBone,mappedChild;
                if(!NativeGroundStatusLayout55.TryMapChild(oldBox,newBox,bone,boneRotation,prefabRotation,
                    Vector(oldRoot.InverseTransformPoint(a.position)),out mappedBone,out mappedChild))
                    throw new InvalidOperationException("Private status child mapping failed");
                b.localPosition=b.parent.InverseTransformPoint(newRoot.TransformPoint(UnityVector(mappedChild)));
                // Keep local rotations/scales, particle modules and native
                // component references exactly as Unity's private clone made them.
                AdaptChildren(a,b,oldRoot,newRoot,bone,boneRotation,prefabRotation,oldBox,newBox);
            }
        }

        internal static int VerifyCarrierPrefix(UnitViewAttributes target,UnitViewAttributes source,int offset)
        {
            RhodesCarrierBinding binding=target==null||target.ModelPrefab==null?null:target.ModelPrefab.GetComponent<RhodesCarrierBinding>();
            Transform root=binding==null||binding.GeometryRoot==null?null:binding.GeometryRoot.Find(RootName);
            if(root==null)return 0;
            NativeGroundStatusBinding55 tag=root.GetComponent<NativeGroundStatusBinding55>();
            if(tag==null||tag.UnitKey!=RhodesCarrierUnits.UnitKey||tag.DefinitionNames==null||tag.AliasNames==null||tag.FrillTypes==null||
                tag.DefinitionNames.Length!=tag.AliasNames.Length||tag.FrillTypes.Length!=tag.DefinitionNames.Length)
                throw new InvalidOperationException("Carrier status metadata is incomplete");
            int count=0;
            foreach(EffectDefinition original in source.UnitEffectDefs)
            {
                if(original==null||!StatusType((int)original.FXType,false)||original.FXPrefab==null||original.FXPrefab.name=="FX_PRFB_Empty_Effect"||
                    original.FXPrefab.GetComponentsInChildren<ParticleSystem>(true).Length==0)continue;
                foreach(string ignored in original.BoneRef)
                {
                    if(count>=tag.DefinitionNames.Length||target.UnitEffectDefs.Length<=offset+count)throw new InvalidOperationException("Carrier status prefix truncated");
                    EffectDefinition copy=target.UnitEffectDefs[offset+count];
                    if(copy==null||object.ReferenceEquals(copy,original)||object.ReferenceEquals(copy.BoneRef,original.BoneRef)||copy.Name!=tag.DefinitionNames[count]||
                        (int)copy.FXType!=tag.FrillTypes[count]||copy.FXType!=original.FXType||copy.ActiveAtLOD!=original.ActiveAtLOD||copy.Sound!=original.Sound||copy.PolyGroup!=original.PolyGroup||
                        copy.RaycastDir!=original.RaycastDir||copy.FollowNLIPSScale!=original.FollowNLIPSScale||copy.InheritRaycastNormal!=original.InheritRaycastNormal||
                        copy.FXPrefab==null||copy.FXPrefab==original.FXPrefab||copy.FXPrefab.parent!=Holder.transform||copy.BoneRef==null||copy.BoneRef.Length!=1||copy.BoneRef[0]!=tag.AliasNames[count]||
                        CountNamed(binding.GeometryRoot,copy.BoneRef[0])!=1)
                        throw new InvalidOperationException("Carrier status isolation/native fields contract changed");
                    count++;
                }
            }
            if(count!=tag.DefinitionNames.Length||count!=6)throw new InvalidOperationException("Carrier native status inventory changed");
            return count;
        }

        static Bounds GeometryBounds(Transform root)
        {
            Bounds result=new Bounds();bool found=false;
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh=null;SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;
                if(skin!=null)mesh=skin.sharedMesh;
                else if(renderer is MeshRenderer){MeshFilter filter=renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
                if(mesh==null||mesh.vertexCount==0)continue;
                Bounds b=mesh.bounds;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 p=b.center+new Vector3((corner&1)==0?-b.extents.x:b.extents.x,(corner&2)==0?-b.extents.y:b.extents.y,(corner&4)==0?-b.extents.z:b.extents.z);
                    p=root.InverseTransformPoint(renderer.transform.TransformPoint(p));
                    if(!found){result=new Bounds(p,Vector3.zero);found=true;}else result.Encapsulate(p);
                }
            }
            if(!found)throw new InvalidOperationException("Native status source geometry bounds missing");
            return result;
        }
        static Transform FindUnique(Transform root,string name)
        {
            Transform found=null;foreach(Transform node in root.GetComponentsInChildren<Transform>(true))if(node.name==name)
            {if(found!=null)throw new InvalidOperationException("Ambiguous native status bone: "+name);found=node;}
            if(found==null)throw new InvalidOperationException("Native status bone missing: "+name);return found;
        }
        static int CountNamed(Transform root,string name)
        {int n=0;foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)n++;return n;}
        static bool UnitScale(Vector3 s){return (s-Vector3.one).sqrMagnitude<.000001f;}
        static NativeStatusVector55 Vector(Vector3 v){return new NativeStatusVector55(v.x,v.y,v.z);}
        static Vector3 UnityVector(NativeStatusVector55 v){return new Vector3((float)v.X,(float)v.Y,(float)v.Z);}
        static NativeStatusRotation55 Rotation(Quaternion q){return new NativeStatusRotation55(q.x,q.y,q.z,q.w);}
        static NativeStatusBounds55 Box(Bounds b){return new NativeStatusBounds55(Vector(b.min),Vector(b.max));}
        static string BoundsKey(NativeStatusBounds55 b)
        {return string.Join(",",new string[]{Number(b.Min.X),Number(b.Min.Y),Number(b.Min.Z),Number(b.Max.X),Number(b.Max.Y),Number(b.Max.Z)});}
        static string Number(double n){return n.ToString("R",CultureInfo.InvariantCulture);}
        static void Warn(string key,Exception e){Runtime.Log("RHODES_NATIVE_STATUSES55_DISABLED key="+key+"; scope=UnitStatusPresentationOnly; reason="+e.Message);}
        internal static void Reset()
        {
            // DetachElement schedules a delayed despawn. Retire only each exact
            // private prefab pool before dropping its source. Shared native FX
            // pools/materials and unrelated active effects stay untouched.
            List<string> released=new List<string>();HasDeferredRetirement=false;
            foreach(KeyValuePair<string,Transform> entry in Templates)
            {
                Transform template=entry.Value;
                try
                {
                    if(!object.ReferenceEquals(template,null))
                    {
                        RhodesThermalVisuals.RetirePrivatePool(template);
                        RhodesThermalVisuals.ForgetTemplateTracker(template);
                    }
                    if(template!=null)UnityEngine.Object.DestroyImmediate(template.gameObject);
                    released.Add(entry.Key);
                }
                catch(Exception error)
                {HasDeferredRetirement=true;Warn("template_retirement_deferred",error);}
            }
            foreach(string key in released)Templates.Remove(key);
            if(!HasDeferredRetirement&&Templates.Count==0)
            {if(Holder!=null)UnityEngine.Object.DestroyImmediate(Holder);Holder=null;}
        }
    }
}
