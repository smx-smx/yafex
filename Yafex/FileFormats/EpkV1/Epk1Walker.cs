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
using System.Runtime.InteropServices;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex.FileFormats.EpkV1
{
    /// <summary>
    /// Header-only walk of EPKv1 packages shared by the extractor and the lazy VFS:
    /// (pak name, header offset, payload size) without reading payloads.
    /// </summary>
    public static class Epk1Walker
    {
        public static IEnumerable<(string Name, long Offset, long Size)> EnumeratePaks(
            Memory64<byte> fileData, Epk1Type epkType)
        {
            switch (epkType)
            {
                case Epk1Type.BigEndian:
                {
                    var hdr = fileData.ReadStruct<Epk1BeHeader>();
                    for (var i = 0; i < hdr.PakCount; i++)
                    {
                        var rec = hdr.pakRecs[i];
                        var offset = rec.offset.BigEndianToHost();
                        if (offset == 0) continue; // same as the extractor
                        var pakHdr = fileData.ReadStruct<PakHeader>(offset);
                        yield return (
                            pakHdr.PakName,
                            offset,
                            pakHdr.imageSize.BigEndianToHost());
                    }
                    break;
                }
                case Epk1Type.Old:
                {
                    var hdr = fileData.ReadStruct<Epk1Header>();
                    for (var i = 0; i < hdr.pakCount; i++)
                    {
                        var rec = hdr.pakRecs[i];
                        var pakHdr = fileData.ReadStruct<PakHeader>(rec.offset);
                        yield return (pakHdr.PakName, rec.offset, pakHdr.imageSize);
                    }
                    break;
                }
                default:
                    throw new NotImplementedException();
            }
        }
    }

}
