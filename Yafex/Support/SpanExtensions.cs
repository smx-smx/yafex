using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Smx.Yafex.Support
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

		public static unsafe T ReadStruct<T>(this Span<byte> data, int offset = 0) where T : struct {
			fixed(byte *dptr = data) {
				return Marshal.PtrToStructure<T>(new IntPtr(dptr + offset));
			}
		}

		public static unsafe T ReadStruct<T>(this ReadOnlySpan<byte> data, int offset = 0) where T : struct {
			fixed (byte* dptr = data) {
				return Marshal.PtrToStructure<T>(new IntPtr(dptr + offset));
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
