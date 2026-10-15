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

using Microsoft.Extensions.DependencyInjection;

using Yafex;

namespace Yafex.Gui.Services;

/// <summary>DI container for the GUI, wired like the CLI host via <see cref="YafexServices"/>.</summary>
public sealed class GuiServices : IDisposable
{
    private ServiceProvider? _provider;
    private bool _disposed;

    public string? KeysPath { get; private set; }

    public FormatFinder Finder => Resolve<FormatFinder>();

    public FileFormatRepository Repository => Resolve<FileFormatRepository>();

    public Extractor Extractor => Resolve<Extractor>();

    /// <summary>
    /// Rebuilds the services with the key bundle at <paramref name="keysPath"/>.
    /// A malformed bundle falls back to no keys and is reported via the return value.
    /// </summary>
    public string? Configure(string? keysPath)
    {
        DisposeProvider();
        KeysPath = keysPath;
        string? error = null;
        KeyBundle keys;
        try
        {
            keys = YafexServices.LoadKeys(keysPath);
        }
        catch (Exception ex)
        {
            keys = KeyBundle.Empty;
            error = ex.Message;
        }
        _provider = new ServiceCollection()
            .AddYafex(new ProgramOptions(), keys)
            .BuildServiceProvider();
        return error;
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
