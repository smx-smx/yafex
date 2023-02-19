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
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yafex.Support;

namespace Yafex.FileFormats.LxBoot
{
    public class LxSecureBootExtractor : IFormatExtractor
    {
        private readonly LxSecureBootContext ctx;
        public LxSecureBootExtractor(LxSecureBootContext ctx)
        {
            this.ctx = ctx;
        }


        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            var data = source.Data;
            
            var firstOffset = ctx.SpbcDescriptors.First().LoadAddress & 0xFFFF;

            var secondStageOffset = ctx.SpbcSecondStageOffset;
            
            var isSecondStage = secondStageOffset != null;
            var firstEnd = isSecondStage
                ? secondStageOffset
                : (uint)data.Length;

            var firstStage = new MemoryDataSource(
                data.Slice((int)firstOffset, (int)(firstEnd - firstOffset))
            )
            {
                Flags = DataSourceFlags.Output,
                Name = "secureboot_0.bin"
            };

            yield return firstStage;

            if (isSecondStage)
            {
                var header = data.ReadStruct<LxBootHeader>((int)secondStageOffset);
                yield return new MemoryDataSource(
                    data.Slice((int)secondStageOffset, (int)header.Size)
                )
                {
                    Flags = DataSourceFlags.Output,
                    Name = "secureboot_1.bin"
                };
            }
        }
    }
}
