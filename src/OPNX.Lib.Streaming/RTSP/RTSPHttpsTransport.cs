using System.Net;
using System.Net.Security;

namespace OPNX.Lib.Streaming.RTSP
{
    public class RTSPHttpsTransport : RTSPHttpTransport
    {
        private readonly RemoteCertificateValidationCallback _userCertificateValidationCallback;

        public RTSPHttpsTransport(
            Uri uri,
            NetworkCredential credentials,
            RemoteCertificateValidationCallback userCertificateValidationCallback = null
        ) : base(uri, credentials)
        {
            _userCertificateValidationCallback = userCertificateValidationCallback;
        }

        public override Stream GetStream()
        {
            // 기본 스트림 가져오기
            Stream baseStream = base.GetStream();

            // SSL 스트림 생성
            var sslStream = new SslStream(
                baseStream,
                leaveInnerStreamOpen: true,
                userCertificateValidationCallback: _userCertificateValidationCallback
            );

            // 클라이언트 인증
            sslStream.AuthenticateAsClient(Uri.Host);

            return sslStream;
        }
    }

}
