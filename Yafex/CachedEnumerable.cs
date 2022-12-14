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
