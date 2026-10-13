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
using DiscUtils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats;
using Yafex.FileFormats.Epk;
using Yafex.FileFormats.EpkV1;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.EpkV3;
using Yafex.Support;

namespace Yafex.Fuse
{
    /// <summary>
    /// A virtual package file, backed by a lazy <see cref="PakStream"/>. No payload
    /// is read or decrypted until a FUSE read request arrives.
    /// </summary>
    public class PakVfsFile : VfsNode, IVfsFile
    {
        private readonly IVirtualFile _image;

        public PakVfsFile(IVirtualFile image, int mode)
            : base(image.Name, mode, image.Length)
        {
            _image = image;
        }

        public override VfsNodeType Type => VfsNodeType.File;

        public byte[]? Read(long offset, long count)
        {
            using var stream = _image.OpenRead();
            if (offset > stream.Length) return null;
            stream.Seek(offset, System.IO.SeekOrigin.Begin);

            var len = (int)Math.Min(count, stream.Length - offset);
            var buf = new byte[len];

            int total = 0;
            while (total < len)
            {
                int n = stream.Read(buf, total, len - total);
                if (n <= 0) break;
                total += n;
            }
            if (total != len) return null;
            return buf;
        }

        public int Rename(string newName) => -(int)PosixError.ENOTSUP;
        public int Truncate(long length) => -(int)PosixError.ENOTSUP;
        public int Write(byte[] buffer, long offset, long count) => -(int)PosixError.ENOTSUP;
    }

    public class DiscDirNode : VfsNode, IVfsDir
    {
        public DiscDirNode(string name, int mode, long size = 0) : base(name, mode, size)
        {
        }

        public override VfsNodeType Type => VfsNodeType.Directory;
    }

    public class DiscFileNode : VfsNode, IVfsFile
    {
        private readonly DiscFileInfo discNode;
        private readonly object _gate;

        public DiscFileNode(DiscFileInfo f, int mode, object gate)
            : base(f.Name, mode, f.Length)
        {
            this.discNode = f;
            this._gate = gate;
        }

        public override VfsNodeType Type => VfsNodeType.File;

        public byte[]? Read(long offset, long count)
        {
            // DiscUtils' SquashFileSystemReader shares a single underlying stream,
            // so concurrent opens/reads must be serialized.
            lock (_gate)
            {
                using var stream = discNode.OpenRead();
                if (offset > stream.Length) return null;
                stream.Seek(offset, System.IO.SeekOrigin.Begin);

                var available = stream.Length - offset;
                var length = Math.Min(available, count);

                var buf = new byte[length];
                try
                {
                    stream.ReadExactly(buf);
                }
                catch (Exception)
                {
                    // signal I/O error
                    return null;
                }
                return buf;
            }
        }

        public int Rename(string newName) => -(int)PosixError.ENOTSUP;
        public int Truncate(long length) => -(int)PosixError.ENOTSUP;
        public int Write(byte[] buffer, long offset, long count) => -(int)PosixError.ENOTSUP;
    }

    /// <summary>
    /// FUSE VFS that mounts an EPK1/EPK2/EPK2Beta/EPK3 image directly from the backing
    /// file, decrypting on demand in blocks. Nested filesystems (e.g. squashfs)
    /// are exposed as subdirectories.
    /// </summary>
    public class EpkVfs : YafexVfs
    {
        private readonly MFile _input;
        private readonly List<object> _openFilesystems = new List<object>();
        private int _pakCount;

        public int PakCount => _pakCount;

        public EpkVfs(MFile input, KeysRepository keysRepo)
        {
            _input = input;

            var blockSource = new MappedBlockSource(input.Data);

            var epk1 = new Epk1Detector().Detect(input);
            if (epk1 is Epk1DetectionResult epk1r && epk1r.Succeded() && epk1r.EpkType != Epk1Type.New)
            {
                BuildEpk1(epk1r.EpkType, blockSource);
                return;
            }

            var epk2 = new Epk2Detector(keysRepo).Detect(input);
            if (epk2 is Epk2DetectionResult<EPK_V2_HEADER> epk2r && epk2r.Succeded() && epk2r.Context != null)
            {
                BuildEpk2(epk2r.Context, blockSource);
                return;
            }

            var beta = new Epk2BetaDetector(keysRepo).Detect(input);
            if (beta is Epk2DetectionResult<EPK_V2_BETA_HEADER> betar && betar.Succeded() && betar.Context is Epk2BetaContext betaCtx)
            {
                BuildEpk2Beta(betaCtx, blockSource);
                return;
            }

            var epk3 = new Epk3NewDetector(keysRepo).Detect(input);
            if (epk3 is Epk3NewDetectionResult epk3r && epk3r.Succeded() && epk3r.Context != null)
            {
                BuildEpk3New(epk3r.Context, blockSource);
                return;
            }

            throw new NotSupportedException("Input is not a recognized EPK1/EPK2/EPK2Beta/EPK3 firmware image");
        }

        public static bool TryCreate(MFile input, KeysRepository keysRepo, out EpkVfs vfs)
        {
            try
            {
                vfs = new EpkVfs(input, keysRepo);
                return true;
            }
            catch (NotSupportedException)
            {
                vfs = null!;
                return false;
            }
        }

        private void BuildEpk1(Epk1Type epkType, MappedBlockSource source)
        {
            // walker yields header offsets; payload follows the PakHeader,
            // mirroring Epk1Extractor
            int headerSize = Marshal.SizeOf<PakHeader>();
            var images = Epk1Walker.EnumeratePaks(source.Data, epkType)
                .Select(entry =>
                {
                    var image = new PakImage { Name = $"{entry.Name}.pak" };
                    image.Segments.Add(new PakSegment
                    {
                        PhysicalDataOffset = entry.Offset,
                        EncryptedSize = headerSize + entry.Size,
                        ContentOffset = headerSize,
                        LogicalSize = entry.Size,
                        WasDecrypted = false,
                    });
                    return (image, (Func<string>?)(() => $"package: {entry.Name}\noffset: 0x{entry.Offset:X}\nsize: {entry.Size}"));
                });
            AddImages(images, source);
        }

        private void BuildEpk2(EpkContext<EPK_V2_HEADER> ctx, MappedBlockSource source)
        {
            var hdr = ctx.Header;
            var images = Epk2SegmentWalker.BuildPakImages(
                source.Data.AsReadonlySpan(),
                ctx,
                hdr
            );
            AddImages(images.Select(pak => (
                pak.Image,
                (Func<string>?)(() => DescribePak(pak.Header, pak.Image))
            )), source);
        }

        private void BuildEpk2Beta(Epk2BetaContext ctx, MappedBlockSource source)
        {
            var hdr = ctx.Header;
            var images = Epk2SegmentWalker.BuildPakImages(
                source.Data.AsReadonlySpan(),
                ctx,
                hdr
            );
            AddImages(images.Select(pak => (
                pak.Image,
                (Func<string>?)(() => DescribePak(pak.Header, pak.Image))
            )), source);
        }

        private void BuildEpk3New(Epk3NewContext ctx, MappedBlockSource source)
        {
            var data = source.Data;
            var epk = data.ReadStruct<EPK_V3_NEW_STRUCTURE>();
            var image = new Epk3NewImage(ctx, data.AsReadonlySpan(), epk);
            AddImages(image.Paks.Select(pak => (
                pak.Image,
                (Func<string>?)(() => $"package: {pak.Name}\nsegments: {pak.Image.Segments.Count}\nsize: {pak.Image.Length}\nraw: {pak.Image.RawLength}")
            )), source);
        }

        private static string DescribePak(PAK_V2_HEADER header, PakImage image)
        {
            return $"image: {header.ImageType}\nversion: {header.SwVersion}\nplatform: {header.ModelName}\n" +
                $"magic: {header.PakMagic}\nsegments: {image.Segments.Count}\nsize: {image.Length}\nraw: {image.RawLength}";
        }

        private void AddImages(IEnumerable<(PakImage Image, Func<string>? Describe)> images, IBlockSource source)
        {
            var rootDir = new YafexDirectory(_input.Name ?? "epk", Helpers.OctalLiteral(0755));
            Root.AddNode(rootDir);

            foreach (var (image, describe) in images)
            {
                ++_pakCount;
                AddPak(rootDir, image, source, describe);
            }
        }

        private void AddPak(IVfsNode parent, PakImage image, IBlockSource source, Func<string>? describe)
        {
            image.Source = source;
            VfsBuilder.AddImage(
                parent, image,
                VfsImageViews.All,
                _openFilesystems,
                raw: image.AsRaw(),
                describe: describe);
        }
    }
}

