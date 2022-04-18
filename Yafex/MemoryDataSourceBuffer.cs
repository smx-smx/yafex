using Smx.SharpIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smx.Yafex
{
    public class MemoryDataSourceBuffer : IDisposable
    {
        private readonly MemoryStream mem;

        private readonly string name;
        private readonly DataSourceType flags;

        public MemoryDataSourceBuffer(string name, DataSourceType flags)
        {
            mem = new MemoryStream();
            this.name = name;
            this.flags = flags;
        }

        public void Write(byte[] data)
        {
            mem.Write(data);
        }
        public void Write(ReadOnlySpan<byte> data)
        {
            mem.Write(data);
        }

        public MemoryDataSource ToDataSource()
        {
            return new MemoryDataSource(mem.ToArray())
            {
                Name = name,
                Flags = flags
            };
        }

        public void Dispose()
        {
            mem.Dispose();
        }
    }
}
