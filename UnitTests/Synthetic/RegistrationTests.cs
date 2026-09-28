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
// Registration completeness: explicit registration stays, but omissions
// fail here instead of as silent non-detection at runtime.
using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using Yafex;
using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class RegistrationTests
{
    private static FileFormatRepository Repo()
    {
        var services = new ServiceCollection();
        FileFormatRepository.RegisterFileFormats(services);
        services.AddSingleton(KeyBundle.Empty);
        services.AddSingleton<KeysRepository>();
        services.AddSingleton<FileFormatRepository>();
        return services.BuildServiceProvider().GetRequiredService<FileFormatRepository>();
    }

    // Formats intentionally without a registered addon. Keep this list
    // empty unless there is a platform-level reason (like below).
    private static readonly HashSet<FileFormat> UnregisteredOk = new HashSet<FileFormat>
    {
        // Cygwin-only v4 path (shells out to unsquashfs); the managed
        // Squashfs4 addon serves all platforms instead.
        FileFormat.Squashfs,
    };

    [Test]
    public void EveryFileFormat_HasAddon()
    {
        var repo = Repo();
        var missing = Enum.GetValues(typeof(FileFormat)).Cast<FileFormat>()
            .Where(f => !UnregisteredOk.Contains(f) && !repo.TryGetAddonForFormat(f, out _))
            .ToList();
        Assert.IsEmpty(missing, "formats without addon: " + string.Join(",", missing));
    }

    [Test]
    public void EveryAddon_BuildsDetector()
    {
        var repo = Repo();
        foreach (var fmt in repo.GetRegisteredFormats())
        {
            Assert.IsTrue(repo.TryGetAddonForFormat(fmt, out var addon) && addon != null);
            Assert.DoesNotThrow(() => addon!.CreateDetector(new Dictionary<string, string>()), $"{fmt}");
        }
    }

    [Test]
    public void ExpanderNames_AreUnique()
    {
        var names = ContentExpanders.All.Select(e => e.Name).ToList();
        Assert.IsNotEmpty(names);
        var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.IsEmpty(dupes, "duplicate expanders: " + string.Join(",", dupes));
    }
}
