using System;
using System.IO;
using System.IO.Compression;

namespace Yafex.FileSystems.Squashfs2
{
    /// <summary>
    /// Data-block decompression with vendor-variant fallback: stock codecs
    /// first, then raw LZMA with shifted offsets and brute-forced
    /// properties. The first adaptive success is cached for the rest of
    /// the run. LZMA paths need netstandard2.0 (SharpCompress).
    /// </summary>
    internal static class Squashfs2Decompress
    {
        private static readonly object _gate = new object();
        private static bool _adaptiveDetected;
        private static int _adaptiveLc;
        private static int _adaptiveLp;
        private static int _adaptivePb;
        private static int _adaptiveDict;
        private static int _adaptiveOffset;

        public static byte[] Uncompress(byte[] stored, long expected)
        {
            byte[] result;
            Exception last = null;
#if NETSTANDARD2_0
            // Fast path: the per-image detection sticks. Big-endian images
            // land here after the first block (implicit-defaults raw LZMA
            // after a short header).
            lock (_gate)
            {
                if (_adaptiveDetected)
                {
                    if (TryWithProps(stored, _adaptiveLc, _adaptiveLp,
                        _adaptivePb, _adaptiveDict, _adaptiveOffset, expected, out result))
                    {
                        return result;
                    }
                }
            }
#endif
            if (TryZlib(stored, expected, out result))
            {
                return result;
            }
            for (int off = 0; off <= 4; off += 4)
            {
                if (TryLzmaAlone(stored, off, -1, expected, out result))
                {
                    return result;
                }
            }
#if NETSTANDARD2_0
            if (TryAdaptive(stored, expected, out result))
            {
                return result;
            }
#else
            last = new NotSupportedException("LZMA data blocks need netstandard2.0");
#endif
            throw new IOException(
                "squashfs data block matched no decompressor"
                + (last != null ? ": " + last.Message : string.Empty));
        }

        public static bool TryZlib(byte[] stored, long expected, out byte[] result)
        {
            result = Array.Empty<byte>();
            try
            {
                int start = stored.Length >= 2 && stored[0] == 0x78 ? 2 : 0;
                using (var raw = new MemoryStream(stored, start, stored.Length - start, false))
                using (var inflater = new DeflateStream(raw, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buf = new byte[1 << 16];
                    long total = 0;
                    for (;;)
                    {
                        int n = inflater.Read(buf, 0, buf.Length);
                        if (n <= 0)
                        {
                            break;
                        }
                        total += n;
                        if (total > expected + (1 << 20))
                        {
                            return false;
                        }
                        output.Write(buf, 0, n);
                    }
                    if (output.Length == 0)
                    {
                        return false;
                    }
                    result = output.ToArray();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

#if NETSTANDARD2_0
        public static bool TryLzmaAlone(byte[] stored, int offset, int dictSize, long expected, out byte[] result)
        {
            result = Array.Empty<byte>();
            try
            {
                if (offset < 0 || offset + 5 >= stored.Length)
                {
                    return false;
                }
                var props = new byte[5];
                Array.Copy(stored, offset, props, 0, 5);
                if (props[0] >= 225)
                {
                    return false;
                }
                using (var raw = new MemoryStream(stored, offset + 5, stored.Length - offset - 5, false))
                using (var lzma = new SharpCompress.Compressors.LZMA.LzmaStream(props, raw))
                using (var output = new MemoryStream())
                {
                    var buf = new byte[1 << 16];
                    long total = 0;
                    for (;;)
                    {
                        int n = lzma.Read(buf, 0, (int)Math.Min(buf.Length, expected - total));
                        if (n <= 0)
                        {
                            break;
                        }
                        total += n;
                        if (total > expected)
                        {
                            return false;
                        }
                        output.Write(buf, 0, n);
                    }
                    // exact size + full input consumption: rejects
                    // wrong-property decodes (these streams carry no EOS marker).
                    if (output.Length != expected || raw.Position != raw.Length)
                    {
                        return false;
                    }
                    result = output.ToArray();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static readonly int[] DictSizes = { 1 << 16, 1 << 20, 1 << 23 };

        public static bool TryAdaptive(byte[] stored, long expected, out byte[] result)
        {
            result = Array.Empty<byte>();
            lock (_gate)
            {
                if (_adaptiveDetected)
                {
                    return TryWithProps(stored, _adaptiveLc, _adaptiveLp,
                        _adaptivePb, _adaptiveDict, _adaptiveOffset, expected, out result);
                }
            }
            // BE images: implicit-defaults raw LZMA after a 5-byte header.
            // Otherwise brute-force (lc, lp, pb, offset) like lzma_adaptive.
            foreach (int offset in AdaptiveOffsets)
            {
                foreach (int dict in DictSizes)
                {
                    for (int lc = 0; lc <= 8; lc++)
                    {
                        for (int lp = 0; lp <= 4; lp++)
                        {
                            for (int pb = 0; pb <= 4; pb++)
                            {
                                // defaults first: the common vendor case
                                if (lc != 3 || lp != 0 || pb != 2)
                                {
                                    continue;
                                }
                                if (TryWithProps(stored, lc, lp, pb, dict, offset, expected, out result))
                                {
                                    Detect(lc, lp, pb, dict, offset);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            for (int offset = 0; offset <= 10; offset++)
            {
                if (offset == 5)
                {
                    continue;
                }
                foreach (int dict in DictSizes)
                {
                    for (int lc = 0; lc <= 8; lc++)
                    {
                        for (int lp = 0; lp <= 4; lp++)
                        {
                            for (int pb = 0; pb <= 4; pb++)
                            {
                                if (TryWithProps(stored, lc, lp, pb, dict, offset, expected, out result))
                                {
                                    Detect(lc, lp, pb, dict, offset);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static readonly int[] AdaptiveOffsets = { 5, 0, 4, 1, 2, 3, 6, 7, 8, 9, 10 };

        private static void Detect(int lc, int lp, int pb, int dict, int offset)
        {
            lock (_gate)
            {
                _adaptiveDetected = true;
                _adaptiveLc = lc;
                _adaptiveLp = lp;
                _adaptivePb = pb;
                _adaptiveDict = dict;
                _adaptiveOffset = offset;
            }
        }

        private static bool TryWithProps(byte[] stored, int lc, int lp, int pb, int dict, int offset,
            long expected, out byte[] result)
        {
            result = Array.Empty<byte>();
            try
            {
                if (offset < 0 || offset >= stored.Length)
                {
                    return false;
                }
                int propsByte = lc + lp * 9 + pb * 45;
                if (propsByte >= 225)
                {
                    return false;
                }
                var props = new byte[5];
                props[0] = (byte)propsByte;
                props[1] = (byte)dict;
                props[2] = (byte)(dict >> 8);
                props[3] = (byte)(dict >> 16);
                props[4] = (byte)(dict >> 24);
                using (var raw = new MemoryStream(stored, offset, stored.Length - offset, false))
                using (var lzma = new SharpCompress.Compressors.LZMA.LzmaStream(props, raw))
                using (var output = new MemoryStream())
                {
                    var buf = new byte[1 << 16];
                    long total = 0;
                    for (;;)
                    {
                        if (total >= expected)
                        {
                            break;
                        }
                        int n = lzma.Read(buf, 0, (int)Math.Min(buf.Length, expected - total));
                        if (n <= 0)
                        {
                            break;
                        }
                        total += n;
                        output.Write(buf, 0, n);
                    }
                    // exact size match + full input consumption: rejects
                    // wrong-property decodes (BE streams have no EOS).
                    if (output.Length != expected || raw.Position != raw.Length)
                    {
                        return false;
                    }
                    result = output.ToArray();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
#else
        public static bool TryLzmaAlone(byte[] stored, int offset, int dictSize, long expected, out byte[] result)
        {
            result = Array.Empty<byte>();
            return false;
        }

        public static bool TryAdaptive(byte[] stored, long expected, out byte[] result)
        {
            result = Array.Empty<byte>();
            return false;
        }
#endif
    }
}
