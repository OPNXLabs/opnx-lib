namespace OPNX.Lib.Media.FFMpeg.RawFrames.Video
{
    public class RawH264IFrame(long timestamp, ReadOnlyMemory<byte> frameData, H264ParameterSets parameterSets, byte[]? rentedBuffer = null, byte[]? rentedParammeterSetBuffer = null) 
        : RawH264Frame(timestamp, frameData, rentedBuffer, rentedParammeterSetBuffer)
    {
        #region Fields
        private readonly H264ParameterSets _parameterSets = parameterSets;
        #endregion        

        #region Properties
        public override bool IsIFrame => true;

        public override ReadOnlyMemory<byte> ParameterSets => _parameterSets.Combined;
        public H264ParameterSets ParameterSetsInfo => _parameterSets;
        public bool HasValidParameterSets => _parameterSets.IsValid;
        #endregion
    }
}
