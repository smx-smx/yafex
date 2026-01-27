using System;
using System.Collections.Generic;
using System.Security.Cryptography;

using Yafex.Support;

namespace Yafex;

public class KeysRepository
{
    private readonly Config _config;
    private readonly KeyBundle _keyBundle;

    public KeysRepository(
        Config config,
        KeyBundle keyBundle
    )
    {
        _config = config;
        _keyBundle = keyBundle;
    }

    public IEnumerable<KeyEntry> GetKeyCollection(string id)
    {
        return _keyBundle.GetKeyCollection(id);
    }

    public AesDecryptor? CreateAesDecryptor(
        string keysId,
        ReadOnlySpan<byte> data, CryptoResultChecker validator
    )
    {
        if (_keyBundle == null)
        {
            return null;
        }

        var keyFinder = new AesKeyFinder(_keyBundle, keysId);
        if (!keyFinder.FindAesKey(data, validator, out var result) || result == null)
        {
            return null;
        }

        var keyEntry = result.Value.Key;

        var blockSize = keyEntry.key.Length * 8;

        var aes = Aes.Create();
        aes.BlockSize = blockSize;
        aes.KeySize = blockSize;
        aes.Key = keyEntry.key;
        if (keyEntry.keyMode == CipherMode.CBC)
        {
            aes.IV = keyEntry.iv;
        }
        aes.Mode = keyEntry.keyMode;
        // $FIXME: expose in JSON, if/when needed
        aes.Padding = PaddingMode.None;
        return new AesDecryptor(aes);
    }
}
