using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Models;

namespace OPNX.Lib.Onvif.Policies
{
    public sealed class CompositeOnvifDevicePolicy : IOnvifDevicePolicy
    {
        private readonly IReadOnlyList<IOnvifDevicePolicy> _policies;

        public CompositeOnvifDevicePolicy(params IOnvifDevicePolicy[] policies)
        {
            ArgumentNullException.ThrowIfNull(policies);

            if (policies.Length == 0)
                throw new ArgumentException("At least one ONVIF device policy must be provided.", nameof(policies));

            if (policies.Any(x => x is null))
                throw new ArgumentException("ONVIF device policies cannot contain null.", nameof(policies));

            _policies = policies.ToArray();
        }

        public Uri NormalizeServiceUri(Uri deviceServiceUri, Uri advertisedServiceUri)
        {
            var normalizedUri = advertisedServiceUri;
            foreach (var policy in _policies)
                normalizedUri = policy.NormalizeServiceUri(deviceServiceUri, normalizedUri);

            return normalizedUri;
        }

        public TimeSpan GetRequestTimeout(OnvifOperation operation, TimeSpan defaultTimeout)
        {
            var timeout = defaultTimeout;
            foreach (var policy in _policies)
                timeout = policy.GetRequestTimeout(operation, timeout);

            return timeout;
        }

        public bool ShouldRetry(OnvifOperation operation, Exception exception, int attempt)
        {
            return _policies.Any(policy => policy.ShouldRetry(operation, exception, attempt));
        }
    }
}
