using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using log4net;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

namespace Yafex;

public class AesDecryptor
{
    private static readonly ILog log = LogManager.GetLogger(typeof(AesDecryptor));

    public Aes Aes { get; private set; }

    public AesDecryptor(Aes aes)
    {
        this.Aes = aes;
    }

    public Memory64<byte> Decrypt(ReadOnlySpan64<byte> data, long bufferSize)
    {
        var blockSizeMask = (Aes.BlockSize >> 3) - 1;
        var unalignedCount = data.Length & blockSizeMask;
        if(unalignedCount > 0)
        {
            log.WarnFormat("Warning: skipping {0} unaligned bytes", unalignedCount);
            data = data.Slice(0, data.Length - unalignedCount);
        }

        if (bufferSize < data.Length) {
            throw new ArgumentException("provided bufferSize is smaller than the size of the data");
        }

        ICryptoTransform decryptor = Aes.CreateDecryptor();

        var outStream = new MemoryStream();
        outStream.SetLength(bufferSize);

        using (var cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write))
        {
            foreach (var chunk in data.GetChunks())
            {
                cs.Write(chunk);
            }
        }
        return outStream.ToArray();
    }

    public Memory64<byte> Decrypt(ReadOnlySpan64<byte> data) => Decrypt(data, data.Length);
}

