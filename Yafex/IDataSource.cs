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
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Yafex.Metadata;

namespace Yafex
{
	[Flags]
    public enum DataSourceFlags {
		Input = 1 << 0,
		Output = 1 << 1,
		Temporary = 1 << 2,
		ProcessFurther = 1 << 3,
		Stream = 1 << 4
	}


    public interface IDataSource
	{
		string? Name { get; set; }
		string? Directory { get; set; }

		Memory<byte> Data { get; }

		DataSourceFlags Flags { get; set; }

		IEnumerable<T> GetMetadata<T>() where T : IArtifactMetadata;
        void AddMetadata<T>(T metadata) where T : IArtifactMetadata;
    }
}
