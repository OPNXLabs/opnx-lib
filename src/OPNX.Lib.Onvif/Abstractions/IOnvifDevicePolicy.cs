using OPNX.Lib.Onvif.Models;

namespace OPNX.Lib.Onvif.Abstractions
{
    public interface IOnvifDevicePolicy
    {
        Uri NormalizeServiceUri(Uri deviceServiceUri, Uri advertisedServiceUri);
        TimeSpan GetRequestTimeout(OnvifOperation operation, TimeSpan defaultTimeout);
        bool ShouldRetry(OnvifOperation operation, Exception exception, int attempt);
    }
}
