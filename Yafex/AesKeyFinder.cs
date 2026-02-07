#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using log4net;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;

using Yafex.Support;

namespace Yafex
{
    public struct KeyFinderResult
    {
        public KeyEntry Key;
        public Memory64<byte> Data;
    }

    public delegate bool CryptoResultChecker(ReadOnlySpan64<byte> data);

    public class AesKeyFinder
    {
        private readonly KeyBundle bundle;
        private readonly string _bundleId;

        private static readonly ILog logger = LogManager.GetLogger(typeof(AesKeyFinder));

        public AesKeyFinder(KeyBundle bundle, string bundleId)
        {
            this.bundle = bundle;
            _bundleId = bundleId;
        }

        private Span64<byte> TestAesKey(ReadOnlySpan64<byte> data, KeyEntry keyEntry, CryptoResultChecker validator)
        {
            var aes = Aes.Create();
            aes.BlockSize = 128;
            aes.KeySize = keyEntry.key.Length * 8;
            aes.Key = keyEntry.key;
            if (keyEntry.keyMode == CipherMode.CBC)
            {
                aes.IV = keyEntry.iv;
            }
            aes.Mode = keyEntry.keyMode;
            aes.Padding = PaddingMode.None;

            var decryptor = aes.CreateDecryptor();

            var buffer = new NativeMemoryManager64<byte>(data.Length);
            var outStream = new SpanStream(buffer.Memory);

            var cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
            foreach(var chunk in data.GetChunks())
            {
                cs.Write(chunk);
            }
            cs.Flush();

            var span = buffer.Memory.Span;
            if (validator(span))
            {
                return span;
            }

            return null;
        }

        public bool FindAesKey(
            ReadOnlySpan64<byte> data,
            CryptoResultChecker checker,
            [MaybeNullWhen(false)]
            out KeyFinderResult? result)
        {
            var algos = ImmutableArray.Create(CipherAlgorithmType.Aes128, CipherAlgorithmType.Aes256, CipherAlgorithmType.Aes);

            var keys = bundle.GetKeyCollection(_bundleId).Where(k => algos.Contains(k.keyAlgo));
            foreach (var k in keys)
            {
                logger.FineFormat("Trying {0} ({1})",
                    k.key.HexDump(printAddress: false, printSpacing: false, printAscii: false),
                    k.comment
                );
                var decrypted = TestAesKey(data, k, checker);
                if (!decrypted.IsEmpty)
                {
                    result = new KeyFinderResult
                    {
                        Key = k,
                        Data = decrypted.ToArray()
                    };
                    return true;
                }
            }

            result = null;
            return false;
        }
    }
}
