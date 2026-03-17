using FFmpeg.AutoGen;
using System.Drawing;

namespace OPNX.Lib.Media.FFMpeg.Generic
{
    public class TransformParameters(RectangleF regionOfInterest, Size targetFrameSize, ScalingPolicy scalePolicy,
            AVPixelFormat targetFormat, ScalingQuality scaleQuality)
    {
        public RectangleF RegionOfInterest { get; } = regionOfInterest;

        public Size TargetFrameSize { get; } = targetFrameSize;

        public ScalingPolicy ScalePolicy { get; } = scalePolicy;

        public AVPixelFormat TargetFormat { get; } = targetFormat;

        public ScalingQuality ScaleQuality { get; } = scaleQuality;

        protected bool Equals(TransformParameters other)
        {
            return RegionOfInterest.Equals(other.RegionOfInterest) &&
                   TargetFrameSize.Equals(other.TargetFrameSize) &&
                   TargetFormat == other.TargetFormat && ScaleQuality == other.ScaleQuality;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((TransformParameters)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return HashCode.Combine(
                    RegionOfInterest,
                    TargetFrameSize,
                    TargetFormat,
                    ScaleQuality);

                //var hashCode = RegionOfInterest.GetHashCode();
                //hashCode = (hashCode * 397) ^ TargetFrameSize.GetHashCode();
                //hashCode = (hashCode * 397) ^ (int)TargetFormat;
                //hashCode = (hashCode * 397) ^ (int)ScaleQuality;
                //return hashCode;
            }
        }
    }
}
