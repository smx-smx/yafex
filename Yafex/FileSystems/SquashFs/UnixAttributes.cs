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
using System.IO;

using DiscUtils;

namespace Yafex.FileSystems.SquashFs;

/// <summary>
/// Local copy of the Core-internal unix-type to <see cref="FileAttributes"/>
/// mapping (DiscUtils cannot expose internals here). Behavior unchanged.
/// </summary>
internal static class UnixAttributes
{
    public static FileAttributes FromUnixFileType(UnixFileType fileType)
    {
        switch (fileType)
        {
            case UnixFileType.Fifo:
                return FileAttributes.Device | FileAttributes.System;
            case UnixFileType.Character:
                return FileAttributes.Device | FileAttributes.System;
            case UnixFileType.Directory:
                return FileAttributes.Directory;
            case UnixFileType.Block:
                return FileAttributes.Device | FileAttributes.System;
            case UnixFileType.Regular:
                return FileAttributes.Normal;
            case UnixFileType.Link:
                return FileAttributes.ReparsePoint;
            case UnixFileType.Socket:
                return FileAttributes.Device | FileAttributes.System;
            default:
                return 0;
        }
    }
}
