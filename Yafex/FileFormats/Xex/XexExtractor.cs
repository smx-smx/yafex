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
using Smx.SharpIO;
using Yafex.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Smx.SharpIO.Extensions;

namespace Yafex.FileFormats.Xex
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

        private Memory<byte> peMem;

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
            if (!offset.HasValue) return null;
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

            this.peMem = new Memory<byte>(new byte[exe_length]);
            var out_ptr = peMem;

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

            this.peMem = new Memory<byte>(new byte[(int)total_size]);
            var out_ptr = peMem;

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

        private Memory<byte> BuildIAT(IMAGE_NT_HEADERS nthdr)
        {
            var pe_impdir = nthdr.OptionalHeader.DataDirectory[IMAGE_OPTIONAL_HEADER.IMAGE_DIRECTORY_ENTRY_IMPORT];
            var pe_iat = nthdr.OptionalHeader.DataDirectory[IMAGE_OPTIONAL_HEADER.IMAGE_DIRECTORY_ENTRY_IAT];

            Memory<byte> xex_implibs_data;
            {
                var maybe_xex_implibs_data = GetOptHeader<xex2_opt_import_libraries>(xex2_header_keys.IMPORT_LIBRARIES);
                if (maybe_xex_implibs_data == null)
                {
                    return null;
                }
                xex_implibs_data = maybe_xex_implibs_data.Value;
            }

            var xex_implibs = new xex2_opt_import_libraries(xex_implibs_data);

            // import descriptors (including trailing NULL descriptor)
            var size_descr = IMAGE_IMPORT_DESCRIPTOR.SIZEOF * (xex_implibs.import_libraries.Length + 1);
            var size_strings = xex_implibs.string_table.size;
            // import entries (including trailing NULL entry for each lib)
            var size_entries = (IMAGE_THUNK_DATA32.SIZEOF * xex_implibs.import_libraries.Sum(lib => lib.import_table.Length + 1));

            var size_iat = size_descr + size_strings + size_entries;
            var iat_mem = new Memory<byte>(new byte[size_iat]);
            var iat = new SpanStream(iat_mem);

            /**
             * layout:
             * - IMAGE_IMPORT_DESCRIPTOR[] descriptors
             * - string[] names
             * - IMAGE_THUNK_DATA32[] entries
             **/

            var rva_descr = pe_impdir.VirtualAddress;
            var rva_strings = rva_descr + size_descr;
            var rva_entries = rva_strings + size_strings;

            var off_descr = 0;
            var off_strings = 0;
            var off_entries = 0;


            foreach(var lib in xex_implibs.import_libraries)
            {
                var name = xex_implibs.string_table.table[lib.name_index];
                var id = new IMAGE_IMPORT_DESCRIPTOR()
                {
                    Name = (uint)(rva_strings + off_strings),
                    FirstThunk = (uint)(rva_entries + off_entries)
                };

                // write desriptor
                iat.PerformAt(off_descr, () => id.Write(iat));
                off_descr += IMAGE_IMPORT_DESCRIPTOR.SIZEOF;

                // write name
                iat.PerformAt(off_strings, () => iat.WriteCString(name));
                off_strings += name.Length + 1;

                // write thunks
                iat.PerformAt(off_entries, () =>
                {
                    var thunks = lib.import_table.Select(imp => new IMAGE_THUNK_DATA32()
                    {
                        value = imp
                    });
                    foreach(var t in thunks)
                    {
                        t.Write(iat);
                    }
                });
                off_entries += IMAGE_THUNK_DATA32.SIZEOF * (lib.import_table.Length + 1);
            }

            return iat_mem;
        }

        private (IMAGE_NT_HEADERS, IMAGE_SECTION_HEADER[]) ReadPEHeaders()
        {
            var doshdr = new IMAGE_DOS_HEADER(peMem);
            if (doshdr.e_magic != IMAGE_DOS_HEADER.IMAGE_DOS_SIGNATURE)
            {
                throw new InvalidDataException("DOS signature mismatch");
            }

            var nthdr_mem = peMem.Slice(doshdr.e_lfanew);

            var nthdr = new IMAGE_NT_HEADERS(nthdr_mem);
            if (nthdr.Signature != IMAGE_NT_HEADERS.IMAGE_NT_SIGNATURE)
            {
                throw new InvalidDataException("NT signature mismatch");
            }

            var filehdr = nthdr.FileHeader;
            if(filehdr.Machine != IMAGE_FILE_HEADER.IMAGE_FILE_MACHINE_POWERPCBE
                || (filehdr.Characteristics & IMAGE_FILE_HEADER.IMAGE_FILE_32BIT_MACHINE) != IMAGE_FILE_HEADER.IMAGE_FILE_32BIT_MACHINE)
            {
                throw new InvalidDataException("Unexpected PE Machine/Characteristics");
            }

            if(filehdr.SizeOfOptionalHeader != IMAGE_FILE_HEADER.IMAGE_SIZEOF_NT_OPTIONAL_HEADER)
            {
                throw new InvalidDataException("Unexpected SizeOfOptionalHeader");
            }

            var opthdr = nthdr.OptionalHeader;
            if(opthdr.Magic != IMAGE_OPTIONAL_HEADER.IMAGE_NT_OPTIONAL_HDR32_MAGIC)
            {
                throw new InvalidDataException("Optional Header signature mismatch");
            }

            if(opthdr.Subsystem != IMAGE_OPTIONAL_HEADER.IMAGE_SUBSYSTEM_XBOX)
            {
                throw new InvalidDataException("Unexpected Subsystem");
            }

            var section_headers = new SpanStream(nthdr_mem.Slice(nthdr.SIZEOF));
            var sections = Enumerable.Range(0, filehdr.NumberOfSections)
                .Select(_ => new IMAGE_SECTION_HEADER(section_headers))
                .ToArray();

            return (nthdr, sections);
        }

        private Memory<byte> BuildFinalPE(IMAGE_SECTION_HEADER[] sections, Memory<byte>? iat)
        {
            var lastSection = sections.Aggregate((a, b) =>
                a.PointerToRawData > b.PointerToRawData ? a : b);

            var fileSize = lastSection.PointerToRawData + lastSection.SizeOfRawData;

            var iat_segment = sections.Where(s => s.Name == ".idata").First();
            var iat_file_offset = iat_segment.PointerToRawData;

            var peFile = new Memory<byte>(new byte[fileSize]);

            // copy all headers
            var minSection = (int)sections.Min(s => s.VirtualAddress);
            peMem.Slice(0, minSection)
                .CopyTo(peFile);

            foreach(var s in sections)
            {
                var source = (int)s.VirtualAddress;
                var target = peFile.Slice((int)s.PointerToRawData);
                if (source >= peMem.Length) continue;
                var sectionData = peMem.Slice(source, (int)s.VirtualSize);
                sectionData.CopyTo(target);
            }

            // copy IAT
            if (iat != null)
            {
                
                var iat_data = peFile.Slice((int)iat_file_offset);
                //iat.Value.CopyTo(iat_data);
            }

            return peFile;
        }

        private Memory<byte> RebuildPEFile()
        {
            var (nthdr, sections) = ReadPEHeaders();
            var iat = BuildIAT(nthdr);
            var peFile = BuildFinalPE(sections, iat);
            return peFile;
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
            this.peMem = out_data;

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

            var peFile = RebuildPEFile();

            var outputName = Path.GetFileNameWithoutExtension(source.Directory) + ".exe";
            var exeArtifact = new MemoryDataSource(peFile)
            {
                Name = outputName,
                Flags = DataSourceFlags.Output
            };
            yield return exeArtifact;
        }
    }
}
