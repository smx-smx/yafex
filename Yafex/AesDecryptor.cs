using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using log4net;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

namespace Yafex;

public interface IAesDecryptorFactory<T> where T : AesDecryptor
{
    T Create(Aes aes);
}

public class AesDecryptor
{
    private static readonly ILog log = LogManager.GetLogger(typeof(AesDecryptor));

    public Aes Aes { get; private set; }

    public AesDecryptor(Aes aes)
    {
        this.Aes = aes;
    }

    protected virtual ReadOnlySpan<byte> HandleUnalignedData(ReadOnlySpan<byte> data)
    {
        log.WarnFormat("Warning: skipping {0} unaligned bytes", data.Length);
        return new Span<byte>();
    }

    public Memory64<byte> Decrypt(ReadOnlySpan64<byte> data, long bufferSize)
    {
        var blockSizeMask = (Aes.BlockSize >> 3) - 1;
        var unalignedCount = data.Length & blockSizeMask;
        var unalignedData = data.Slice(data.Length - unalignedCount);
        if(unalignedCount > 0)
        {
            data = data.Slice(0, data.Length - unalignedCount);
        }

        if (bufferSize < data.Length) {
            throw new ArgumentException("provided bufferSize is smaller than the size of the data");
        }

        ICryptoTransform decryptor = Aes.CreateDecryptor();

        using var outStream = new MemoryStream();
        outStream.SetLength(bufferSize);

        using (var cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write, leaveOpen: true))
        {
            foreach (var chunk in data.GetChunks())
            {
                cs.Write(chunk);
            }
        }

        if (unalignedData.Length > 0)
        {
            var trailingData = HandleUnalignedData((ReadOnlySpan<byte>)unalignedData);
            outStream.Write(trailingData);
        }

        return outStream.ToArray();
    }

    public Memory64<byte> Decrypt(ReadOnlySpan64<byte> data) => Decrypt(data, data.Length);

    /// <summary>
    /// Decrypts a window of AES blocks into <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="ciphertext"/> is expected to be block-aligned in length. When
    /// <paramref name="discardPrefixBlocks"/> is non-zero (CBC mode with an unaligned start),
    /// the first blocks of <paramref name="ciphertext"/> seed the decryption chaining state
    /// and their plaintext is not copied to <paramref name="output"/>.
    /// A fresh transform is created per call, matching whole-buffer decryption semantics
    /// (each segment is decrypted independently from its own IV).
    /// </remarks>
    public void DecryptBlocks(byte[] ciphertext, int discardPrefixBlocks, byte[] output)
    {
        var blockSize = Aes.BlockSize >> 3;
        var alignedLen = ciphertext.Length & ~(blockSize - 1);
        if (alignedLen == 0)
        {
            return;
        }
        if (output.Length < alignedLen)
        {
            throw new ArgumentException("output buffer is smaller than the block-aligned ciphertext");
        }

        var decryptor = Aes.CreateDecryptor();
        int written = decryptor.TransformBlock(ciphertext, 0, alignedLen, output, 0);

        var skip = discardPrefixBlocks * blockSize;
        if (written > skip)
        {
            Buffer.BlockCopy(output, skip, output, 0, written - skip);
        }
    }
}

