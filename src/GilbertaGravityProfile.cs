using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Source-UV material sheets are shared. Per-cast mesh UV buffers select a
    // frame without depending on particle shaders' private tint conventions.
    internal sealed class GilbertaGravityProfile
    {
        internal string TextureFile;
        internal int Frames,Columns,Rows,Resolution,Padding,Cell;
        internal Vector4 Domain;

        internal Vector2 AtlasUV(Vector2 source,int frame)
        {
            float u=(source.x-Domain.x)/Domain.z;
            float v=(source.y-Domain.y)/Domain.w;
            int column=frame%Columns,row=Rows-1-frame/Columns;
            return new Vector2((column*Cell+Padding+.5f+u*(Resolution-1))/(Columns*Cell),
                (row*Cell+Padding+.5f+v*(Resolution-1))/(Rows*Cell));
        }
        static string ReadString(BinaryReader reader)
        {
            int count=reader.ReadInt32();
            if(count<1||count>256)throw new InvalidDataException("Gravity sheet string bounds");
            byte[] bytes=reader.ReadBytes(count);
            if(bytes.Length!=count)throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }
        internal static Dictionary<string,GilbertaGravityProfile> Load(string root)
        {
            Dictionary<string,GilbertaGravityProfile> profiles=new Dictionary<string,GilbertaGravityProfile>();
            using(BinaryReader reader=new BinaryReader(File.OpenRead(Path.Combine(root,"gravity_sheets.bin"))))
            {
                if(reader.ReadUInt32()!=0x31535247||reader.ReadInt32()!=1)throw new InvalidDataException("Gravity sheet signature");
                int count=reader.ReadInt32();
                if(count<1||count>8)throw new InvalidDataException("Gravity sheet count");
                for(int index=0;index<count;index++)
                {
                    string name=ReadString(reader);
                    GilbertaGravityProfile profile=new GilbertaGravityProfile();
                    profile.TextureFile=ReadString(reader);
                    profile.Frames=reader.ReadInt32();profile.Columns=reader.ReadInt32();profile.Rows=reader.ReadInt32();
                    profile.Resolution=reader.ReadInt32();profile.Padding=reader.ReadInt32();profile.Cell=reader.ReadInt32();
                    profile.Domain=new Vector4(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                    if(Path.GetFileName(profile.TextureFile)!=profile.TextureFile||profile.Frames<2||profile.Frames>64||
                        profile.Columns<1||profile.Columns>8||profile.Rows<1||profile.Rows>8||profile.Frames>profile.Columns*profile.Rows||
                        profile.Resolution<64||profile.Resolution>256||profile.Padding<4||profile.Padding>16||
                        profile.Cell!=profile.Resolution+profile.Padding*2||profile.Domain.z<=0||profile.Domain.w<=0)
                        throw new InvalidDataException("Gravity sheet profile bounds");
                    profiles.Add(name,profile);
                }
                if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Gravity sheet trailing bytes");
            }
            return profiles;
        }
    }
}
