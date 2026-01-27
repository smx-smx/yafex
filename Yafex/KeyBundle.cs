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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace Yafex
{
    public class KeyEntry
    {
        public CipherAlgorithmType keyAlgo;
        public CipherMode keyMode;
        public required byte[] key;
        public required byte[] iv;
        public required string comment;
    }

    public class KeyBundle
    {
        private readonly Dictionary<string, KeySecretDTO> _keySecrets;

        private static CipherAlgorithmType ConvertAlgoType(KeyDTO key)
        {
            if (key is Aes128Ecb || key is Aes128Ecb)
            {
                return CipherAlgorithmType.Aes128;
            }
            if (key is Aes256Ecb || key is Aes256Cbc)
            {
                return CipherAlgorithmType.Aes256;
            }
            throw new NotSupportedException(key.ToString());
        }

        private static CipherMode ConvertKeyMode(KeyDTO key)
        {
            if (key is Aes128Ecb || key is Aes256Ecb)
            {
                return CipherMode.ECB;
            }
            if (key is Aes128Cbc || key is Aes256Cbc)
            {
                return CipherMode.CBC;
            }
            throw new NotSupportedException(key.ToString());
        }

        private static KeyEntry ConvertKey(KeyDTO key)
        {
            var keyAlgo = ConvertAlgoType(key);
            var keyMode = ConvertKeyMode(key);
            return new KeyEntry
            {
                keyAlgo = keyAlgo,
                keyMode = keyMode,
                comment = key.Description ?? string.Empty,
                key = key is BasicKeyDTO basicKey
                    ? Convert.FromHexString(basicKey.KeyMaterial)
                    : [],
                iv = key is KeyWithIVDTO keyWithIV
                    ? Convert.FromHexString(keyWithIV.KeyIV)
                    : []
            };
        }

        public IEnumerable<KeyEntry> GetKeyCollection(string id)
        {
            if (!_keySecrets.TryGetValue(id, out var item))
            {
                throw new InvalidOperationException();
            }
            if (item is not KeyCollectionDTO keyList)
            {
                throw new InvalidDataException();
            }
            return keyList.Keys.Select(ConvertKey);
        }


        public KeyBundle(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var keys = JsonSerializer.Deserialize<List<KeySecretDTO>>(fs);
            if (keys == null)
            {
                throw new InvalidDataException($"Failed to read key bundle \"{filePath}\"");
            }
            _keySecrets = keys.ToDictionary(x => x.Id, x => x);
        }
    }
}
