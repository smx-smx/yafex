namespace Yafex.FileSystems.Ufs
{
    // One parsed struct-direct entry (FreeBSD sys/ufs/ufs/dir.h):
    // u32 ino, u16 reclen, u8 type, u8 namlen, name padded to 4 bytes.
    public sealed class UfsDirent
    {
        public uint Ino;
        public byte Dtype;
        public string Name = string.Empty;
    }
}
