using Smx.SharpIO;
using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Smx.Yafex.FileFormats.Xex
{
    public class XexExtractor : IFormatExtractor
    {
        private static byte[] xe_xex2_retail_key = {
            0x20, 0xB1, 0x85, 0xA5, 0x9D, 0x28, 0xFD, 0xC3,
            0x40, 0x58, 0x3F, 0xBB, 0x08, 0x96, 0xBF, 0x91};

        private static byte[] xe_xex2_devkit_key = {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};


        private const string kXEX1Signature = "XEX1";
        private const string kXEX2Signature = "XEX2";

        private enum XexFormat
        {
            Xex1,
            Xex2,
        }

        private Memory<byte> mem;

        private xex2_header? header;
        private xex2_security_info? security_info;
        private xex2_opt_file_format_info? opt_file_format_info;
        private byte[]? session_key;

        private Memory<byte> peFile;

        private XexFormat GetXexFormat()
        {
            switch (header.magic.AsString(Encoding.ASCII))
            {
                case kXEX1Signature:
                    return XexFormat.Xex1;
                case kXEX2Signature:
                    return XexFormat.Xex2;
                default:
                    throw new InvalidDataException("Invalid XEX magic");
            }
        }
        
        private Memory<byte>? GetOptHeader<T>(xex2_header_keys key) 
        {
            return GetOptHeader<T>(key, out var _);
        }

        private Memory<byte>? GetOptHeader<T>(xex2_header_keys key, out int header_offset)
        {
            var offset = GetOptHeader(key, out header_offset);
            if (!offset.HasValue) return default;
            return mem.Slice((int)offset.Value);
        }

        private uint? GetOptHeader(xex2_header_keys key)
        {
            return GetOptHeader(key, out var _);
        }

        private uint? GetOptHeader(xex2_header_keys key, out int header_offset)
        {
            header_offset = 0;

            var s = Enumerable.Range(0, (int)header.header_count)
                .Select(i => (i, header.opt_headers[i]))
                .Where(t => t.Item2.key == (uint)key);

            if (!s.Any()) return null;

            var t = s.First();
            var opt_header = t.Item2;

            header_offset = header.SIZEOF + (t.i * opt_header.SIZEOF);

            switch (opt_header.key & 0xFF)
            {
                case 0x00:
                    return opt_header.value;
                case 0x01:
                    // header holds a struct (get pointer to value)
                    var value_offset = header_offset + 4;
                    return (uint)value_offset;
                default:
                    // header holds an offset to a struct
                    return opt_header.offset;
            }
        }

        private uint GetBaseAddress()
        {
            var opt_image_base = GetOptHeader(xex2_header_keys.IMAGE_BASE_ADDRESS)?.Let(it =>
            {
                var view = mem.Slice((int)it);
                return new SpanStream(view, Endianness.BigEndian).ReadUInt32();
            });
            if (opt_image_base.HasValue) return opt_image_base.Value;
            return security_info.load_address;
        }
        
        private bool IsPatch()
        {
            var flags = header.module_flags;
            return flags.HasFlag(xex2_module_flags.MODULE_PATCH)
                || flags.HasFlag(xex2_module_flags.PATCH_DELTA)
                || flags.HasFlag(xex2_module_flags.PATCH_FULL);
        }

        private static byte[] AesDecryptECB(byte[] data, byte[] key)
        {
            Rijndael aes = new RijndaelManaged()
            {
                BlockSize = 128,
                KeySize = 128,
                Mode = CipherMode.ECB,
                Key = key,
                Padding = PaddingMode.None
            };
            return aes.CreateDecryptor().TransformFinalBlock(data, 0, data.Length);
        }

        private void ReadImageUncompressed()
        {
            int exe_length = mem.Length - (int)header.header_size;
            int uncompressed_size = exe_length;

            this.peFile = new Memory<byte>(new byte[exe_length]);
            var out_ptr = peFile;

            var ivec = new byte[16];
            var aes = new RijndaelManaged()
            {
                BlockSize = 128,
                KeySize = 128,
                Mode = CipherMode.CBC,
                IV = ivec,
                Key = session_key,
                Padding = PaddingMode.None
            }.CreateDecryptor();

            var p = (int)header.header_size;


            switch (opt_file_format_info.encryption_type)
            {
                case xex2_encryption_type.NONE:
                    mem.CopyTo(out_ptr);
                    break;
                case xex2_encryption_type.NORMAL:
                    var data = mem.Slice(p).ToArray();
                    for (int i = 0; i < uncompressed_size; i += 16)
                    {
                        aes.TransformBlock(data, i, 16, data, i);
                    }
                    data.CopyTo(out_ptr);
                    break;
                    
            }
            aes.Dispose();
        }

        private int PageSize()
        {
            if (GetBaseAddress() <= 0x90000000) return 64 * 1024;
            return 4 * 1024;
        }

        private void ReadImageBasicCompressed()
        {
            int exe_length = mem.Length - (int)header.header_size;
            int block_count = (int)(opt_file_format_info.info_size - 8) / 8;
            
            var comp_info = opt_file_format_info.basic_compression_info();
            var blocks = comp_info.blocks(block_count);

            var uncompressed_size = blocks.Sum(b => b.data_size + b.zero_size);
            var descrs = security_info.page_descriptors;

            var total_size = descrs.Sum(d => d.section.page_count * PageSize());

            var p = (int)header.header_size;

            var ivec = new byte[16];
            var aes = new RijndaelManaged()
            {
                BlockSize = 128,
                KeySize = 128,
                Mode = CipherMode.CBC,
                IV = ivec,
                Key = session_key,
                Padding = PaddingMode.None
            }.CreateDecryptor();

            this.peFile = new Memory<byte>(new byte[(int)total_size]);
            var out_ptr = peFile;

            foreach (var block in blocks)
            {
                switch (opt_file_format_info.encryption_type)
                {
                    case xex2_encryption_type.NONE:
                        mem.Slice(p, (int)block.data_size).CopyTo(out_ptr);
                        break;
                    case xex2_encryption_type.NORMAL:
                        int data_size = (int)block.data_size;
                        var data = mem.Slice(p, data_size).ToArray();
                        // in place decrypt
                        for(int i=0; i<block.data_size; i += 16)
                        {
                            aes.TransformBlock(data, i, 16, data, i);
                        }
                        data.CopyTo(out_ptr);
                        break;
                }
                p += (int)block.data_size;
                out_ptr = out_ptr.Slice((int)(block.data_size + block.zero_size));
            }

            aes.Dispose();
        }

        private void ReadImageCompressed()
        {
            var exe_length = mem.Length - header.header_size;
            var compress_buffer = new Memory<byte>(new byte[exe_length]);

            var ivec = new byte[16];
            var aes = new RijndaelManaged()
            {
                BlockSize = 128,
                KeySize = 128,
                Mode = CipherMode.CBC,
                IV = ivec,
                Key = session_key,
                Padding = PaddingMode.None
            }.CreateDecryptor();

            var input_buffer = mem.Slice((int)header.header_size);

            switch (opt_file_format_info.encryption_type)
            {
                case xex2_encryption_type.NONE:
                    break;
                case xex2_encryption_type.NORMAL:
                    input_buffer = aes.TransformFinalBlock(
                        mem.Slice((int)header.header_size, (int)exe_length).ToArray(),
                        0, (int)exe_length);
                    break;
            }

            var compression_info = opt_file_format_info.normal_compression_info();
            var cur_block = compression_info.first_block;

            int in_offset = 0;
            int out_offset = 0;
            while(cur_block.block_size > 0)
            {
                var block_data = input_buffer.Slice(in_offset, (int)cur_block.block_size).ToArray();

                var digest = SHA1.HashData(block_data);
                if(!Enumerable.SequenceEqual(digest, cur_block.block_hash))
                {
                    throw new InvalidDataException("block digest mismatch");
                }

                // skip block info
                in_offset += 4;
                in_offset += 20;

                while (true)
                {
                    var chunk_size = (input_buffer.Span[in_offset] << 8) | input_buffer.Span[in_offset + 1];
                    in_offset += 2;
                    if (chunk_size == 0) break;

                    input_buffer.Slice(in_offset, chunk_size)
                        .CopyTo(compress_buffer, out_offset);

                    in_offset += chunk_size;
                    out_offset += chunk_size;
                }

                var next_slice = input_buffer.Slice((int)cur_block.block_size);
                if(next_slice.Length == 0)
                {
                    break;
                }

                var next_block = new xex2_compressed_block_info(next_slice);
                cur_block = next_block;
            }

            var uncompressed_size = ImageSize();
            var out_data = new byte[uncompressed_size];
            this.peFile = out_data;

            var compressed_data = compress_buffer
                // d - compress_buffer
                .Slice(0, out_offset).ToArray();
            
            var window_bits = (int)Math.Log2(compression_info.window_size);

            var decoder = new LzxDecompressionMethod();
            decoder.init(window_bits, 0, compressed_data.Length, uncompressed_size, false);

            using (var in_buf = new MemoryStream(compressed_data))
            using (var out_buf = new MemoryStream(out_data))
            {
                decoder.decompress(in_buf, out_buf, uncompressed_size);
            }
            aes.Dispose();
        }

        private uint ImageSize()
        {
            uint total_size = (uint)security_info.page_descriptors.Sum(
                d => d.section.page_count * PageSize()
            );
            return total_size;
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            this.mem = source.Data;
            header = new xex2_header(mem);
            security_info = header.security_info;

            if (IsPatch())
            {
                // $FIXME: patch
                throw new NotImplementedException("XEX2 Patches not supported yet");
            }

            var exec_info = GetOptHeader<xex2_opt_execution_info>(xex2_header_keys.EXECUTION_INFO)?.Let(it =>
            {
                return new xex2_opt_execution_info(it);
            });

            // $FIXME: xenia logic is to try both keys and see if the decryption is valid
            // we instead use RexDex logic to look at the title_id
            byte[] keyToUse;
            if(header.magic.AsString(Encoding.ASCII) == kXEX1Signature
                || exec_info == null
                || exec_info.title_id == 0
            ){
                keyToUse = xe_xex2_devkit_key;
            } else
            {
                keyToUse = xe_xex2_retail_key;
            }

            session_key = AesDecryptECB(security_info.aes_key, keyToUse);
            {
                var opt_file_format_info = GetOptHeader<xex2_opt_file_format_info>(
                    xex2_header_keys.FILE_FORMAT_INFO,
                    out var opt_file_format_info_offset)?.Let(it =>
                    {
                        return new xex2_opt_file_format_info(it);
                    });
                if(opt_file_format_info == null)
                {
                    throw new InvalidDataException("Missing FILE_FORMAT_INFO");
                }

                xex2_opt_header opt_header = new xex2_opt_header(mem.Slice(opt_file_format_info_offset));
                this.opt_file_format_info = opt_file_format_info;
            }


            switch (opt_file_format_info.compression_type)
            {
                case xex2_compression_type.NONE:
                    ReadImageUncompressed();
                    break;
                case xex2_compression_type.BASIC:
                    ReadImageBasicCompressed();
                    break;
                case xex2_compression_type.NORMAL:
                    ReadImageCompressed();
                    break;
            }

            var outputName = Path.GetFileNameWithoutExtension(source.Directory) + ".exe";
            var exeArtifact = new MemoryDataSource(peFile)
            {
                Name = outputName,
                Flags = DataSourceType.Output
            };
            yield return exeArtifact;
        }
    }
}