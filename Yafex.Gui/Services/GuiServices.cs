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
using System.IO;

using Microsoft.Extensions.DependencyInjection;

using Yafex;

namespace Yafex.Gui.Services;

/// <summary>Minimal DI setup mirroring the CLI host (formats, keys, finder).</summary>
public sealed class GuiServices : IDisposable
{
    private ServiceProvider? _provider;
    private bool _disposed;

    public string? KeysPath { get; private set; }

    public FormatFinder Finder => Resolve<FormatFinder>();

    public FileFormatRepository Repository => Resolve<FileFormatRepository>();

    public void Configure(string? keysPath)
    {
        DisposeProvider();
        KeysPath = keysPath;
        var services = new ServiceCollection();
        FileFormatRepository.RegisterFileFormats(services);
        KeyBundle keys = KeyBundle.Empty;
        if (keysPath != null && System.IO.File.Exists(keysPath))
        {
            try
            {
                keys = new KeyBundle(keysPath);
            }
            catch (Exception)
            {
                keys = KeyBundle.Empty;
            }
        }
        services.AddSingleton(keys);
        services.AddSingleton(new ProgramOptions());
        services.AddSingleton<KeysRepository>();
        services.AddSingleton<FileFormatRepository>();
        services.AddSingleton<FormatFinder>();
        _provider = services.BuildServiceProvider();
    }

    private T Resolve<T>() where T : notnull
    {
        if (_provider == null)
        {
            Configure(KeysPath);
        }
        return _provider!.GetRequiredService<T>();
    }

    private void DisposeProvider()
    {
        _provider?.Dispose();
        _provider = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeProvider();
    }
}
