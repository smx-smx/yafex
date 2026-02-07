using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

namespace Yafex;

public class AesDecryptor
{
    private Aes aes;

    public AesDecryptor(Aes aes)
    {
        this.aes = aes;
    }

    public unsafe Memory64<byte> Decrypt(ReadOnlySpan64<byte> data)
    {
        ICryptoTransform decryptor = aes.CreateDecryptor();

        var buffer = new NativeMemoryManager64<byte>(data.Length);
        var outStream = new SpanStream(buffer.Memory);

        var cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
        foreach (var chunk in data.GetChunks())
        {
            cs.Write(chunk);
        }
        cs.Flush();
        return buffer.Memory;
    }
}