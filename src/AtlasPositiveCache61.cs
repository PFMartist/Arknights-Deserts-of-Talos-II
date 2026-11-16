using System.Collections.Generic;

namespace EndfieldDoK.MapUnitLab
{
    // Records only successfully built mappings, never "nothing to add" results.
    // Native lists may grow or be replaced during initialization. The generated
    // private atlas lists are owned by these builders, not edited by native UI.
    internal sealed class AtlasPositiveCache61
    {
        sealed class Entry
        {
            internal UIAtlas Result;
            internal List<UISpriteData> SourceSprites,ResultSprites;
            internal int SourceCount,ResultCount,Generation;
        }
        readonly Dictionary<UIAtlas,Entry> Sources=new Dictionary<UIAtlas,Entry>();
        readonly Dictionary<UIAtlas,Entry> Results=new Dictionary<UIAtlas,Entry>();
        internal int Count {get{return Sources.Count;}}
        internal bool ContainsKey(UIAtlas source){return Sources.ContainsKey(source);}
        internal bool TryGetValue(UIAtlas source,int generation,out UIAtlas result)
        {
            result=null;Entry entry;
            if(Sources.TryGetValue(source,out entry))
            {
                if(entry.Generation!=generation||entry.Result==null)return false;
                List<UISpriteData> sprites=source.spriteList;
                if(!object.ReferenceEquals(sprites,entry.SourceSprites)||sprites.Count!=entry.SourceCount)return false;
                sprites=entry.Result.spriteList;
                if(!object.ReferenceEquals(sprites,entry.ResultSprites)||sprites.Count!=entry.ResultCount)return false;
                result=entry.Result;return true;
            }
            if(!Results.TryGetValue(source,out entry)||entry.Generation!=generation||entry.Result==null)return false;
            List<UISpriteData> own=source.spriteList;
            if(!object.ReferenceEquals(own,entry.ResultSprites)||own.Count!=entry.ResultCount)return false;
            result=source;return true;
        }
        internal void Store(UIAtlas source,UIAtlas result,int generation)
        {
            List<UISpriteData> sourceSprites=source.spriteList,resultSprites=result.spriteList;
            Entry previous;if(Sources.TryGetValue(source,out previous))Results.Remove(previous.Result);
            Entry entry=new Entry{Result=result,SourceSprites=sourceSprites,ResultSprites=resultSprites,
                SourceCount=sourceSprites.Count,ResultCount=resultSprites.Count,Generation=generation};
            Sources[source]=entry;Results[result]=entry;
        }
        internal void Clear(){Sources.Clear();Results.Clear();}
    }
}
