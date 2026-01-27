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
using System.Linq;

namespace Yafex.Fuse
{
    public interface IVfs
    {
        IVfsNode Root { get; }

        public IVfsNode? LookupPath(string path)
        {
            string[] parts;
            if (path == "/")
            {
                parts = new string[] { "" };
            }
            else
            {
                parts = path.Split('/');
            }

            var node = Root;
            var iter = parts.AsEnumerable().GetEnumerator();

            if (!iter.MoveNext()) return null;

            /** first iteration acts on the root node **/
            var part = iter.Current;
            // check against current node name
            if (part == null || part != node.Name) return null;

            /** check children (if any) **/
            while (iter.MoveNext())
            {
                if (!node.Tree.TryGetValue(iter.Current, out var nodeLink)) return null;
                node = nodeLink.Node;
            }

            return node;
        }
    }
}
