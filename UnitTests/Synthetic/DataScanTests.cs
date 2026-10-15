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
using System.Linq;
using System.Text;

using NUnit.Framework;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace UnitTests;

[TestFixture]
public class DataScanTests
{
    private static Memory64<byte> Data(string s) => new Memory64<byte>(Encoding.ASCII.GetBytes(s));

    [Test]
    public void FindAll_ReturnsEveryHitAscending()
    {
        var data = Data("xxUBI#yyUBI#zUBI#");
        CollectionAssert.AreEqual(new long[] { 2, 8, 13 }, DataScan.FindAll(data, "UBI#"u8.ToArray()).ToArray());
        CollectionAssert.AreEqual(new long[] { 8 }, DataScan.FindAll(data, "UBI#"u8.ToArray(), start: 3, alignment: 4).ToArray());
        Assert.IsEmpty(DataScan.FindAll(data, "hsqs"u8.ToArray()));
    }

    [Test]
    public void IndexOf_FindsNeedlesStraddlingStepBoundaries()
    {
        // with 8-byte steps every placement crosses or touches a boundary
        for (int at = 0; at <= 20; at++)
        {
            var bytes = new byte[24];
            "SECURITY"u8.ToArray().CopyTo(bytes, at > 16 ? 16 : at);
            int expected = at > 16 ? 16 : at;
            Assert.AreEqual(expected, DataScan.IndexOf(new Memory64<byte>(bytes), "SECURITY"u8.ToArray(), 0, stepSize: 8), $"at {at}");
        }
    }

    [Test]
    public void Window_ClampsToData()
    {
        var data = Data("0123456789");
        Assert.AreEqual("789", Encoding.ASCII.GetString(DataScan.Window(data, 7, 100)));
        Assert.IsEmpty(DataScan.Window(data, 10, 4));
        Assert.IsEmpty(DataScan.Window(data, -1, 4));
    }
}
