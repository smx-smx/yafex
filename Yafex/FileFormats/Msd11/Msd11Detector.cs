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
using System.Text;

using log4net;

namespace Yafex.FileFormats.Msd11;

public record Msd11DetectionResult(int Confidence, Msd11Context? Context) : DetectionResult(Confidence);

/// <summary>MSDU11 detector: structural table parse first (cheap), then the
/// TOC trial-decrypt with explicit `-o msd11:key=` or the MSD11 bundle.
/// Magic without a key still reports 30 so the miss is visible.</summary>
public class Msd11Detector : IFormatDetector
{
    private static readonly ILog log = LogManager.GetLogger(typeof(Msd11Detector));

    private const string KeysId = "MSD11";

    private readonly KeysRepository _keys;
    private readonly IDictionary<string, string> _args;

    public Msd11Detector(KeysRepository keys, IDictionary<string, string> args)
    {
        _keys = keys;
        _args = args;
    }

    public DetectionResult Detect(IDataSource source)
    {
        var data = source.Data;
        if (data.Length < Msd11Structures.MinHeaderSize) return new Msd11DetectionResult(0, null);
        var head = data.Slice(0, Msd11Structures.MinHeaderSize).ToArray();
        if (Encoding.ASCII.GetString(head, 0, 6) != Msd11Structures.Magic)
        {
            return new Msd11DetectionResult(0, null);
        }

        var keys = KeyTrial.ResolveCandidateKeys(_args, _keys, KeysId);
        if (keys.Count == 0)
        {
            log.Warn("MSDU11 magic found but no key available (use -o msd11:key=<hex> or MSD11 bundle)");
            return new Msd11DetectionResult(30, null);
        }

        // TOC trial needs only the header prefix (tables + TOC blob sit up
        // front); member payloads are never paged in at detect time.
        long prefixLen = data.Length < 1 << 20 ? data.Length : 1 << 20;
        var prefix = data.Slice(0, (int)prefixLen).ToArray();
        var package = Msd11Reader.OpenToc(prefix, data.Length, keys);
        if (package == null)
        {
            log.Warn("MSDU11 magic found but TOC failed to decrypt with available keys");
            return new Msd11DetectionResult(30, null);
        }

        log.InfoFormat("MSDU11 detected ({0} members, firmware '{1}')",
            package.Members.Count, package.FirmwareName);
        return new Msd11DetectionResult(100, package);
    }
}
