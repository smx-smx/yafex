using Yafex.FileFormats.SddlSec;

public class SddlSecContext
{
    //deciphered header
    public SddlSecHeader Header { get; set; }
    public bool SaveSDIT { get; set; }
    public bool SaveInfo { get; set; }
}