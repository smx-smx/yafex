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

using log4net;

using Microsoft.Extensions.DependencyInjection;

namespace Yafex;

/// <summary>Service wiring shared by every front end (CLI host, GUI).</summary>
public static class YafexServices
{
    private static readonly ILog log = LogManager.GetLogger(typeof(YafexServices));

    /// <summary>Registers formats, keys, options, detection and extraction.</summary>
    public static IServiceCollection AddYafex(this IServiceCollection services, ProgramOptions opts, KeyBundle keys)
    {
        FileFormatRepository.RegisterFileFormats(services);
        services.AddSingleton(keys);
        services.AddSingleton(opts);
        services.AddSingleton<KeysRepository>();
        services.AddSingleton<FileFormatRepository>();
        services.AddSingleton<FormatFinder>();
        services.AddSingleton<Extractor>();
        return services;
    }

    /// <summary>
    /// Loads the key bundle at <paramref name="path"/>. A missing file means no
    /// keys (logged, not fatal); a malformed one throws.
    /// </summary>
    public static KeyBundle LoadKeys(string? path)
    {
        if (path != null && File.Exists(path))
        {
            return new KeyBundle(path);
        }
        log.WarnFormat("Secrets file \"{0}\" not found, continuing without keys", path ?? "");
        return KeyBundle.Empty;
    }
}
