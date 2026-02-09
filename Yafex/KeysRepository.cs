using System;
using System.Collections.Generic;
using System.Security.Cryptography;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex;

public class KeysRepository
{
    private readonly KeyBundle _keyBundle;

    public KeysRepository(
        KeyBundle keyBundle
    )
    {
        _keyBundle = keyBundle;
    }
    
    public KeyEntry GetKey(string id)
    {
        return _keyBundle.GetKey(id);
    }

    public T GetItem<T>(string id)
    {
        return _keyBundle.GetItem<T>(id);
    }

    public IEnumerable<KeyEntry> GetKeyCollection(string id)
    {
        return _keyBundle.GetKeyCollection(id);
    }

    public AesDecryptor? CreateAesDecryptor(
        string keysId,
        ReadOnlySpan64<byte> data, CryptoResultChecker validator
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
        var aes = keyEntry.GetAes();
        return new AesDecryptor(aes);
    }
}
