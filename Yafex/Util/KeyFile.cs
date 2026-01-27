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
using Yafex.Support;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

using log4net;

namespace Yafex.Util
{
    public delegate bool ValidatorDelegate(ReadOnlySpan<byte> data);

    public struct AesKey
    {
        public CipherMode Mode;
        public byte[] Iv;
        public byte[] Key;
        public string Comment;
    }

    public class KeyFile
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(KeyFile));

        private AesKey[] keys;

        const int AES_BLOCK_SIZE = 16;

        public KeyFile(string keyFilePath)
        {
            StreamReader sr = new StreamReader(
                new FileStream(keyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)
            );

            List<AesKey> keyList = new List<AesKey>();

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();

                byte[] key = new byte[AES_BLOCK_SIZE];
                byte[] iv = new byte[AES_BLOCK_SIZE];
                string comment = null;

                int i;
                for (i = 0; i < AES_BLOCK_SIZE * 2; i += 2)
                {
                    key[i / 2] = Convert.ToByte(line.Substring(i, 2), 16);
                }

                if (i >= line.Length)
                {
                    continue;
                }

                if (line[i] != ',')
                {
                    comment = line.Substring(i).TrimStart();

                    keyList.Add(new AesKey()
                    {
                        Key = key,
                        Mode = CipherMode.ECB,
                        Comment = comment
                    });
                    continue;
                }
                ++i;

                for (int j = 0; j < AES_BLOCK_SIZE * 2; j += 2, i += 2)
                {
                    iv[j / 2] = Convert.ToByte(line.Substring(i, 2), 16);
                }
                comment = line.Substring(i).TrimStart();

                keyList.Add(new AesKey()
                {
                    Key = key,
                    Iv = iv,
                    Mode = CipherMode.CBC,
                    Comment = comment
                });
            }

            sr.Close();

            keys = keyList.ToArray();
        }

        public Aes FindAesKey(ReadOnlySpan<byte> inData, ValidatorDelegate validatorDelegate)
        {
            Aes aes = Aes.Create();
            aes.BlockSize = AES_BLOCK_SIZE * 8;
            aes.KeySize = AES_BLOCK_SIZE * 8;

            foreach (AesKey key in keys)
            {
                logger.InfoFormat("Trying {0} ({1})",
                    key.Key.HexDump(printAddress: false, printSpacing: false, printAscii: false),
                    key.Comment
                );

                aes.Key = key.Key;
                if (key.Mode == CipherMode.CBC)
                {
                    aes.IV = key.Iv;
                }
                aes.Mode = key.Mode;
                aes.Padding = PaddingMode.None;

                ICryptoTransform decryptor = aes.CreateDecryptor();

                MemoryStream outStream = new MemoryStream(inData.Length);

                CryptoStream cs = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write);
                cs.Write(inData);
                cs.Flush();

                if (!outStream.TryGetBuffer(out ArraySegment<byte> buf))
                {
                    throw new Exception("Cannot get output buffer");
                }
                Span<byte> outSpan = buf.AsSpan();
                if (validatorDelegate(outSpan))
                {
                    return aes;
                }
            }

            return null;
        }
    }
}
