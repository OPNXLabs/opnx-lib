using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Models;

namespace OPNX.Lib.Onvif.Policies
{
    public sealed class DefaultOnvifDevicePolicy : IOnvifDevicePolicy
    {
        public static DefaultOnvifDevicePolicy Instance { get; } = new();

        private DefaultOnvifDevicePolicy()
        {
        }

        public Uri NormalizeServiceUri(Uri deviceServiceUri, Uri advertisedServiceUri)
        {
            ArgumentNullException.ThrowIfNull(deviceServiceUri);
            ArgumentNullException.ThrowIfNull(advertisedServiceUri);

            if (!advertisedServiceUri.IsLoopback)
                return advertisedServiceUri;

            return new UriBuilder(advertisedServiceUri)
            {
                Host = deviceServiceUri.Host
            }.Uri;
        }

        public TimeSpan GetRequestTimeout(OnvifOperation operation, TimeSpan defaultTimeout)
        {
            return operation == OnvifOperation.PullMessages
                ? TimeSpan.FromSeconds(65)
                : defaultTimeout;
        }

        public bool ShouldRetry(OnvifOperation operation, Exception exception, int attempt)
        {
            if (attempt != 0 || operation == OnvifOperation.GetSystemDateAndTime)
                return false;

            return exception is HttpRequestException or TaskCanceledException;
        }
    }
}
