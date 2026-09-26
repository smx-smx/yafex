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
// Synthetic KeyTrial tests (no bundle files on disk).
using System.Collections.Generic;

using NUnit.Framework;

using Yafex;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class KeyTrialTests
{
    [Test]
    public void ExplicitHexKey_Wins()
    {
        var args = new Dictionary<string, string> { { "key", new string('A', 32) } };
        var keys = KeyTrial.ResolveCandidateKeys(args, EmptyKeys(), "lgup-keys");
        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual(16, keys[0].Length);
    }

    [Test]
    public void InvalidHex_YieldsEmpty()
    {
        var args = new Dictionary<string, string> { { "key", "ZZZ" } };
        var keys = KeyTrial.ResolveCandidateKeys(args, EmptyKeys(), "no-such-collection");
        Assert.IsEmpty(keys);
    }

    [Test]
    public void MissingCollection_YieldsEmpty()
    {
        var keys = KeyTrial.ResolveCandidateKeys(new Dictionary<string, string>(), EmptyKeys(), "no-such-collection");
        Assert.IsEmpty(keys);
    }
}
