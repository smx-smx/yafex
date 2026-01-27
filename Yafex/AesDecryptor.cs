using System;
using System.IO;
using System.Security.Cryptography;

namespace Yafex;

public class AesDecryptor
{
    private Aes aes;

    public AesDecryptor(Aes aes)
    {
        this.aes = aes;
    }

    public Memory<byte> Decrypt(ReadOnlySpan<byte> data)
    {
        ICryptoTransform decryptor = aes.CreateDecryptor();

        MemoryStream outStream = new MemoryStream(data.Length);

        CryptoStream cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
        cs.Write(data);
        cs.Flush();

        if (!outStream.TryGetBuffer(out ArraySegment<byte> buf))
        {
            return null;
        }
        return buf;
    }
}