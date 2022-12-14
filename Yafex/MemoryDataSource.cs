using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex
{
    public class MemoryDataSource : IDataSource
    {
        private Memory<byte> data;

        public MemoryDataSource(Memory<byte> data) {
            this.data = data;
        }

        public Memory<byte> Data => data;

        private DataSourceType _flags;
        public DataSourceType Flags {
            get => _flags;
            set => _flags = value;
        }

        private string? _name;
        public string? Name
        {
            get => _name;
            set => _name = value;
        }

        private string? _path;
        public string? Directory
        {
            get => _path;
            set => _path = value;
        }
    }
}
