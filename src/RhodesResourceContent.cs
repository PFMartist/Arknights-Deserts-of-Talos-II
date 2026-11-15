using System;
using System.IO;
using System.Security.Cryptography;

namespace EndfieldDoK.ModelIntake
{
    // Pure managed read/decode-input stage. No Unity API or game state access.
    // Hash the actual bytes each time; timestamps and path are not identities.
    internal sealed class RhodesResourceContent
    {
        internal readonly byte[] Bytes;
        internal readonly string Sha256;
        internal RhodesResourceContent(byte[] bytes)
        {
            if(bytes==null)throw new ArgumentNullException("bytes");Bytes=bytes;
            using(SHA256 hash=SHA256.Create())Sha256=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        internal static RhodesResourceContent Read(string file,long maximum)
        {
            FileInfo info=new FileInfo(file);
            if(!info.Exists||info.Length<1||info.Length>maximum)throw new InvalidDataException("Resource byte budget exceeded: "+file);
            byte[] bytes=File.ReadAllBytes(file);
            if(bytes.LongLength!=info.Length||bytes.LongLength>maximum)throw new InvalidDataException("Resource changed during read: "+file);
            return new RhodesResourceContent(bytes);
        }
    }
}
