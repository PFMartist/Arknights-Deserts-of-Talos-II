using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BBI.Core.IO.Streams;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Core.Pathfinding;
using BBI.Game.Data;
using BBI.Game.Simulation;

public static class CarrierRouteProbe62
{
    static string Native;
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static Fixed64 F(float v){return Fixed64.FromConstFloat(v);}
    static Vector2r V(float x,float y){return new Vector2r(F(x),F(y));}
    static double D(Vector2r a,Vector2r b){double x=Fixed64.UnsafeFloatValue(a.X-b.X),y=Fixed64.UnsafeFloatValue(a.Y-b.Y);return Math.Sqrt(x*x+y*y);}
    static string H(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
    static object Get(object o,string n){var p=o.GetType().GetProperty(n,Flags);return p!=null?p.GetValue(o,null):o.GetType().GetField(n,Flags).GetValue(o);}
    static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,Flags).Invoke(o,a);}
    static string N(float value){return value.ToString("0.###",CultureInfo.InvariantCulture);}
    static string P(Vector2r p){return "["+N(Fixed64.UnsafeFloatValue(p.X))+","+N(Fixed64.UnsafeFloatValue(p.Y))+"]";}
    static SerializableNavMeshAttributes Profile(float radius)
    {
        var p=new SerializableNavMeshAttributes();
        using(var s=new MemoryStream()){
            var w=new BinaryStreamWriter(s);w.WriteFixed64(F(radius));w.WriteFixed64(F(.2f));w.WriteInt32(8);
            s.Position=0;p.Read(new BinaryStreamReader(s));
        }
        return p;
    }
    static string SerializedProfile(float radius)
    {using(var stream=new MemoryStream()){Profile(radius).Write(new BinaryStreamWriter(stream));return Convert.ToBase64String(stream.ToArray());}}
    sealed class Query
    {
        public bool Nonempty,ExactGoal,ValidStart,ValidGoal;
        public int Vertices;
        public Vector2r AdjustedStart,AdjustedGoal;
    }
    static Query Ask(PathNodeNavigationCache cache,Vector2r from,Vector2r to)
    {
        Query q=new Query();
        q.ValidStart=cache.NearbyValidPosition(from,out q.AdjustedStart);
        q.ValidGoal=cache.NearbyValidPosition(to,out q.AdjustedGoal);
        var path=new List<Vector2r>();cache.FindPath(from,to,path);q.Vertices=path.Count;q.Nonempty=path.Count>0;
        foreach(var p in path)if(D(p,to)<.25)q.ExactGoal=true;
        return q;
    }
    public static int Main(string[] args)
    {
        if(args.Length!=2){Console.Error.WriteLine("Expected external maps TSV and native Managed directory");return 2;}
        Native=Path.GetFullPath(args[1]);
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string file=Path.Combine(Native,new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
        return Run(args);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]static int Run(string[] args)
    {
        using(var timer=new Timer(delegate{Console.Error.WriteLine("ROUTE_PROBE_TIMEOUT");Environment.Exit(124);},null,55000,Timeout.Infinite))
        try{
            var report=new StringBuilder("{\"scope\":\"native serialized caches and native managed pathfinder; normalized synthetic queries, not player commands\",\"UnityExecuted\":false,\"gameStarted\":false,\"grid\":\"5x5 at 10/30/50/70/90 percent of native map bounds, projected by the native 5-clearance cache\",\"goalTolerance\":0.25,\"profiles\":[[5,0.2,8],[305,0.2,8]],\"profile5SerializedBytes\":\""+SerializedProfile(5)+"\",\"profile305SerializedBytes\":\""+SerializedProfile(305)+"\",\"maps\":[");
            bool first=true;int maps=0,totalPairs=0,totalLost=0,totalExact5=0,totalExact305=0;
            foreach(string row in File.ReadAllLines(args[0])){
                string[] cells=row.Split('\t');if(cells.Length!=3)throw new Exception("Expected label, file, SHA256");
                string input=Path.IsPathRooted(cells[1])?cells[1]:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),cells[1]);
                byte[] data=File.ReadAllBytes(input);if(H(data)!=cells[2])throw new Exception("Native cache provenance hash differs: "+cells[0]);
                Type mapType=typeof(SimMapDependencies).Assembly.GetType("BBI.Game.Simulation.SimMap",true);
                object map=Activator.CreateInstance(mapType,Flags,null,new object[]{null,0},CultureInfo.InvariantCulture);
                var reader=new BinaryStreamReader(new MemoryStream(data,false));var load=(IEnumerator)Call(map,"Read",reader);while(load.MoveNext()){}
                if(!reader.EndOfStream)throw new Exception("Map cache trailing data");
                var cache5=(PathNodeNavigationCache)Call(map,"GetNavMeshFor",Profile(5));if(cache5==null)throw new Exception("Native 5/.2/8 carrier profile missing: "+cells[0]);
                var obstacles=(QuadTree)Get(map,"mPathfindingObstaclesHash");Vector2r min=(Vector2r)Get(map,"Min"),max=(Vector2r)Get(map,"Max");
                var cache305=new PathNodeNavigationCache();cache305.BakeNavigationCache(obstacles.AllObstacles,min,max,F(305),F(.2f),8);
                float x0=Fixed64.UnsafeFloatValue(min.X),x1=Fixed64.UnsafeFloatValue(max.X),y0=Fixed64.UnsafeFloatValue(min.Y),y1=Fixed64.UnsafeFloatValue(max.Y);
                var points=new List<Vector2r>();int rawValid5=0,rawValid305=0;
                for(int ix=0;ix<5;ix++)for(int iy=0;iy<5;iy++){
                    Vector2r raw=V(x0+(x1-x0)*(.1f+.2f*ix),y0+(y1-y0)*(.1f+.2f*iy)),a,b;
                    bool native=cache5.NearbyValidPosition(raw,out a),large=cache305.NearbyValidPosition(raw,out b);
                    if(native&&D(raw,a)<.25)rawValid5++;if(large&&D(raw,b)<.25)rawValid305++;
                    if(!native)continue;bool duplicate=false;foreach(var prior in points)if(D(prior,a)<1)duplicate=true;if(!duplicate)points.Add(a);
                }
                int pairs=0,nonempty5=0,nonempty305=0,exact5=0,exact305=0,lost=0,displaced305=0,exampleCount=0,commonValidPairs=0,lostCommonValidPairs=0;
                var examples=new StringBuilder("[");
                for(int i=0;i<points.Count;i++)for(int j=i+1;j<points.Count;j++){
                    var a=points[i];var b=points[j];Query normal=Ask(cache5,a,b),large=Ask(cache305,a,b);pairs++;
                    if(normal.Nonempty)nonempty5++;if(large.Nonempty)nonempty305++;if(normal.ExactGoal)exact5++;if(large.ExactGoal)exact305++;
                    if(large.ValidGoal&&D(b,large.AdjustedGoal)>.25)displaced305++;
                    bool commonValid=large.ValidStart&&large.ValidGoal&&D(a,large.AdjustedStart)<.25&&D(b,large.AdjustedGoal)<.25;
                    if(normal.ExactGoal&&commonValid){commonValidPairs++;if(!large.ExactGoal)lostCommonValidPairs++;}
                    if(normal.ExactGoal&&!large.ExactGoal){
                        lost++;if(exampleCount<4){if(exampleCount>0)examples.Append(',');examples.Append("{\"from\":").Append(P(a)).Append(",\"to\":").Append(P(b)).Append(",\"waypoints5\":").Append(normal.Vertices).Append(",\"waypoints305\":").Append(large.Vertices).Append(",\"goalValid305\":").Append(large.ValidGoal?"true":"false").Append(",\"adjustedGoal305\":").Append(P(large.AdjustedGoal)).Append('}');exampleCount++;}
                    }
                }
                examples.Append(']');if(!first)report.Append(',');first=false;maps++;totalPairs+=pairs;totalLost+=lost;totalExact5+=exact5;totalExact305+=exact305;
                report.Append("{\"name\":\"").Append(cells[0]).Append("\",\"sourceSha256\":\"").Append(cells[2]).Append("\",\"bounds\":[").Append(P(min)).Append(',').Append(P(max)).Append("],\"connectedComponents5\":").Append(cache5.ConnectedComponents).Append(",\"connectedComponents305\":").Append(cache305.ConnectedComponents).Append(",\"rawGridPointsUnchanged5\":").Append(rawValid5).Append(",\"rawGridPointsUnchanged305\":").Append(rawValid305).Append(",\"samplePoints\":").Append(points.Count).Append(",\"pairs\":").Append(pairs).Append(",\"nonemptyFindPath5\":").Append(nonempty5).Append(",\"nonemptyFindPath305\":").Append(nonempty305).Append(",\"requestedGoalReached5\":").Append(exact5).Append(",\"requestedGoalReached305\":").Append(exact305).Append(",\"goalDisplaced305\":").Append(displaced305).Append(",\"goalReachLostAt305\":").Append(lost).Append(",\"native5ReachablePairsWithBothEndpointsUnchanged305\":").Append(commonValidPairs).Append(",\"lostWithBothEndpointsUnchanged305\":").Append(lostCommonValidPairs).Append(",\"examples\":").Append(examples).Append('}');
            }
            report.Append("],\"mapCount\":").Append(maps).Append(",\"totalPairs\":").Append(totalPairs).Append(",\"totalRequestedGoalReached5\":").Append(totalExact5).Append(",\"totalRequestedGoalReached305\":").Append(totalExact305).Append(",\"totalGoalReachLostAt305\":").Append(totalLost).Append(",\"runtimeRouteAcceptance\":false}");
            Console.WriteLine(report.ToString());return 0;
        }catch(Exception error){Console.Error.WriteLine(error.ToString());return 1;}
    }
}
