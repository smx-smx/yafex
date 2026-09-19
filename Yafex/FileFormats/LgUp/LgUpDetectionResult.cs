namespace Yafex.FileFormats.LgUp;

public record LgUpDetectionResult(int Confidence) : DetectionResult(Confidence)
{
    public LgUpDetectionResult(DetectionResult original) : this(original.Confidence)
    {
    }
}