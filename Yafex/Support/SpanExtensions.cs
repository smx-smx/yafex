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
        public static string AsString<TBuffer>(
            this TBuffer buffer,
            Encoding encoding
        )
            where TBuffer : struct
        {
            int length = Unsafe.SizeOf<TBuffer>();
            ref var bufferRef = ref Unsafe.AsRef(in buffer);
            ref var start = ref Unsafe.As<TBuffer, byte>(ref buffer);
            var span = MemoryMarshal.CreateReadOnlySpan(ref start, length);
            return span.AsString(encoding);
        }

        public static string AsString(this ReadOnlyMemory64<byte> data, Encoding encoding)
        {
            // we're not gonna have a string larger than 2GB, hopefully
            return AsString((ReadOnlySpan<byte>)data.Span, encoding);
        }

        public static string AsString(this Memory64<byte> data, Encoding encoding)
        {
            // we're not gonna have a string larger than 2GB, hopefully
            return AsString((ReadOnlySpan<byte>)data.Span, encoding);
        }

        public static string AsString(this ReadOnlyMemory<byte> data, Encoding encoding)
        {
            return AsString(data.Span, encoding);
        }

        public static string AsString(this Memory<byte> data, Encoding encoding)
        {
            return AsString(data.Span, encoding);
        }

        public static string AsString(this ReadOnlySpan<byte> data, Encoding encoding)
        {
            var nullPos = data.IndexOf((byte)0x00);
            if(nullPos < 0)
            {
                return encoding.GetString(data);
            } else
            {
                return encoding.GetString(data.Slice(0, nullPos));
            }
        }

        public static string AsString(this Span<byte> data, Encoding encoding)
        {
            var nullPos = data.IndexOf((byte)0x00);
            if(nullPos < 0)
            {
                return encoding.GetString(data);
            } else
            {
                return encoding.GetString(data.Slice(0, nullPos));
            }
        }

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

        private static int FieldSize(FieldInfo field)
        {
            if (field.FieldType.IsArray)
            {
                var attr = field.GetCustomAttribute<MarshalAsAttribute>(false);
                if(attr == null)
                {
                    throw new InvalidOperationException("Cannot determine Field Size: missing [MarshalAs] attribute");
                }
                var elementType = field.FieldType.GetElementType();
                if(elementType == null)
                {
                    throw new InvalidOperationException();
                }
                return Marshal.SizeOf(elementType) * attr.SizeConst;
            }
            else
            {
                if (field.FieldType.IsEnum)
                {
                    return Marshal.SizeOf(Enum.GetUnderlyingType(field.FieldType));
                }
                return Marshal.SizeOf(field.FieldType);
            }
        }

        private static void SwapEndian<T>(byte[] data, FieldInfo field, Dictionary<int, int> seenOffsets)
        {
            var type = typeof(T);
            int offset = Marshal.OffsetOf(type, field.Name).ToInt32();

            int fieldSize = FieldSize(field);

            // $NOTE: this doesn't work in case of 2 overlapping unions
            if (seenOffsets.TryGetValue(offset, out var lastSize) && lastSize >= fieldSize)
            {
                return;
            }
            else
            {
                seenOffsets[offset] = fieldSize;
            }

            if (field.FieldType.IsArray)
            {
                var attr = field.GetCustomAttribute<MarshalAsAttribute>(false);
                var elementType = field.FieldType.GetElementType();
                if (attr != null && elementType != null)
                {
                    int subSize = Marshal.SizeOf(elementType);
                    for (int i = 0; i < attr.SizeConst; i++)
                    {
                        Array.Reverse(data, offset + (i * subSize), subSize);
                    }
                }
            }
            else
            {
                Array.Reverse(data, offset, fieldSize);
            }
        }

        /* Adapted from http://stackoverflow.com/a/2624377 */
        private static T RespectEndianness<T>(T data) where T : notnull
        {
            var structEndianness = Endianness.LittleEndian;
            var type = typeof(T);
            if (type.IsDefined(typeof(EndianAttribute), false))
            {
                var attr = type.GetCustomAttribute<EndianAttribute>(false);
                if (attr != null)
                {
                    structEndianness = attr.Endianness;
                }
            }

            var sz = Marshal.SizeOf(data);
            var mem = Marshal.AllocHGlobal(sz);
            try
            {
                Marshal.StructureToPtr(data, mem, false);
                var bytes = new byte[sz];
                Marshal.Copy(mem, bytes, 0, sz);

                // marks <offset, size> pairs
                var seenOffsets = new Dictionary<int, int>();

                foreach (var field in type.GetFields())
                {
                    if (field.IsDefined(typeof(EndianAttribute), false))
                    {
                        Endianness fieldEndianess = ((EndianAttribute)field.GetCustomAttributes(typeof(EndianAttribute), false)[0]).Endianness;
                        if (
                            (fieldEndianess == Endianness.BigEndian && BitConverter.IsLittleEndian) ||
                            (fieldEndianess == Endianness.LittleEndian && !BitConverter.IsLittleEndian)
                        )
                        {
                            SwapEndian<T>(bytes, field, seenOffsets);
                        }
                    }
                    else if (
                      (structEndianness == Endianness.BigEndian && BitConverter.IsLittleEndian) ||
                      (structEndianness == Endianness.LittleEndian && !BitConverter.IsLittleEndian)
                  )
                    {
                        SwapEndian<T>(bytes, field, seenOffsets);
                    }
                }
                Marshal.Copy(bytes, 0, mem, sz);
                var res = Marshal.PtrToStructure<T>(mem);
                if(res == null)
                {
                    throw new InvalidOperationException("Marshal.PtrToStructure failed");
                }
                data = res;
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }

            return data;
        }

        public static unsafe T ReadStruct<T>(this Span64<byte> data, long offset = 0) where T : struct
        {
            fixed(byte *dptr = data)
            {
                return RespectEndianness(Marshal.PtrToStructure<T>(new IntPtr(dptr + offset)));
            }
        }

        public static unsafe T ReadStruct<T>(this Span<byte> data, int offset = 0) where T : struct
        {
            fixed (byte* dptr = data)
            {
                return RespectEndianness(Marshal.PtrToStructure<T>(new IntPtr(dptr + offset)));
            }
        }

        public static unsafe T ReadStruct<T>(this ReadOnlySpan<byte> data, int offset = 0) where T : struct
        {
            fixed (byte* dptr = data)
            {
                return RespectEndianness(Marshal.PtrToStructure<T>(new IntPtr(dptr + offset)));
            }
        }

        public static unsafe T ReadStruct<T>(this ReadOnlySpan64<byte> data, long offset = 0) where T : struct
        {
            fixed (byte* dptr = data)
            {
                return RespectEndianness(Marshal.PtrToStructure<T>(new IntPtr(dptr + offset)));
            }
        }

        public static unsafe T ReadStruct<T>(this Memory<byte> data, int offset = 0) where T : struct
        {
            return ReadStruct<T>(data.Span, offset);
        }

        public static unsafe T ReadStruct<T>(this Memory64<byte> data, long offset = 0) where T : struct
        {
            return ReadStruct<T>(data.Span, offset);
        }

        public static Memory<T> GetField<T, TStruct, TField>(this Memory<T> memory, string fieldName)
        where T : unmanaged
            where TStruct : struct
            where TField : struct
        {
            var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
            var length = Marshal.SizeOf<TField>();

            return memory.Cast<T, byte>()
                .Slice(offset, length)
                .Cast<byte, T>();
        }

        public static Memory64<T> GetField<T, TStruct, TField>(this Memory64<T> memory, string fieldName)
        where T : unmanaged
            where TStruct : struct
            where TField : struct
        {
            var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
            var length = Marshal.SizeOf<TField>();

            return memory.Cast<T, byte>()
                .Slice(offset, length)
                .Cast<byte, T>();
        }

        public static Span<T> GetField<T, TStruct, TField>(this Span<T> span, string fieldName)
            where T : unmanaged
            where TStruct : struct
            where TField : struct
        {
            var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
            var length = Marshal.SizeOf<TField>();

            return span
                .Cast<T, byte>()
                .Slice(offset, length)
                .Cast<byte, T>();
        }

        public static Span64<T> GetField<T, TStruct, TField>(this Span64<T> span, string fieldName)
            where T : unmanaged
            where TStruct : struct
            where TField : struct
        {
            var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
            var length = Marshal.SizeOf<TField>();

            return span
                .Cast<T, byte>()
                .Slice(offset, length)
                .Cast<byte, T>();
        }

        public static ReadOnlySpan<T> GetField<T, TStruct, TField>(this ReadOnlySpan<T> span, string fieldName)
            where T : unmanaged
            where TStruct : struct
            where TField : struct
        {
            var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
            var length = Marshal.SizeOf<TField>();

            return span
                .Cast<T, byte>()
                .Slice(offset, length)
                .Cast<byte, T>();
        }

        public static ReadOnlySpan64<T> GetField<T, TStruct, TField>(this ReadOnlySpan64<T> span, string fieldName)
            where T : unmanaged
            where TStruct : struct
            where TField : struct
        {
            var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
            var length = Marshal.SizeOf<TField>();

            return span
                .Cast<T, byte>()
                .Slice(offset, length)
                .Cast<byte, T>();
        }
    }
}
