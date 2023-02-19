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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.Fuse
{
    public abstract class VfsNode : IVfsNode
    {
        private string _name = "";
        private int _mode;
        private long _size;
        private IDictionary<string, VfsNodeLink> _tree = new Dictionary<string, VfsNodeLink>();

        public string Name { get => _name; set => _name = value; }

        public abstract VfsNodeType Type { get; }

        public int Mode {
            get
            {
                var mode = _mode;
                switch (this.Type)
                {
                    case VfsNodeType.Directory:
                        mode |= (int)StatMode.S_IFDIR;
                        break;
                    default:
                        mode |= (int)StatMode.S_IFREG;
                        break;
                }

                return mode;
            }
            set => _mode = value;
        }

        public long Size { get => _size; set => _size = value; }


        public IDictionary<string, VfsNodeLink> Tree => _tree;

        public VfsNode(string name, int mode, long size)
        {
            _name = name;
            _mode = mode;
            _size = size;
        }



        // symlink, TODO
        public int LinkTo(string linkName)
        {
            throw new NotImplementedException();
        }

        public void AddNode(IVfsNode node)
        {
            Tree[node.Name] = new VfsNodeLink()
            {
                Parent = this,
                Node = node
            };
        }

        public void RemoveNode(IVfsNode node)
        {
            Tree.Remove(node.Name);
        }
    }
}
