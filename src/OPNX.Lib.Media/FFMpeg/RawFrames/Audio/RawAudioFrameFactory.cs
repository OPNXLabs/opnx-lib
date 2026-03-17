namespace OPNX.Lib.Media.FFMpeg.RawFrames.Audio
{
    public class RawAudioFrameFactory : IDisposable
    {
        #region Constructors
        public RawAudioFrameFactory()
        {
        }
        #endregion

        #region Private / Protected Methods
        private ReadOnlyMemory<byte> CombineSegments(IEnumerable<ReadOnlyMemory<byte>> segments)
        {
            int totalLength = segments.Sum(s => s.Length);
            byte[] buffer = new byte[totalLength];

            int offset = 0;
            foreach (var segment in segments)
            {
                segment.Span.CopyTo(buffer.AsSpan(offset));
                offset += segment.Length;
            }

            return new ReadOnlyMemory<byte>(buffer);
        }

        private static ReadOnlyMemory<byte> GetAacConfig(IEnumerable<ReadOnlyMemory<byte>> data)
        {
            // SDP나 초기 extradata에서 추출해야 하는 경우 있음
            // 일단 placeholder
            return default;
        }
        #endregion


        #region Public Methods
        public RawAudioFrame CreateAudioFrame(string codec, int sampleRate, int channels, int bitsPerSample, long timeStamp, IEnumerable<ReadOnlyMemory<byte>> data)
        {
            if (string.IsNullOrWhiteSpace(codec))
                throw new ArgumentException("Codec cannot be null or empty.", nameof(codec));
            if (data == null || !data.Any())
                throw new ArgumentException("Audio data cannot be null or empty.", nameof(data));

            var frameBytes = CombineSegments(data);

            return codec.ToUpperInvariant() switch
            {
                "AAC" => new RawAACFrame(timeStamp, frameBytes, GetAacConfig(data)),
                "PCMA" => new RawG711AFrame(timeStamp, frameBytes),
                "PCMU" => new RawG711UFrame(timeStamp, frameBytes),
                "G726" => new RawG726Frame(timeStamp, frameBytes, bitsPerSample),
                "PCM" => new RawPCMFrame(timeStamp, frameBytes, sampleRate, bitsPerSample, channels),
                _ => throw new NotSupportedException($"Unsupported audio codec: {codec}"),
            };
        }
        public void Dispose()
        {

        }
        #endregion
    }
}
