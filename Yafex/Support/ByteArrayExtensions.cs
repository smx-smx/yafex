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
using System.Text;

namespace Yafex.Support
{
    public static class ByteArrayExtensions
    {
        private static readonly int ROW_PRESIZE = 0x3E;
        private static readonly int ROW_POSTSIZE = 16 + Environment.NewLine.Length;
        private static readonly int ROW_SIZE = ROW_PRESIZE + ROW_POSTSIZE;

        private static int ROUND_UP_DIV(int val, int div)
        {
            return (val + div - 1) / div;
        }

        public static string HexDump(this byte[] bytes, bool printAddress = true, bool printSpacing = true, bool printAscii = true)
        {
            int max = ROUND_UP_DIV(bytes.Length, ROW_SIZE) * ROW_SIZE;
            StringBuilder sb = new StringBuilder(max);

            int i = 0, j = 0, octets = 0;
            while (i < bytes.Length)
            {
                int offset = i & 15;
                if (offset == 0 && printAddress)
                {
                    sb.AppendFormat("{0:X8}   ", i);
                }

                sb.AppendFormat("{0:X2}", bytes[i++]);

                if (printSpacing)
                {
                    sb.Append(' ');

                    if (i > 0 && (i & 7) == 0)
                    {
                        if (++octets == 2)
                            sb.Append("  ");
                        else
                            sb.Append(' ');
                    }
                }

                if (printAscii)
                {
                    if (octets == 2 || i == bytes.Length)
                    {
                        int ws = sb.Length % ROW_SIZE;
                        if (ws < ROW_PRESIZE)
                        {
                            sb.Append(new string(' ', ROW_PRESIZE - ws));
                        }

                        // go back to saved pos, and print the bytes content
                        for (int k = j; k < i; k++)
                        {
                            if (bytes[k] < 0x20)
                                sb.Append('.');
                            else
                                sb.Append((char)(bytes[k]));
                        }
                        sb.AppendLine();

                        j = i;
                        octets = 0;
                    }
                }
            }

            return sb.ToString();
        }

        public static string AsString(this byte[] arr, Encoding encoding)
        {
            var str = encoding.GetString(arr);
            var nullOffset = str.IndexOf('\0');
            if (nullOffset > -1)
            {
                return str.Substring(0, nullOffset);
            }

            return str;
        }
    }
}
