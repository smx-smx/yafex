using System;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using Smx.SharpIO.Memory.Buffers;
using Yafex.Support;

namespace UnitTests;

[InlineArray(12)]
public struct StructWithStr
{
    private byte _element0;
}

public class SpanExtensionsTests
{
    [Test]
    public void TestAsString_WithStruct()
    {
        var stuff = new StructWithStr();
        Encoding.UTF8.GetBytes("Hello World").CopyTo(stuff);
        Assert.AreEqual("Hello World", stuff.AsString(Encoding.UTF8));
    }

    [Test]
    public void TestAsString_WithMemory()
    {
        var mem32 = new Memory<byte>(Encoding.UTF8.GetBytes("Hello World\01234"));
        var mem64 = new Memory64<byte>(Encoding.UTF8.GetBytes("Hello World\01234"));

        Assert.AreEqual("Hello World", mem32.AsString(Encoding.UTF8));
        Assert.AreEqual("Hello World", mem64.AsString(Encoding.UTF8));
    }
}
