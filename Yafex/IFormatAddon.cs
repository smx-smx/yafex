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
using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex
{
    public interface IFormatAddon
    {
        FileFormat FileFormat { get; }
        IFormatDetector CreateDetector(IDictionary<string, string> args);
        /// <summary>
        /// Mount node for an extracted artifact (materializing mount path).
        /// Default: read-only file over the artifact's data.
        /// </summary>
        IVfsNode CreateVfsNode(IDataSource ds) => new YafexFile(ds, Helpers.OctalLiteral(0444));

        IFormatExtractor CreateExtractor(DetectionResult result);

        /// <summary>
        /// Lazy virtual filesystem for mount/cat without extraction, or null to fall
        /// back to the materializing path. Default: unsupported.
        /// </summary>
        Yafex.Fuse.YafexVfs? CreateVfs(IDataSource source, DetectionResult result) => null;

        /// <summary>
        /// Documents `-o &lt;format&gt;:&lt;opt&gt;=&lt;val&gt;` options shown by --help.
        /// Key = option name, value = one-line description.
        /// </summary>
        IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>();
    }

    /// <summary>
    /// Base for format addons: checks the detection result's type (and
    /// <see cref="IsUsable"/>) once, so a format implements typed hooks only.
    /// A minimal format overrides <see cref="FileFormat"/>,
    /// <see cref="CreateDetector"/> and <see cref="CreateExtractor(TResult)"/>.
    /// </summary>
    public abstract class FormatAddon<TResult> : IFormatAddon where TResult : DetectionResult
    {
        public abstract FileFormat FileFormat { get; }

        public abstract IFormatDetector CreateDetector(IDictionary<string, string> args);

        public IFormatExtractor CreateExtractor(DetectionResult result)
        {
            if (result is not TResult typed || !IsUsable(typed))
            {
                throw new ArgumentException($"expected a usable {typeof(TResult).Name}", nameof(result));
            }
            return CreateExtractor(typed);
        }

        public virtual YafexVfs? CreateVfs(IDataSource source, DetectionResult result) => null;

        public virtual IVfsNode CreateVfsNode(IDataSource ds) => new YafexFile(ds, Helpers.OctalLiteral(0444));

        public virtual IDictionary<string, string> DescribeOptions() => new Dictionary<string, string>();

        /// <summary>Payload check beyond the type (e.g. the image was buffered). Default: always.</summary>
        protected virtual bool IsUsable(TResult result) => true;

        protected abstract IFormatExtractor CreateExtractor(TResult result);
    }
}
