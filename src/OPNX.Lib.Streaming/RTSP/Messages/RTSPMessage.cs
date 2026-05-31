using System.Buffers;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OPNX.Lib.Streaming.RTSP.Messages
{
    public partial class RtspMessage : RtspChunk
    {
        /// <summary>
        /// The regex to validate the Rtsp message.
        /// </summary>       

#if NET8_0_OR_GREATER
        [GeneratedRegex(@"^RTSP/\d\.\d", RegexOptions.None, 10)]
        private static partial Regex RtspVersionTest();
#else
        private static readonly Regex _rtspVersionTest = new(@"^RTSP/\d\.\d", RegexOptions.Compiled, TimeSpan.FromMilliseconds(10));
        private static Regex RtspVersionTest() => _rtspVersionTest;
#endif

        /// <summary>
        /// Create the good type of Rtsp Message from the header.
        /// </summary>
        /// <param name="aRequestLine">A request line.</param>
        /// <returns>An Rtsp message</returns>
        public static RtspMessage GetRtspMessage(string aRequestLine)
        {
            // We can't determine the message 
            if (string.IsNullOrEmpty(aRequestLine))
                return new RtspMessage();

            string[] requestParts = aRequestLine.Split(' ', 3);
            RtspMessage returnValue;
            if (requestParts.Length == 3)
            {
                // A request is : Method SP Request-URI SP RTSP-Version
                // A response is : RTSP-Version SP Status-Code SP Reason-Phrase
                // RTSP-Version = "RTSP" "/" 1*DIGIT "." 1*DIGIT
                if (RtspVersionTest().IsMatch(requestParts[2]))
                {
                    returnValue = RtspRequest.GetRtspRequest(requestParts);
                }
                else if (RtspVersionTest().IsMatch(requestParts[0]))
                {
                    returnValue = new RtspResponse();
                }
                else
                {
                    //  _logger.Warn(CultureInfo.InvariantCulture, "Got a strange message {0}", aRequestLine);
                    returnValue = new RtspMessage();
                }
            }
            else
            {
                // _logger.Warn(CultureInfo.InvariantCulture, "Got a strange message {0}", aRequestLine);
                returnValue = new RtspMessage();
            }
            returnValue.Command = aRequestLine;
            return returnValue;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RtspMessage"/> class.
        /// </summary>
        public RtspMessage()
        {
        }

        protected internal string[] commandArray = [string.Empty];

        /// <summary>
        /// Gets or sets the creation time.
        /// </summary>
        /// <value>The creation time.</value>
        public DateTime Creation { get; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the command of the message (first line).
        /// </summary>
        /// <value>The command.</value>
        public string Command
        {
            get => commandArray is null ? string.Empty : string.Join(" ", commandArray);
            set => commandArray = value?.Split(' ', 3) ?? [string.Empty];
        }

        /// <summary>
        /// Gets the Method of the message (eg OPTIONS, DESCRIBE, SETUP, PLAY).
        /// </summary>
        /// <value>The Method</value>
        [Obsolete("Please use RequestTyped in RtspRequest")]
        public string Method => commandArray is null ? string.Empty : commandArray[0];

        /// <summary>
        /// Gets the headers of the message.
        /// </summary>
        /// <value>The headers.</value>
        public IDictionary<string, string?> Headers { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Adds one header from a string.
        /// </summary>
        /// <param name="line">The string containing header of format Header: Value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="line"/> is null</exception>
        public void AddHeader(string line)
        {
            ArgumentNullException.ThrowIfNull(line);

            //spliter
            string[] elements = line.Split(':', 2);
            if (elements.Length == 2)
            {
                Headers[elements[0].Trim()] = elements[1].TrimStart();
            }
            else
            {
                // _logger.Warn(CultureInfo.InvariantCulture, "Invalid Header received : -{0}-", line);
            }
        }

        /// <summary>
        /// Gets or sets the command Seqquence number.
        /// <remarks>If the header is not define or not a valid number it return 0</remarks>
        /// </summary>
        /// <value>The sequence number.</value>
        public int CSeq
        {
            get
            {
                if (!(Headers.TryGetValue(RtspHeaderNames.CSeq, out string? returnStringValue) &&
                    int.TryParse(returnStringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int returnValue)))
                {
                    returnValue = 0;
                }

                return returnValue;
            }
            set
            {
                Headers[RtspHeaderNames.CSeq] = value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Gets the session ID.
        /// </summary>
        /// <value>The session ID.</value>
        public virtual string? Session
        {
            get => !Headers.TryGetValue(RtspHeaderNames.Session, out string? value) ? null : value;
            set => Headers[RtspHeaderNames.Session] = value!;
        }

        /// <summary>
        /// Initialises the length of the data byte array from content lenth header.
        /// </summary>
        public void InitialiseDataFromContentLength()
        {
            if (!(Headers.ContainsKey("Content-Length")
                && int.TryParse(Headers["Content-Length"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dataLength)))
            {
                dataLength = 0;
            }
            Data = new byte[dataLength];
        }

        /// <summary>
        /// Adjusts the content length header.
        /// </summary>
        public void AdjustContentLength()
        {
            if (!Data.IsEmpty)
            {
                Headers["Content-Length"] = Data.Length.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                Headers.Remove("Content-Length");
            }
        }

        /// <summary>
        /// Sends to the message to a stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is empty</exception>
        /// <exception cref="ArgumentException"><paramref name="stream"/> can't be written.</exception>
        public void SendTo(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (!stream.CanWrite)
                throw new ArgumentException("Stream CanWrite == false, can't send message to it", nameof(stream));

            Contract.EndContractBlock();

            AdjustContentLength();

            StringBuilder outputString = new();
            // output header
            outputString.Append(Command).Append("\r\n");
            foreach (var (key, value) in Headers)
            {
                outputString.Append(key).Append(": ").Append(value).Append("\r\n");
            }
            outputString.Append("\r\n");
            var output = outputString.ToString();
            var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(output.Length));
            var size = Encoding.UTF8.GetBytes(output, 0, output.Length, buffer, 0);
            lock (stream)
            {
                stream.Write(buffer.AsSpan(0, size));

                // Output data
                if (!Data.IsEmpty)
                    stream.Write(Data.Span);
            }
            ArrayPool<byte>.Shared.Return(buffer);
            stream.Flush();
        }

        public async Task SendToAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (!stream.CanWrite)
                throw new ArgumentException("Stream CanWrite == false, can't send message to it", nameof(stream));

            Contract.EndContractBlock();

            AdjustContentLength();

            StringBuilder outputString = new();
            outputString.Append(Command).Append("\r\n");

            foreach (var (key, value) in Headers)
            {
                outputString.Append(key).Append(": ").Append(value).Append("\r\n");
            }

            outputString.Append("\r\n");

            var output = outputString.ToString();
            var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(output.Length));

            try
            {
                var size = Encoding.UTF8.GetBytes(output, 0, output.Length, buffer, 0);

                await stream.WriteAsync(buffer.AsMemory(0, size), cancellationToken).ConfigureAwait(false);

                if (!Data.IsEmpty)
                    await stream.WriteAsync(Data, cancellationToken).ConfigureAwait(false);

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Create a string of the message for debug.
        /// </summary>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.Append("Commande : ").AppendLine(Command);
            foreach (var (key, value) in Headers)
            {
                stringBuilder.Append("Header : ").Append(key).Append(": ").AppendLine(value);
            }

            if (!Data.IsEmpty)
            {
                stringBuilder.Append("Data :-").Append(Encoding.ASCII.GetString(Data.Span)).Append('-').AppendLine();
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Crée un nouvel objet qui est une copie de l'instance en cours.
        /// </summary>
        /// <returns>
        /// Nouvel objet qui est une copie de cette instance.
        /// </returns>
        public override object Clone()
        {
            RtspMessage returnValue = GetRtspMessage(Command);

            foreach (var item in Headers)
            {
                returnValue.Headers.Add(item.Key, item.Value);
            }
            returnValue.Data = Data;
            returnValue.SourcePort = SourcePort;

            return returnValue;
        }
    }
}


