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
using System.Security.Cryptography;
using System.Text.Json;

namespace Yafex
{
    /// <summary>AES key size marker (replaces the obsolete
    /// <c>CipherAlgorithmType</c> SslStream enum).</summary>
    public enum KeyAlgo
    {
        Aes128,
        Aes256,
    }

    public class KeyEntry
    {
        public KeyAlgo keyAlgo;
        public CipherMode keyMode;
        public required byte[] key;
        public required byte[] iv;
        public required string comment;

        public Aes GetAes(PaddingMode paddingMode = PaddingMode.None)
        {
            // AES block size is fixed at 128 bits; only the key size varies.
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
            {
                throw new InvalidDataException($"Invalid AES key length: {key.Length} bytes");
            }

            var aes = Aes.Create();
            aes.BlockSize = 128;
            aes.KeySize = key.Length * 8;
            aes.Key = key;
            if (keyMode == CipherMode.CBC)
            {
                aes.IV = iv;
            }
            aes.Mode = keyMode;
            // $FIXME: expose in JSON, if/when needed
            aes.Padding = paddingMode;
            return aes;
        }
    }

    public class KeyBundle
    {
        private readonly Dictionary<string, KeyDTO> _keySecrets;

        private static KeyAlgo ConvertAlgoType(KeyDTO key)
        {
            if (key is Aes128Ecb || key is Aes128Cbc)
            {
                return KeyAlgo.Aes128;
            }
            if (key is Aes256Ecb || key is Aes256Cbc)
            {
                return KeyAlgo.Aes256;
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

        public KeyEntry GetKey(string id)
        {
            var key = GetItem<KeyDTO>(id);
            return ConvertKey(key);
        }

        public bool TryGetKey(string id, out KeyEntry key)
        {
            key = null!;
            try
            {
                key = GetKey(id);
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is InvalidDataException)
            {
                return false;
            }
        }

        public T GetItem<T>(string id)
        {
            if(!_keySecrets.TryGetValue(id, out var item))
            {
                throw new InvalidOperationException();
            }
            if(item is not T typedItem)
            {
                throw new InvalidOperationException();
            }
            return typedItem;
        }

        /// <summary>Reads a <see cref="RawKeyDTO"/> entry as raw (key, iv)
        /// bytes. Returns false when the id is missing or not a raw entry.</summary>
        public bool TryGetRaw(string id, out byte[] key, out byte[] iv)
        {
            key = [];
            iv = [];
            try
            {
                var dto = GetItem<RawKeyDTO>(id);
                key = Convert.FromHexString(dto.KeyMaterial);
                if (dto.KeyIV != null)
                {
                    iv = Convert.FromHexString(dto.KeyIV);
                }
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException)
            {
                return false;
            }
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

        public bool TryGetKeyCollection(string id, out IEnumerable<KeyEntry> keys)
        {
            keys = Enumerable.Empty<KeyEntry>();
            try
            {
                keys = GetKeyCollection(id);
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is InvalidDataException)
            {
                return false;
            }
        }


        /// <summary>
        /// Builds a bundle from already-loaded entries without touching disk.
        /// Used by tests and tools that synthesize keys in memory.
        /// </summary>
        public KeyBundle(IEnumerable<KeyDTO> keys)
        {
            _keySecrets = keys.ToDictionary(x => x.Id, x => x);
        }

        public KeyBundle(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var keys = JsonSerializer.Deserialize<List<KeyDTO>>(fs);
            if (keys == null)
            {
                throw new InvalidDataException($"Failed to read key bundle \"{filePath}\"");
            }
            _keySecrets = keys.ToDictionary(x => x.Id, x => x);
        }

        private KeyBundle()
        {
            _keySecrets = new Dictionary<string, KeyDTO>();
        }

        public static KeyBundle Empty { get; } = new KeyBundle();
    }
}
