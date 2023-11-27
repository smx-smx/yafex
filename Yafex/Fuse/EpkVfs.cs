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
﻿using DiscUtils;
using DiscUtils.SquashFs;
using DiscUtils.Vfs;
using Org.BouncyCastle.Crypto.Prng.Drbg;
using Smx.SharpIO;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.EpkV3;

namespace Yafex.Fuse
{
    public class EpkDirectory : VfsNode, IVfsDir
    {
        public EpkDirectory(string name, int mode = 0, long size = 0) : base(name, mode, size)
        {
        }


        public override VfsNodeType Type => VfsNodeType.Directory;
    }

    public class EpkFile : VfsNode, IVfsFile
    {
        private IDataSource source;

        public EpkFile(IDataSource source, int mode = 0) : base(source.Name, mode, source.Data.Length)
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

    public class SquashfsDirNode : VfsNode, IVfsDir
    {
        public SquashfsDirNode(string name, int mode, long size=0) : base(name, mode, size)
        {
        }

        public override VfsNodeType Type => VfsNodeType.Directory;
    }

    public class SquashfsFileNode : VfsNode, IVfsFile
    {
        private readonly DiscFileInfo discNode;

        public SquashfsFileNode(DiscFileInfo f, int mode)
            : base(f.Name, mode, f.Length)
        {
            this.discNode = f;
        }

        public override VfsNodeType Type => VfsNodeType.File;

        public byte[]? Read(long offset, long count)
        {
            using var stream = discNode.OpenRead();
            if (offset > stream.Length) return null;
            stream.Seek(offset, System.IO.SeekOrigin.Begin);

            var available = stream.Length - offset;
            var length = Math.Min(available, count);

            var buf = new byte[length];
            try
            {
                stream.Read(buf);
            }
            catch (Exception)
            {
                // signal I/O error
                return null;
            }
            return buf;
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

    internal class EpkVfs : YafexVfs
    {

        public IVfsNode Root => _root;
        private readonly EpkDirectory _root = new EpkDirectory(
            "", Helpers.OctalLiteral(0755));

        
        private void BuildSquashfsTree(IVfsNode vfsNode, DiscDirectoryInfo dirNode)
        {
            var dirs = dirNode.GetDirectories();
            foreach(var d in dirs)
            {
                // $FIXME: disregarding original permissions
                var subdir = new SquashfsDirNode(d.Name,
                    Helpers.OctalLiteral(0755)
                );
                vfsNode.AddNode(subdir);
                BuildSquashfsTree(subdir, d);
            }

            var files = dirNode.GetFiles();
            foreach(var f in files)
            {
                // $FIXME: disregarding original permissions
                var subfile = new SquashfsFileNode(f, Helpers.OctalLiteral(0666));
                vfsNode.AddNode(subfile);
            }
        }

        private void HandlePak(IVfsNode parent, IDataSource ds)
        {
            // add file node
            _root.AddNode(new EpkFile(ds, Helpers.OctalLiteral(0444)));

            // check if we can process the artifact further
            if (ds.Data.Length < 4) return;
            var fourCC = ds.Data.Span.Slice(0, 4).Cast<uint>()[0];
            if (fourCC == 0x73717368) // hsqs
            {
                try
                {
                    var mountPoint = new EpkDirectory(ds.Name, Helpers.OctalLiteral(0755));                    
                    parent.AddNode(mountPoint);

                    // $FIXME: leaking resource
                    var rdr = new SquashFileSystemReader(new SpanStream(ds.Data));
                    BuildSquashfsTree(mountPoint, rdr.Root);
                } catch(Exception ex) {
                    Console.WriteLine("FAIL: " + ds.Name);
                }
            }
        }

        public EpkVfs(string epkPath) {

            var a = new Epk2Addon();
            var conf = new Support.Config()
            {
                ConfigDir = @"C:\TEMP"
            };
            var detector = a.CreateDetector(conf);
            var mf = new Yafex.Support.MFile(epkPath);
            var res = detector.Detect(mf);
            
            var extractor = a.CreateExtractor(conf, res);
            var output = extractor.Extract(mf);

            foreach(var artifact in output){
                if (artifact.Name == null) continue;
                HandlePak(_root, artifact);
            }
        }
    }
}
