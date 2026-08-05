using System.Net;

namespace OPNX.Lib.Onvif.Transport
{
    public sealed class OnvifSoapFaultException : Exception
    {
        public OnvifSoapFaultException(string message, HttpStatusCode statusCode, string? faultCode = null, string? responseXml = null)
            : base(message)
        {
            StatusCode = statusCode;
            FaultCode = faultCode;
            ResponseXml = responseXml;
        }

        public HttpStatusCode StatusCode { get; }
        public string? FaultCode { get; }
        public string? ResponseXml { get; }
    }
}
