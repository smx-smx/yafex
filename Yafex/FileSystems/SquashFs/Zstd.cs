//
// Zstd block decompressor for SquashFS, backed by the managed ZstdSharp.Port
// package (MIT) on targets where it is available. Legacy target frameworks
// build without zstd support and report such images as unsupported.
//

using System.IO;

namespace Yafex.FileSystems.SquashFs
{
    internal static class ZstdDecompressor
    {
#if NETSTANDARD2_0
        /// <summary>
        /// Decompresses one zstd frame. Throws <see cref="IOException"/> on
        /// corrupt input or destination overrun.
        /// </summary>
        /// <returns>Number of decompressed bytes written.</returns>
        public static int Decompress(byte[] input, int inOffset, int inLength, byte[] output, int outOffset, int maxOutputLength)
        {
            if (inLength < 1)
            {
                throw new IOException("Zstd block is empty");
            }
            if (maxOutputLength < 0 || outOffset < 0 || outOffset + maxOutputLength > output.Length)
            {
                throw new IOException("Zstd destination overruns output");
            }
            try
            {
                using (ZstdSharp.Decompressor decompressor = new ZstdSharp.Decompressor())
                {
                    return decompressor.Unwrap(input, inOffset, inLength, output, outOffset, maxOutputLength);
                }
            }
            catch (ZstdSharp.ZstdException ex)
            {
                throw new IOException("Zstd decompression failed: " + ex.Message, ex);
            }
        }
#else
        public static int Decompress(byte[] input, int inOffset, int inLength, byte[] output, int outOffset, int maxOutputLength)
        {
            throw new IOException("Zstd-compressed SquashFS is not supported on this target framework");
        }
#endif
    }
}
