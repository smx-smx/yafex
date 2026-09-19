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
using System.Collections.Generic;
using System.IO;

using log4net;

namespace Yafex.FileFormats.LgUp;

public class LgUpDetector : IFormatDetector
{
    private static readonly ILog log = LogManager.GetLogger(typeof(LgUpDetector));

    private const string KeysId = "lgup-keys";

    private readonly KeysRepository _keys;
    private readonly IDictionary<string, string> _args;

    public LgUpDetector(KeysRepository keys, IDictionary<string, string> args)
    {
        _keys = keys;
        _args = args;
    }

    private List<byte[]> CandidateKeys() =>
        KeyTrial.ResolveCandidateKeys(_args, _keys, KeysId);

    public DetectionResult Detect(IDataSource source)
    {
        var data = source.Data;
        if (data.Length < 0x110) return new LgUpDetectionResult(0, null);

        var prefix = data.Slice(0, 0x110).ToArray();
        if (!LgUpParser.TryReadOuterMagic(prefix, out _)) return new LgUpDetectionResult(0, null);

        var keys = CandidateKeys();
        if (keys.Count == 0)
        {
            log.Warn("LGUP magic found but no key available (use -o lgup:key=<hex> or lgup-keys bundle)");
            return new LgUpDetectionResult(30, null);
        }

        var file = data.ToArray();
        var parsed = LgUpParser.Parse(file, keys.ToArray());
        if (parsed == null)
        {
            log.Warn("LGUP magic found but header failed to decrypt with available keys");
            return new LgUpDetectionResult(30, null);
        }

        var (pkg, used) = parsed.Value;
        log.InfoFormat("LGUP v{0} detected ({1} sections, {2} compression)",
            pkg.Version, pkg.SectionCount, LgUpFormat.CompressTypeName(pkg.CompressType));
        return new LgUpDetectionResult(100, new LgUpContext { Key = used, Package = pkg });
    }
}
