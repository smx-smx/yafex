using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using log4net;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex;

public class KeysRepository
{
    private static readonly ILog log = LogManager.GetLogger(typeof(KeysRepository));

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

    public bool TryGetKey(string id, out KeyEntry key)
    {
        key = null!;
        if (_keyBundle == null) return false;
        return _keyBundle.TryGetKey(id, out key);
    }

    public T GetItem<T>(string id)
    {
        return _keyBundle.GetItem<T>(id);
    }

    public bool TryGetRaw(string id, out byte[] key, out byte[] iv)
    {
        key = [];
        iv = [];
        if (_keyBundle == null) return false;
        return _keyBundle.TryGetRaw(id, out key, out iv);
    }

    public IEnumerable<KeyEntry> GetKeyCollection(string id)
    {
        return _keyBundle.GetKeyCollection(id);
    }

    public bool TryGetKeyCollection(string id, out IEnumerable<KeyEntry> keys)
    {
        keys = Enumerable.Empty<KeyEntry>();
        if (_keyBundle == null) return false;
        return _keyBundle.TryGetKeyCollection(id, out keys);
    }

    public AesDecryptor? CreateAesDecryptor<T>(
        string keysId,
        IAesDecryptorFactory<T> factory,
        ReadOnlySpan64<byte> data, CryptoResultChecker validator
    ) where T : AesDecryptor
    {
        if (_keyBundle == null)
        {
            return null;
        }

        var keyFinder = new AesKeyFinder(_keyBundle, keysId);
        KeyFinderResult? result;
        try
        {
            if (!keyFinder.FindAesKey(data, factory, validator, out result) || result == null)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is InvalidDataException)
        {
            // unknown/misconfigured key collection: behave as "no matching key"
            // so detectors Fail gracefully instead of throwing
            log.Debug($"key collection \"{keysId}\" unavailable", ex);
            return null;
        }

        var keyEntry = result.Value.Key;
        var aes = keyEntry.GetAes();

        return factory.Create(aes);
    }
}
