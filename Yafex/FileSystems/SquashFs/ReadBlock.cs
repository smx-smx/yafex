using Yafex.FileSystems.Base.Streams;

namespace Yafex.FileSystems.SquashFs
{
    internal delegate Block ReadBlock(long pos, int diskLen);
}