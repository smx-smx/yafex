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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

using Smx.SharpIO;
using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

namespace Yafex.Support
{
    public static class SpanExtensions
    {
        public unsafe static T Read<T>(this ReadOnlySpan<byte> data, int offset) where T : unmanaged
        {
            int length = sizeof(T);
            return Cast<byte, T>(data.Slice(offset, length))[0];
        }

        public unsafe static T Read<T>(this ReadOnlySpan64<byte> data, int offset) where T : unmanaged
        {
            int length = sizeof(T);
            return Cast<byte, T>(data.Slice(offset, length))[0];
        }

        public unsafe static T Read<T>(this Span<byte> data, int offset) where T : unmanaged
        {
            int length = sizeof(T);
            return Cast<T>(data.Slice(offset, length))[0];
        }

        public unsafe static void Write<T>(this Span<byte> data, int offset, T value) where T : unmanaged
        {
            int length = sizeof(T);
            Cast<T>(data.Slice(offset, length))[0] = value;
        }

#if INTERNAL
		public unsafe static void CopyTo<TFrom, TTo>(this Span<TFrom> data, Span<TTo> dest, int dstOffset)
			where TFrom : unmanaged
			where TTo : unmanaged {
			var srcBytes = MemoryMarshal.Cast<TFrom, byte>(data);
			var dstBytes = MemoryMarshal.Cast<TTo, byte>(dest).Slice(dstOffset);
			srcBytes.CopyTo(dstBytes);
		}

		public unsafe static void CopyTo<TFrom, TTo>(this Memory<TFrom> data, Memory<TTo> dest, int dstOffset)
			where TFrom : unmanaged
			where TTo : unmanaged {
			data.Span.CopyTo(dest.Span, dstOffset);
		}
#endif

        public unsafe static void WriteBytes(this Span<byte> data, int offset, byte[] bytes)
        {
            var start = data.Slice(offset, bytes.Length);
            var dspan = new Span<byte>(bytes);
            dspan.CopyTo(start);
        }

        public static ReadOnlySpan<Tto> Cast<Tfrom, Tto>(this ReadOnlySpan<Tfrom> data)
            where Tto : struct
            where Tfrom : struct
        {
            return MemoryMarshal.Cast<Tfrom, Tto>(data);
        }

        public static ReadOnlySpan64<Tto> Cast<Tfrom, Tto>(this ReadOnlySpan64<Tfrom> data)
            where Tto : struct
            where Tfrom : struct
        {
            return MemoryMarshal64.Cast<Tfrom, Tto>(data);
        }

        public static ReadOnlySpan<T> Cast<T>(this ReadOnlySpan<byte> data) where T : unmanaged
        {
            return MemoryMarshal.Cast<byte, T>(data);
        }

        public static ReadOnlySpan64<T> Cast<T>(this ReadOnlySpan64<byte> data) where T : unmanaged
        {
            return MemoryMarshal64.Cast<byte, T>(data);
        }

        public static Span<T> Cast<T>(this Span<byte> data) where T : unmanaged
        {
            return MemoryMarshal.Cast<byte, T>(data);
        }

        public static Span64<T> Cast<T>(this Span64<byte> data) where T : unmanaged
        {
            return MemoryMarshal64.Cast<byte, T>(data);
        }

        public static Span<T> Cast<T>(this Memory<byte> data) where T : unmanaged
        {
            return Cast<T>(data.Span);
        }

        public static Span64<T> Cast<T>(this Memory64<byte> data) where T : unmanaged
        {
            return Cast<T>(data.Span);
        }

        public static Span<T>.Enumerator GetEnumerator<T>(this Memory<T> data) where T : unmanaged
        {
            return data.Span.GetEnumerator();
        }

        public static ReadOnlySpan<T> AsReadonlySpan<T>(this Span<T> data) where T : unmanaged => data;
        public static ReadOnlySpan64<T> AsReadonlySpan<T>(this Span64<T> data) where T : unmanaged => data;
        public static ReadOnlySpan<T> AsReadonlySpan<T>(this ReadOnlySpan<T> data) where T : unmanaged => data;
        public static ReadOnlySpan64<T> AsReadonlySpan<T>(this ReadOnlySpan64<T> data) where T : unmanaged => data;
        public static ReadOnlySpan<T> AsReadonlySpan<T>(this Memory<T> data) where T : unmanaged => data.Span;
        public static ReadOnlySpan64<T> AsReadonlySpan<T>(this Memory64<T> data) where T : unmanaged => data.Span;

        public static IEnumerable<T> ToEnumerable<T>(this ReadOnlyMemory<T> data) where T : unmanaged
        {
            return MemoryMarshal.ToEnumerable<T>(data);
        }
        public static IEnumerable<T> ToEnumerable<T>(this ReadOnlyMemory64<T> data) where T : unmanaged
        {
            return MemoryMarshal64.ToEnumerable<T>(data);
        }
        public static IEnumerable<T> ToEnumerable<T>(this Memory<T> data) where T : unmanaged
        {
            return MemoryMarshal.ToEnumerable<T>(data);
        }
        public static IEnumerable<T> ToEnumerable<T>(this Memory64<T> data) where T : unmanaged
        {
            return MemoryMarshal64.ToEnumerable<T>(data);
        }

    }
}
