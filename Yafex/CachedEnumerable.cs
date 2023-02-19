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
﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Yafex
{
	public class CachedEnumerable<T> : IReadOnlyList<T>, IEnumerable<T>, IDisposable
	{
		private readonly IList<T> cache = new List<T>();
		private readonly IEnumerator<T> items;

		public CachedEnumerable(IEnumerable<T> items) {
			this.items = items.GetEnumerator();
		}

		public T this[int index] {
			get {
				// check if the item is already there
				if (this.cache.Count > index) {
					return this.cache[index];
				}

				// read until we find it
				foreach (var item in this) {
					if (this.cache.Count > index) {
						return item;
					}
				}

				// we didn't find it
				throw new IndexOutOfRangeException();
			}
		}

		public int Count {
			get {
				return this.cache.Count;
			}
		}

		public void Dispose() {
			this.items?.Dispose();
		}

		public IEnumerator<T> GetEnumerator() {
			foreach (var item in this.cache) {
				yield return item;
			}

			while (this.items.MoveNext()) {
				var item = this.items.Current;
				this.cache.Add(item);
				yield return item;
			}
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return GetEnumerator();
		}
	}
}
