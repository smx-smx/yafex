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
﻿using DiscUtils.SquashFs;
using DiscUtils;
using Smx.SharpIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yafex.FileFormats.EpkV2;

namespace Yafex.Fuse
{
    public class YafexDirectory : VfsNode, IVfsDir
    {
        public YafexDirectory(string name, int mode = 0, long size = 0) : base(name, mode, size)
        {
        }


        public override VfsNodeType Type => VfsNodeType.Directory;
    }

    public class YafexFile : VfsNode, IVfsFile
    {
        private IDataSource source;

        public YafexFile(IDataSource source, int mode = 0) : base(source.Name, mode, source.Data.Length)
        {
            this.source = source;
        }


        public override VfsNodeType Type => VfsNodeType.File;

        public byte[]? Read(long offset, long count)
        {
            Memory<byte> data = source.Data;
            if (offset > data.Length) return null;
            var available = data.Length - offset;
            var length = Math.Min(available, count);
            return data.Slice((int)offset, (int)length).ToArray();
        }

        public int Rename(string newName)
        {
            return -(int)PosixError.ENOTSUP;
        }

        public int Truncate(long length)
        {
            return -(int)PosixError.ENOTSUP;
        }

        public int Write(byte[] buffer, long offset, long count)
        {
            return -(int)PosixError.ENOTSUP;
        }
    }

    public class YafexVfs : IVfs
    {

        public IVfsNode Root => _root;
        private readonly YafexDirectory _root = new YafexDirectory(
            "", Helpers.OctalLiteral(0755));


        public void AddDataSource(IDataSource artifact)
        {
            _root.AddNode(new YafexFile(artifact, Helpers.OctalLiteral(0444)));
        }
    }
}
