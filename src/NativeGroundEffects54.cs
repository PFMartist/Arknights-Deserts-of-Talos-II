using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Core.World;
using BBI.Unity.Game.Data;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Construction-only binding. The native direct-bone WheelContactRange
    // route already drives dust on Escort/HAC/Railgun/AA, whose WheelEffects
    // arrays are empty. Keep that route when suspension is disabled: unlike
    // wheel controllers it receives Grounded from ProcessFXTriggerStatesFromPhysics.
    // NativeGroundActivation56 completes the otherwise-stuck suspension wait
    // at live binding so that the native fixed tick can actually set that flag.
    // Native UnitFXController/EngineInputs own movement, visibility, LOD,
    // quality, parameters, scene pooling, death and reuse. No added Update.
    internal static class NativeGroundEffects54
    {
        const string RootName="EF_NativeGroundFX54";
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        internal sealed class Contact
        {
            internal Transform Parent;
            internal Vector3 Front,Rear,Center;
            internal string Provenance;
            internal float Width,ProbeLift;
        }

        internal static void AppendFaction(UnitViewAttributes target,UnitViewAttributes source,FactionModelBinding binding,FactionTrackPackage tracks)
        {
            if(binding==null||binding.Air)return;
            AppendMovementAudio(target,source,binding.UnitKey);
            GameObject root=null;List<GameObject> aliases=new List<GameObject>();
            try
            {
                RequirePrivate(target,source,binding.GeometryRoot);
                RequireNoSuspension(target);
                if(binding.GeometryRoot.Find(RootName)!=null)return;
                UnitViewAttributes dustSource=source;
                string donor=FactionUnits.FindSpec(binding.UnitKey).Donor;
                if(binding.UnitKey==FactionUnits.ArtilleryKey)
                {
                    // U09 shares the medium tracked chassis; never inherit a
                    // giant ArtilleryCruiser dust cloud merely for its weapon.
                    object container=FactionUnits.GetSource(FactionUnits.AirDefenceKey);
                    dustSource=container==null?null:NativeRead.Get(NativeRead.Get(container,"m_View"),"Data") as UnitViewAttributes;
                    if(dustSource==null)throw new InvalidOperationException("U09 medium AA dust donor unavailable");
                    donor="C_HAC_Upgrade01_MP";
                }
                List<Contact> contacts=tracks==null?WheelContacts(binding):TrackContacts(binding,tracks);
                root=NewRoot(binding.GeometryRoot);
                List<EffectDefinition> added=BindDust(dustSource,contacts,root.transform,binding.GeometryRoot,false,binding.UnitKey==FactionUnits.EscortKey,aliases);
                Append(target,added);
                Runtime.Log("RHODES_NATIVE_GROUND_FX54_READY key="+binding.UnitKey+"; dustDonor="+donor+"; definitions="+added.Count+"; contactGroups="+contacts.Count+
                    "; contacts="+ContactSources(contacts)+"; nativeEmitterBudgetsRetained=True; trigger=NativeWheelContactRange; driver=NativeDirectBoneUnitFX; nativeSpeedFowLODQualityPool=True; nativeRaycastPolicyRetained=True; suspension=False; bodyMotion=False; colliderWrites=0; customUpdates=0; visualAcceptancePending=True");
            }
            catch(Exception error)
            {
                DestroyAliases(aliases);
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                Runtime.Log("RHODES_NATIVE_GROUND_FX54_DISABLED key="+binding.UnitKey+"; scope=UnitDustOnly; reason="+error.Message);
            }
        }

        internal static void AppendCarrier(UnitViewAttributes target,UnitViewAttributes source,RhodesCarrierBinding binding)
        {
            GameObject root=null;List<GameObject> aliases=new List<GameObject>();
            try
            {
                if(binding==null||!binding.IsV05)throw new InvalidOperationException("Carrier authored track binding missing");
                RequirePrivate(target,source,binding.GeometryRoot);
                RequireNoSuspension(target);
                if(binding.GeometryRoot.Find(RootName)!=null)return;
                RhodesTrackRigBinding rig=binding.GeometryRoot.GetComponent<RhodesTrackRigBinding>();
                if(rig==null||rig.Groups==null||rig.Groups.Length==0)throw new InvalidOperationException("Carrier TKR track paths missing");
                List<Contact> contacts=new List<Contact>();
                foreach(RhodesTrackGroupState group in rig.Groups)
                {
                    if(group==null||group.Renderer==null||group.Path==null)throw new InvalidOperationException("Carrier TKR group incomplete");
                    Transform space=group.Renderer.transform.parent;
                    if(space==null||!space.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Carrier track space ownership differs");
                    float min=float.PositiveInfinity;RhodesTrackSegmentState bottom=null;
                    foreach(RhodesTrackSegmentState segment in group.Path)
                    {
                        float height=(segment.A.x+segment.B.x)*.5f;
                        if(segment.Kind==0&&height<min){min=height;bottom=segment;}
                    }
                    if(bottom==null)throw new InvalidOperationException("Carrier lower track straight absent");
                    if(group.Renderer.sharedMesh==null||group.Renderer.sharedMesh.vertexCount==0)throw new InvalidOperationException("Carrier actual track mesh missing");
                    // localBounds is the padded swept/culling envelope. Dust
                    // must use the actual rendered shoe geometry instead.
                    Bounds meshBounds=group.Renderer.sharedMesh.bounds;
                    float y=meshBounds.min.y;
                    float rear=Mathf.Min(bottom.A.y,bottom.B.y),front=Mathf.Max(bottom.A.y,bottom.B.y);
                    // LocalRight is the bogie's hull-space turn-arm, not its
                    // local contact X. The real skin bounds are in this space.
                    float x=meshBounds.center.x;
                    contacts.Add(new Contact{Parent=space,Front=new Vector3(x,y,front),Rear=new Vector3(x,y,rear),
                        Center=new Vector3(x,y,(front+rear)*.5f),Provenance=group.BogieName+"/TKR.lowerStraight"});
                }
                root=NewRoot(binding.GeometryRoot);
                List<EffectDefinition> added=BindDust(source,contacts,root.transform,binding.GeometryRoot,true,false,aliases);
                Append(target,added);
                Runtime.Log("RHODES_NATIVE_GROUND_FX54_READY key=RhodesCarrier; dustDonor=C_Carrier_MP; definitions="+added.Count+"; contactGroups="+contacts.Count+
                    "; contacts=TKR.lowerStraightPerBogie; nativeEmitterBudgetsRetained=True; dustEmitters=7; trigger=NativeWheelContactRange; wheelDustConvertedToDirectBones=True; driver=NativeDirectBoneUnitFX; nativeSpeedFowLODQualityPool=True; nativeRaycastPolicyRetained=True; suspension=False; bodyMotion=False; colliderWrites=0; customUpdates=0; visualAcceptancePending=True");
            }
            catch(Exception error)
            {
                DestroyAliases(aliases);
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                Runtime.Log("RHODES_NATIVE_GROUND_FX54_DISABLED key=RhodesCarrier; scope=CarrierDustOnly; reason="+error.Message);
            }
        }

        internal static List<Contact> WheelContacts(FactionModelBinding binding)
        {
            List<Contact> result=new List<Contact>();
            for(int i=0;i<binding.RigNodes.Length;i++)
            {
                if(binding.RigRoles[i]!=1||binding.RigLods[i]!=0)continue;
                Transform wheel=binding.Nodes[binding.RigNodes[i]];
                if(wheel==null||wheel.parent==null||!wheel.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Wheel role ownership differs");
                Vector3 center=binding.GeometryRoot.InverseTransformPoint(wheel.position);
                float bottom=float.PositiveInfinity,loX=float.PositiveInfinity,hiX=float.NegativeInfinity;
                // The FRG2 calibrated radius is metric. FRG1 uses the actual
                // descendant tire mesh bounds, never a guessed model radius.
                foreach(MeshFilter mesh in wheel.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(mesh.sharedMesh==null||mesh.sharedMesh.vertexCount==0)continue;
                    Bounds b=mesh.sharedMesh.bounds;
                    for(int c=0;c<8;c++)
                    {
                        Vector3 p=b.center+new Vector3((c&1)==0?-b.extents.x:b.extents.x,(c&2)==0?-b.extents.y:b.extents.y,(c&4)==0?-b.extents.z:b.extents.z);
                        Vector3 actual=binding.GeometryRoot.InverseTransformPoint(mesh.transform.TransformPoint(p));
                        bottom=Mathf.Min(bottom,actual.y);loX=Mathf.Min(loX,actual.x);hiX=Mathf.Max(hiX,actual.x);
                    }
                }
                if(binding.RigWheelRadii[i]>0f)bottom=center.y-binding.RigWheelRadii[i];
                if(float.IsInfinity(bottom)||bottom>=center.y)throw new InvalidOperationException("Wheel tire contact cannot be measured: "+wheel.name);
                Vector3 contact=new Vector3(center.x,bottom,center.z);
                // A sibling of the spin pivot follows steering/body placement
                // without revolving the emitter around the wheel each frame.
                Vector3 local=wheel.parent.InverseTransformPoint(binding.GeometryRoot.TransformPoint(contact));
                result.Add(new Contact{Parent=wheel.parent,Front=local,Rear=local,Center=local,Provenance=wheel.name+"/tireBottom",Width=hiX-loX,ProbeLift=center.y-bottom});
            }
            if(result.Count<2)throw new InvalidOperationException("No authored L0 tire contact set");
            return result;
        }

        internal static List<Contact> TrackContacts(FactionModelBinding binding,FactionTrackPackage package)
        {
            if(package.UnitKey!=binding.UnitKey||package.Belts==null||package.Belts.Length==0)throw new InvalidOperationException("FTK dust contact identity differs");
            List<Contact> result=new List<Contact>();
            foreach(FactionTrackBelt belt in package.Belts)
            {
                Transform pivot=null;
                for(int i=0;i<binding.Names.Length;i++)if(binding.Names[i]==belt.Node){if(pivot!=null)throw new InvalidOperationException("Duplicate FTK pivot");pivot=binding.Nodes[i];}
                if(pivot==null||!pivot.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("FTK contact pivot absent");
                float min=float.PositiveInfinity;FactionTrackSegment bottom=null;
                foreach(FactionTrackSegment segment in belt.Path)
                {
                    float height=(segment.AU+segment.BU)*.5f;
                    if(segment.Kind==0&&height<min){min=height;bottom=segment;}
                }
                if(bottom==null)throw new InvalidOperationException("FTK lower track straight absent");
                float rear=Mathf.Min(bottom.AF,bottom.BF),front=Mathf.Max(bottom.AF,bottom.BF);
                // FTK Bounds also contains culling padding. Measure the L0
                // authored shoe vertices once while constructing the template.
                if(belt.Skins==null||belt.Skins.Length==0||belt.Skins[0].Lod!=0||belt.Skins[0].Vertices==null||belt.Skins[0].Vertices.Length<10)
                    throw new InvalidOperationException("FTK actual lower shoe geometry missing");
                float[] vertices=belt.Skins[0].Vertices;float loX=float.PositiveInfinity,hiX=float.NegativeInfinity,y=float.PositiveInfinity,hiY=float.NegativeInfinity;
                for(int v=0;v<vertices.Length;v+=10){loX=Mathf.Min(loX,vertices[v]);hiX=Mathf.Max(hiX,vertices[v]);y=Mathf.Min(y,vertices[v+1]);hiY=Mathf.Max(hiY,vertices[v+1]);}
                float x=(loX+hiX)*.5f;
                result.Add(new Contact{Parent=pivot,Front=new Vector3(x,y,front),Rear=new Vector3(x,y,rear),Center=new Vector3(x,y,(front+rear)*.5f),Provenance=belt.Node+"/FTK.lowerStraight",Width=(hiX-loX)*Mathf.Abs(pivot.lossyScale.x),ProbeLift=(hiY-y)*Mathf.Abs(pivot.lossyScale.y)});
            }
            return result;
        }

        static List<EffectDefinition> BindDust(UnitViewAttributes source,List<Contact> contacts,Transform root,Transform geometry,bool carrier,bool nitrous,List<GameObject> aliases)
        {
            if(source==null||source.UnitEffectDefs==null||contacts.Count==0)throw new InvalidOperationException("Native dust definitions/contact set missing");
            List<EffectDefinition> result=new List<EffectDefinition>();
            Vector3 average=Vector3.zero;
            foreach(Contact c in contacts)
            {
                Vector3 p=geometry.InverseTransformPoint(c.Parent.TransformPoint(c.Center));average+=p;
            }
            average/=contacts.Count;
            int definitionIndex=0;
            foreach(EffectDefinition item in source.UnitEffectDefs)
            {
                if(item==null||item.FXPrefab==null||item.Name==null||item.Name.IndexOf("Dust",StringComparison.OrdinalIgnoreCase)<0)continue;
                bool nativeNitrous=nitrous&&item.FXType==FrillType.Nitrous&&(item.Name=="FX - Dust Trail Nitrous"||item.Name=="FX - Wheel Dust Front Nitrous");
                if(item.FXType!=FrillType.WheelContactRange&&!nativeNitrous)continue;
                // Exact native direct-bone definitions; carrier indirect wheel
                // dust uses this same trigger and prefab through direct bones.
                if(!carrier&&(item.BoneRef==null||item.BoneRef.Length==0))continue;
                bool body=item.Name.IndexOf("Body",StringComparison.OrdinalIgnoreCase)>=0||item.Name=="Dust Trail 2"||
                    item.BoneRef!=null&&item.BoneRef.Length==1&&item.BoneRef[0].IndexOf("Chas",StringComparison.OrdinalIgnoreCase)>=0;
                bool rear=item.Name.IndexOf("Back",StringComparison.OrdinalIgnoreCase)>=0||item.Name.IndexOf(" B Treads",StringComparison.Ordinal)>=0;
                bool front=item.Name.IndexOf("Front",StringComparison.OrdinalIgnoreCase)>=0||item.Name.IndexOf(" F Treads",StringComparison.Ordinal)>=0||
                    BoneContains(item,"Front")||BoneContains(item,"_FL")||BoneContains(item,"_FR");
                bool middle=item.Name.IndexOf(" M Treads",StringComparison.Ordinal)>=0;
                List<string> bones=new List<string>();
                int budget=NativeEmitterBudget(source,item,carrier);
                if(body)bones.Add(Alias(root,average,"D"+definitionIndex+"_Body",geometry,aliases));
                else
                {
                    bool useRear=rear||!front&&!middle;
                    int[] pair=PickPair(contacts,geometry,front,useRear,middle);
                    if(budget==1)
                    {
                        int centered=CenteredContact(contacts,geometry,front,useRear);
                        if(centered>=0)
                        {Contact c=contacts[centered];bones.Add(Alias(c.Parent,useRear?c.Rear:front?c.Front:c.Center,"D"+definitionIndex+"_CenterContact",geometry,aliases));}
                        else
                        {
                            // A native single tail emitter stays symmetric:
                            // merge actual left/right contacts, never one side.
                            Vector3 left=ContactPoint(contacts[pair[0]],geometry,front,useRear);
                            Vector3 right=ContactPoint(contacts[pair[1]],geometry,front,useRear);
                            bones.Add(Alias(root,(left+right)*.5f,"D"+definitionIndex+"_PairMean",geometry,aliases));
                        }
                    }
                    else foreach(int i in pair)
                    {Contact c=contacts[i];bones.Add(Alias(c.Parent,useRear?c.Rear:front?c.Front:c.Center,"D"+definitionIndex+"_C"+i,geometry,aliases));}
                }
                if(bones.Count!=budget)throw new InvalidOperationException("Native dust emitter budget differs: "+item.Name);
                EffectDefinition copy=Clone(item);copy.BoneRef=bones.ToArray();result.Add(copy);definitionIndex++;
            }
            if(result.Count==0)throw new InvalidOperationException("Native WheelContactRange dust whitelist empty");
            return result;
        }

        static int NativeEmitterBudget(UnitViewAttributes source,EffectDefinition item,bool carrier)
        {
            int count=item.BoneRef==null?0:item.BoneRef.Length;
            if(count==0&&carrier&&source.WheelFXDefinitions!=null)
            {
                foreach(UnitViewAttributes.WheelAttributes wheel in source.WheelFXDefinitions)
                    if(wheel!=null&&wheel.WheelEffects!=null&&Array.IndexOf(wheel.WheelEffects,item.Name)>=0)count++;
            }
            if(count<1||count>2)throw new InvalidOperationException("Unsupported native dust emitter budget: "+item.Name+" count="+count);
            return count;
        }

        internal static Vector3 ContactPoint(Contact contact,Transform geometry,bool front,bool rear)
        {return geometry.InverseTransformPoint(contact.Parent.TransformPoint(rear?contact.Rear:front?contact.Front:contact.Center));}

        static int CenteredContact(List<Contact> contacts,Transform geometry,bool front,bool rear)
        {
            int selected=-1;float best=rear?float.PositiveInfinity:float.NegativeInfinity;
            for(int i=0;i<contacts.Count;i++)
            {Vector3 p=ContactPoint(contacts[i],geometry,front,rear);if(Mathf.Abs(p.x)>.001f)continue;if(selected<0||(rear?p.z<best:p.z>best)){selected=i;best=p.z;}}
            return selected;
        }

        internal static int[] PickPair(List<Contact> contacts,Transform geometry,bool front,bool rear,bool middle)
        {
            Vector3[] points=new Vector3[contacts.Count];float meanX=0f,minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity;
            for(int i=0;i<contacts.Count;i++)
            {Vector3 p=ContactPoint(contacts[i],geometry,front,rear);points[i]=p;meanX+=p.x;minZ=Mathf.Min(minZ,p.z);maxZ=Mathf.Max(maxZ,p.z);}
            meanX/=contacts.Count;float targetZ=middle?(minZ+maxZ)*.5f:rear?minZ:maxZ;
            int[] selected={-1,-1};float[] score={float.PositiveInfinity,float.PositiveInfinity};
            for(int i=0;i<points.Length;i++)
            {
                Vector3 p=points[i];int side=p.x<meanX?0:1;float distance=Mathf.Abs(p.z-targetZ);
                if(selected[side]<0||distance<score[side]-.001f||Mathf.Abs(distance-score[side])<=.001f&&Mathf.Abs(p.x-meanX)>Mathf.Abs(points[selected[side]].x-meanX))
                {selected[side]=i;score[side]=distance;}
            }
            if(selected[0]<0||selected[1]<0)throw new InvalidOperationException("Authored dust contact pair lacks a side");
            return selected;
        }

        static bool BoneContains(EffectDefinition item,string value)
        {if(item.BoneRef!=null)foreach(string bone in item.BoneRef)if(bone!=null&&bone.IndexOf(value,StringComparison.OrdinalIgnoreCase)>=0)return true;return false;}

        static string Alias(Transform parent,Vector3 point,string suffix,Transform geometry,List<GameObject> aliases)
        {
            // Each anchor follows its actual bogie or steering parent, never a
            // spin/shoe bone. Only failed construction destroys these aliases.
            GameObject go=new GameObject("EF_Ground54_"+suffix);aliases.Add(go);go.layer=geometry.gameObject.layer;go.transform.parent=parent;
            go.transform.localPosition=point;go.transform.rotation=geometry.rotation;go.transform.localScale=Vector3.one;
            return go.name;
        }

        internal static int VerifyCarrierPrefix(UnitViewAttributes target,UnitViewAttributes source,int offset)
        {
            RhodesCarrierBinding binding=target.ModelPrefab.GetComponent<RhodesCarrierBinding>();
            if(binding==null||binding.GeometryRoot==null||binding.GeometryRoot.Find(RootName)==null)return 0;
            int count=0;
            foreach(EffectDefinition original in source.UnitEffectDefs)
            {
                if(original==null||original.FXType!=FrillType.WheelContactRange||original.FXPrefab==null||original.Name==null||original.Name.IndexOf("Dust",StringComparison.OrdinalIgnoreCase)<0)continue;
                if(target.UnitEffectDefs.Length<=offset+count)throw new InvalidOperationException("Carrier dust prefix truncated");
                EffectDefinition copy=target.UnitEffectDefs[offset+count];
                if(copy==null||object.ReferenceEquals(copy,original)||object.ReferenceEquals(copy.BoneRef,original.BoneRef)||copy.Name!=original.Name||copy.FXPrefab!=original.FXPrefab||
                    copy.FXType!=original.FXType||copy.ActiveAtLOD!=original.ActiveAtLOD||copy.Sound!=original.Sound||copy.PolyGroup!=original.PolyGroup||copy.RaycastDir!=original.RaycastDir||
                    copy.FollowNLIPSScale!=original.FollowNLIPSScale||copy.InheritRaycastNormal!=original.InheritRaycastNormal||copy.BoneRef==null||copy.BoneRef.Length!=NativeEmitterBudget(source,original,true))
                    throw new InvalidOperationException("Carrier dust source/isolation contract changed");
                foreach(string bone in copy.BoneRef)if(!bone.StartsWith("EF_Ground54_",StringComparison.Ordinal)||CountNamed(binding.GeometryRoot,bone)!=1)
                    throw new InvalidOperationException("Carrier dust authored contact alias missing/ambiguous");
                count++;
            }
            if(count!=4)throw new InvalidOperationException("Carrier native dust definition inventory changed");
            return count;
        }

        internal static void AppendMovementAudio(UnitViewAttributes target,UnitViewAttributes source,string key)
        {
            GameObject rootAlias=null;
            try
            {
                if(target==null||source==null||target.ModelPrefab==null||object.ReferenceEquals(target,source))throw new InvalidOperationException("Movement audio requires a private model");
                List<EffectDefinition> result=new List<EffectDefinition>();int engines=0;
                foreach(EffectDefinition item in source.UnitEffectDefs)
                {
                    if(!MovementAudio(item))continue;
                    if(item.BoneRef==null||item.BoneRef.Length==0)throw new InvalidOperationException("Native movement audio emission contract changed");
                    EffectDefinition copy=Clone(item);
                    for(int i=0;i<copy.BoneRef.Length;i++)
                    {
                        string bone=copy.BoneRef[i];int count=CountNamed(target.ModelPrefab.transform,bone);
                        if(count==0&&bone==source.ModelPrefab.name)
                        {
                            if(rootAlias==null)
                            {
                                rootAlias=new GameObject("EF_MovementAudio54_Root");rootAlias.layer=target.ModelPrefab.layer;rootAlias.transform.parent=target.ModelPrefab.transform;
                                rootAlias.transform.localPosition=Vector3.zero;rootAlias.transform.localRotation=Quaternion.identity;rootAlias.transform.localScale=Vector3.one;
                            }
                            copy.BoneRef[i]=rootAlias.name;
                        }
                        else if(count!=1)throw new InvalidOperationException("Movement audio bone missing/ambiguous: "+bone+" count="+count);
                    }
                    if(item.Name=="SFX - Engine")engines++;
                    result.Add(copy);
                }
                if(engines!=1)throw new InvalidOperationException("Native movement engine whitelist changed");
                Append(target,result);
                Runtime.Log("RHODES_NATIVE_MOVEMENT_AUDIO54_READY key="+key+"; soundOnlyDefinitions="+result.Count+"; nativeEngineStartStopBrakeTriggersAndFabric=True; nativeRpmVisibilityPausePolyphonyLOD=True; driftExcluded=True; newAudioSources=0; customUpdates=0; listeningAcceptancePending=True");
            }
            catch(Exception error)
            {
                if(rootAlias!=null)UnityEngine.Object.DestroyImmediate(rootAlias);
                Runtime.Log("RHODES_NATIVE_MOVEMENT_AUDIO54_DISABLED key="+key+"; scope=UnitMovementAudioOnly; reason="+error.Message);
            }
        }

        static bool MovementAudio(EffectDefinition item)
        {
            if(item==null||item.FXPrefab!=null||string.IsNullOrEmpty(item.Sound)||item.Sound=="_UnSet_")return false;
            return item.Name=="SFX - Engine"&&item.FXType==FrillType.Engine||item.Name=="SFX - Engine Start Up"&&item.FXType==FrillType.EngineStartUpRange||
                (item.Name=="SFX - Engine Shut Down"||item.Name=="SFX - Stop")&&item.FXType==FrillType.EngineShutDownRange||
                (item.Name=="SFX - Braking"||item.Name=="SFX - braking")&&item.FXType==FrillType.Brake;
        }
        static EffectDefinition Clone(EffectDefinition item)
        {EffectDefinition copy=(EffectDefinition)CloneMethod.Invoke(item,null);copy.BoneRef=item.BoneRef==null?new string[0]:(string[])item.BoneRef.Clone();return copy;}
        static void Append(UnitViewAttributes target,List<EffectDefinition> added)
        {List<EffectDefinition> all=new List<EffectDefinition>();if(target.UnitEffectDefs!=null)all.AddRange(target.UnitEffectDefs);all.AddRange(added);target.UnitEffectDefs=all.ToArray();}
        static GameObject NewRoot(Transform geometry)
        {GameObject root=new GameObject(RootName);root.layer=geometry.gameObject.layer;root.transform.parent=geometry;root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;return root;}
        static int CountNamed(Transform root,string name)
        {int count=0;foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)count++;return count;}
        static void RequirePrivate(UnitViewAttributes target,UnitViewAttributes source,Transform geometry)
        {if(target==null||source==null||object.ReferenceEquals(target,source)||target.ModelPrefab==null||geometry==null||!geometry.IsChildOf(target.ModelPrefab.transform))throw new InvalidOperationException("Ground FX require private authored model");}
        static void RequireNoSuspension(UnitViewAttributes target)
        {if(target.LODTunings==null||Convert.ToInt32(NativeRead.Get(target.LODTunings,"SuspensionActiveLODs"))!=0||Convert.ToInt32(NativeRead.Get(target.LODTunings,"BodyMotionActiveLODs"))!=0)throw new InvalidOperationException("Ground FX must not re-enable native suspension/body motion");}
        static string ContactSources(List<Contact> contacts)
        {List<string> names=new List<string>();foreach(Contact c in contacts)names.Add(c.Provenance);return string.Join("|",names.ToArray());}
        static void DestroyAliases(List<GameObject> aliases)
        {foreach(GameObject alias in aliases)if(alias!=null)UnityEngine.Object.DestroyImmediate(alias);}
    }
}
