//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex.FileSystems.Fat
{
    /// <summary>
    /// VFAT long file name (LFN) slot runs: strict validation (sequence,
    /// checksum, layout) with Windows-compatible fallback (ignore the run
    /// on any defect, the short name stands).
    /// </summary>
    internal static class LongFileName
    {
        public static byte Checksum(byte[] shortName)
        {
            byte sum = 0;
            for (int i = 0; i < 11; i++)
            {
                sum = (byte)(((sum & 1) << 7) + (sum >> 1) + shortName[i]);
            }
            return sum;
        }

        public static bool TryParse(IList<byte[]> slots, byte[] shortName, out string longName)
        {
            longName = null;
            int n = slots.Count;
            if (n < 1 || n > 20) return false;

            byte expected = Checksum(shortName);
            var chars = new char[n * 13];
            for (int i = 0; i < n; i++)
            {
                byte[] s = slots[i];
                int ordinal = s[0] & 0x1F;
                bool last = (s[0] & 0x40) != 0;
                // slots precede the entry physically last-first
                if (ordinal != n - i || last != (i == 0)) return false;
                if (s[0] == 0xE5) return false;
                if (s[12] != 0) return false;
                if (s[13] != expected) return false;
                if (s[26] != 0 || s[27] != 0) return false;
                for (int c = 0; c < 13; c++)
                {
                    int off = c < 5 ? 1 + c * 2 : c < 11 ? 14 + (c - 5) * 2 : 28 + (c - 11) * 2;
                    chars[(n - 1 - i) * 13 + c] = (char)(s[off] | (s[off + 1] << 8));
                }
            }

            // cut at the NUL terminator, strip 0xFFFF padding
            int len = chars.Length;
            int nul = Array.IndexOf(chars, '\0');
            if (nul >= 0) len = nul;
            while (len > 0 && chars[len - 1] == '\uFFFF') len--;
            if (len == 0 || len > 255) return false;
            longName = new string(chars, 0, len);
            return true;
        }
    }
}
