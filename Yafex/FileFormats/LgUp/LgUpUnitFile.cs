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
using System.Security.Cryptography;

using log4net;

namespace Yafex.FileFormats.LgUp;

/// <summary>
/// Lazily-readable LGUP unit: the encrypted payload stays in the source file until
/// first read, then the unit is decrypted + decompressed once and memoized.
/// Random access is served from the memoized output; listing/peek never touches payloads.
/// Shared by the extractor (offsets) and the VFS (reads).
/// </summary>
public sealed class LgUpUnitFile : IVirtualFile
{
    private static readonly ILog log = LogManager.GetLogger(typeof(LgUpUnitFile));

    private readonly IDataSource _source;
    private readonly long _fileOffset;
    private readonly LgUpUnit _unit;
    private readonly byte[] _key;
    private readonly uint _compressType;
    private readonly object _gate = new();

    private byte[]? _output;

    public LgUpUnitFile(string name, IDataSource source, long fileOffset, LgUpUnit unit, byte[] key, uint compressType)
    {
        Name = name;
        _source = source;
        _fileOffset = fileOffset;
        _unit = unit;
        _key = key;
        _compressType = compressType;
    }

    public string Name { get; }

    public long Length => EnsureOutput().Length;

    private byte[] EnsureOutput()
    {
        lock (_gate)
        {
            if (_output != null) return _output;
            var file = _source.Data.ToArray();
            if (_fileOffset + _unit.PatchSize > file.Length)
                throw new InvalidDataException("unit payload overruns file");
            var payload = file.AsSpan((int)_fileOffset, (int)_unit.PatchSize).ToArray();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), _unit.Sha256))
                log.Warn("unit SHA256 mismatch, continuing");
            var content = LgUpParser.StripPad(LgUpParser.AesCbcDecrypt(payload, _key));
            _output = _unit.UncompressedSize != 0
                ? LgUpDecompress.Decompress(_compressType, content)
                : content;
            return _output;
        }
    }

    public Stream OpenRead()
    {
        return new MemoryStream(EnsureOutput(), writable: false);
    }

    /// <summary>Walks sections/units with file offsets, no payload reads.</summary>
    public static IEnumerable<(LgUpSection Section, int SectionIndex, LgUpUnit Unit, long FileOffset)> Locate(LgUpPackage pkg)
    {
        for (var i = 0; i < pkg.Sections.Count; i++)
        {
            var cursor = pkg.DataBase + pkg.PatchOffsets[i];
            foreach (var unit in pkg.Sections[i].Units)
            {
                yield return (pkg.Sections[i], i, unit, cursor);
                cursor += unit.PatchSize;
            }
        }
    }
}
