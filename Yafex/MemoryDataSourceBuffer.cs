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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex
{
    public class MemoryDataSourceBuffer : IDisposable
    {
        private readonly MemoryStream mem;

        private readonly string name;
        private readonly DataSourceFlags flags;

        public MemoryDataSourceBuffer(string name, DataSourceFlags flags)
        {
            mem = new MemoryStream();
            this.name = name;
            this.flags = flags;
        }

        public void Write(byte[] data)
        {
            mem.Write(data);
        }
        public void Write(ReadOnlySpan<byte> data)
        {
            mem.Write(data);
        }

        public void WriteAt(int index, ReadOnlySpan<byte> data)
        {
            var savedPos = mem.Position;
            mem.Position = index;
            mem.Write(data);
            mem.Position = savedPos;
        }

        public MemoryDataSource ToDataSource()
        {
            return new MemoryDataSource(mem.ToArray())
            {
                Name = name,
                Flags = flags
            };
        }

        public void Dispose()
        {
            mem.Dispose();
        }
    }
}
