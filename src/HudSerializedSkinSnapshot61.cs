using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EndfieldDoK.MapUnitLab
{
    // Cache a skin's managed colour graph, not its current game/UI identity.
    // Mutable colour leaves and reference/collection replacement invalidate it;
    // primitive counters do not. A new component always gets its own snapshot.
    internal sealed class HudSerializedSkinSnapshot61
    {
        sealed class Node
        {
            internal int Kind;
            internal object Identity;
            internal Type Type;
            internal FieldInfo[] Fields;
            internal Node[] Children;
            internal bool Matches(object value)
            {
                // An uninspected budget tail is never evidence of equality.
                // Earlier collection shrinkage can make that tail mappable.
                if(Kind==7)return false;
                if(Kind==0)return value==null;
                if(value==null||value.GetType()!=Type)return false;
                if(Kind==1)return true;
                if(Kind==2)return Identity.Equals(value);
                if(Kind==6)return Type.IsValueType?Identity.Equals(value):object.ReferenceEquals(Identity,value);
                if(!Type.IsValueType&&!object.ReferenceEquals(Identity,value))return false;
                Array array=value as Array;IList list=value as IList;
                if(Kind==3)
                {
                    if(array==null||array.Rank!=1||array.Length!=Children.Length)return false;
                    for(int i=0;i<Children.Length;i++)if(!Children[i].Matches(array.GetValue(i)))return false;
                }
                else if(Kind==4)
                {
                    if(list==null||list.Count!=Children.Length)return false;
                    for(int i=0;i<Children.Length;i++)if(!Children[i].Matches(list[i]))return false;
                }
                else for(int i=0;i<Fields.Length;i++)if(!Children[i].Matches(Fields[i].GetValue(value)))return false;
                return true;
            }
        }
        sealed class IdentityComparer:IEqualityComparer<object>
        {
            public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}
            public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}
        }
        readonly FieldInfo[] Fields;
        readonly Node[] Nodes;
        HudSerializedSkinSnapshot61(FieldInfo[] fields,Node[] nodes){Fields=fields;Nodes=nodes;}
        internal bool Matches(object root)
        {
            if(root==null)return false;
            for(int i=0;i<Fields.Length;i++)if(!Nodes[i].Matches(Fields[i].GetValue(root)))return false;
            return true;
        }
        internal static HudSerializedSkinSnapshot61 Capture(object root,Func<Type,FieldInfo[]> fields,Func<object,bool> opaque,Predicate<FieldInfo> excluded)
        {
            List<FieldInfo> selected=new List<FieldInfo>();
            foreach(FieldInfo field in fields(root.GetType()))if(!excluded(field))selected.Add(field);
            Node[] nodes=new Node[selected.Count];int budget=1024;
            HashSet<object> path=new HashSet<object>(new IdentityComparer());
            for(int i=0;i<nodes.Length;i++)nodes[i]=CaptureValue(selected[i].GetValue(root),0,ref budget,fields,opaque,path);
            return new HudSerializedSkinSnapshot61(selected.ToArray(),nodes);
        }
        static Node CaptureValue(object value,int depth,ref int budget,Func<Type,FieldInfo[]> fields,Func<object,bool> opaque,HashSet<object> path)
        {
            if(value==null)return new Node();
            Type type=value.GetType();Node node=new Node{Kind=1,Type=type,Identity=value};
            if(depth>8||--budget<0){node.Kind=7;return node;}
            if(type.FullName=="UnityEngine.Color"||type.FullName=="UnityEngine.Color32")
            {node.Kind=2;return node;}
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||opaque(value))return node;
            // The mapper never traverses other namespaces or nonserializable
            // objects, except arrays/List<T>, which it handles explicitly.
            Array array=value as Array;IList list=value as IList;
            bool genericList=type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(List<>);
            string space=type.Namespace??string.Empty;
            if(array==null&&!genericList&&(!type.IsSerializable||(!space.StartsWith("BBI.",StringComparison.Ordinal)&&space.Length!=0)))return node;
            if(!type.IsValueType&&!path.Add(value)){node.Kind=6;return node;}
            try
            {
                if(array!=null)
                {
                    if(array.Rank!=1||array.Length>budget){node.Kind=7;return node;}
                    node.Kind=3;node.Children=new Node[array.Length];
                    for(int i=0;i<array.Length;i++)node.Children[i]=CaptureValue(array.GetValue(i),depth+1,ref budget,fields,opaque,path);
                }
                else if(genericList)
                {
                    if(list.Count>budget){node.Kind=7;return node;}
                    node.Kind=4;node.Children=new Node[list.Count];
                    for(int i=0;i<list.Count;i++)node.Children[i]=CaptureValue(list[i],depth+1,ref budget,fields,opaque,path);
                }
                else
                {
                    node.Kind=5;node.Fields=fields(type);node.Children=new Node[node.Fields.Length];
                    for(int i=0;i<node.Fields.Length;i++)node.Children[i]=CaptureValue(node.Fields[i].GetValue(value),depth+1,ref budget,fields,opaque,path);
                }
                return node;
            }
            finally{if(!type.IsValueType)path.Remove(value);}
        }
    }
}
