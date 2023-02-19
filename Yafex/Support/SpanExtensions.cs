#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using Smx.SharpIO;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Yafex.Support
{
	public static class SpanExtensions
	{
		public unsafe static T Read<T>(this ReadOnlySpan<byte> data, int offset) where T : unmanaged {
			int length = sizeof(T);
			return Cast<byte, T>(data.Slice(offset, length))[0];
		}

		public unsafe static T Read<T>(this Span<byte> data, int offset) where T : unmanaged {
			int length = sizeof(T);
			return Cast<T>(data.Slice(offset, length))[0];
		}

		public unsafe static void Write<T>(this Span<byte> data, int offset, T value) where T : unmanaged {
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

		public unsafe static void WriteBytes(this Span<byte> data, int offset, byte[] bytes) {
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

		public static ReadOnlySpan<T> Cast<T>(this ReadOnlySpan<byte> data) where T : unmanaged {
			return MemoryMarshal.Cast<byte, T>(data);
		}

		public static Span<T> Cast<T>(this Span<byte> data) where T : unmanaged {
			return MemoryMarshal.Cast<byte, T>(data);
		}

		public static Span<T> Cast<T>(this Memory<byte> data) where T : unmanaged {
			return Cast<T>(data.Span);
		}

		public static Span<T>.Enumerator GetEnumerator<T>(this Memory<T> data) where T : unmanaged {
			return data.Span.GetEnumerator();
		}

		public static ReadOnlySpan<T> ToReadOnlySpan<T>(this Span<T> data) where T : unmanaged => data;
		public static ReadOnlySpan<T> ToReadOnlySpan<T>(this ReadOnlySpan<T> data) where T : unmanaged => data;
		public static ReadOnlySpan<T> ToReadOnlySpan<T>(this Memory<T> data) where T : unmanaged => data.Span;

		public static IEnumerable<T> ToEnumerable<T>(this ReadOnlyMemory<T> data) where T : unmanaged {
			return MemoryMarshal.ToEnumerable<T>(data);
		}
		public static IEnumerable<T> ToEnumerable<T>(this Memory<T> data) where T : unmanaged {
			return MemoryMarshal.ToEnumerable<T>(data);
		}

		private static int FieldSize(FieldInfo field)
		{
			if (field.FieldType.IsArray)
			{
				MarshalAsAttribute attr = (MarshalAsAttribute)field.GetCustomAttribute(typeof(MarshalAsAttribute), false);
				return Marshal.SizeOf(field.FieldType.GetElementType()) * attr.SizeConst;
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
			if(seenOffsets.TryGetValue(offset, out var lastSize) && lastSize >= fieldSize)
            {
				return;
            } else
            {
				seenOffsets[offset] = fieldSize;
            }

			if (field.FieldType.IsArray)
			{
				MarshalAsAttribute attr = (MarshalAsAttribute)field.GetCustomAttribute(typeof(MarshalAsAttribute), false);
				int subSize = Marshal.SizeOf(field.FieldType.GetElementType());
				for (int i = 0; i < attr.SizeConst; i++)
				{
					Array.Reverse(data, offset + (i * subSize), subSize);
				}
			}
			else
			{
				Array.Reverse(data, offset, fieldSize);
			}
		}

		/* Adapted from http://stackoverflow.com/a/2624377 */
		private static T RespectEndianness<T>(T data)
		{
			var structEndianness = Endianness.LittleEndian;
			var type = typeof(T);
			if (type.IsDefined(typeof(EndianAttribute), false))
			{
				EndianAttribute attr = (EndianAttribute)type
					.GetCustomAttribute(typeof(EndianAttribute), false);
				structEndianness = attr.Endianness;
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
				data = Marshal.PtrToStructure<T>(mem);
			}
			finally
			{
				Marshal.FreeHGlobal(mem);
			}

			return data;
		}

		public static unsafe T ReadStruct<T>(this Span<byte> data, int offset = 0) where T : struct {
			fixed(byte *dptr = data) {
				return RespectEndianness(Marshal.PtrToStructure<T>(new IntPtr(dptr + offset)));
			}
		}

		public static unsafe T ReadStruct<T>(this ReadOnlySpan<byte> data, int offset = 0) where T : struct {
			fixed (byte* dptr = data) {
				return RespectEndianness(Marshal.PtrToStructure<T>(new IntPtr(dptr + offset)));
			}
		}

		public static unsafe T ReadStruct<T>(this Memory<byte> data, int offset = 0) where T : struct {
			return ReadStruct<T>(data.Span, offset);
		}

		public static Span<T> GetField<T, TStruct, TField>(this Span<T> span, string fieldName)
			where T : unmanaged
			where TStruct : struct
			where TField : struct
		{
			var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
			var length = Marshal.SizeOf<TField>();

			return span.Slice(offset, length);
		}

		public static ReadOnlySpan<T> GetField<T, TStruct, TField>(this ReadOnlySpan<T> span, string fieldName)
			where T : unmanaged
			where TStruct : struct
			where TField : struct 
		{
			var offset = Marshal.OffsetOf<TStruct>(fieldName).ToInt32();
			var length = Marshal.SizeOf<TField>();

			return span.Slice(offset, length);
		}
	}
}
