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
// Synthetic KeyBundle tests (in-memory construction, no bundle files).
using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using Yafex;

namespace UnitTests;

[TestFixture]
public class KeyBundleTests
{
    [Test]
    public void InMemory_ResolvesKeys()
    {
        var key = new byte[16];
        Random.Shared.NextBytes(key);
        var iv = new byte[16];
        Random.Shared.NextBytes(iv);
        var bundle = new KeyBundle(new KeyDTO[]
        {
            new Aes128Cbc { Id = "hdr", KeyMaterial = Convert.ToHexString(key), KeyIV = Convert.ToHexString(iv) },
            new KeyCollectionDTO
            {
                Id = "col",
                Keys = new List<KeyDTO>
                {
                    new Aes128Ecb { KeyMaterial = Convert.ToHexString(key), Description = "one" },
                },
            },
        });
        var repo = new KeysRepository(bundle);
        Assert.IsTrue(repo.TryGetKey("hdr", out var entry));
        Assert.AreEqual(16, entry.key.Length);
        Assert.AreEqual(16, entry.iv.Length);
        Assert.IsTrue(repo.TryGetKeyCollection("col", out var entries));
        Assert.AreEqual(1, entries.Count());
        Assert.IsFalse(repo.TryGetKey("missing", out _));
    }
}
