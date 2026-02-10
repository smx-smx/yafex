using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

namespace Yafex;

public class AesDecryptor
{
    public Aes Aes { get; private set; }

    public AesDecryptor(Aes aes)
    {
        this.Aes = aes;
    }

    public Memory64<byte> Decrypt(ReadOnlySpan64<byte> data, long bufferSize)
    {
        if (bufferSize < data.Length) {
            throw new ArgumentException("provided bufferSize is smaller than the size of the data");
        }

        ICryptoTransform decryptor = Aes.CreateDecryptor();

        var outStream = new MemoryStream();
        var cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
        foreach (var chunk in data.GetChunks())
        {
            cs.Write(chunk);
        }
        cs.Flush();
        return outStream.ToArray();
    }

    public Memory64<byte> Decrypt(ReadOnlySpan64<byte> data) => Decrypt(data, data.Length);
}

