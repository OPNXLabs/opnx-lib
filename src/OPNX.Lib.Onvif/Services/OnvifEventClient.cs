using OPNX.Lib.Onvif.Abstractions;
using OPNX.Lib.Onvif.Internal;
using OPNX.Lib.Onvif.Models;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Services
{
    public sealed class OnvifEventClient(IOnvifSoapTransport transport, Uri endpoint) : OnvifServiceClient(transport, endpoint), IOnvifEventClient
    {
        public async Task<IOnvifEventSubscription> CreatePullPointSubscriptionAsync(TimeSpan initialTerminationTime, CancellationToken cancellationToken = default)
        {
            XNamespace events = OnvifNamespaces.Events;
            var document = await SendAsync($"{OnvifNamespaces.Events}/EventPortType/CreatePullPointSubscriptionRequest",
                new XElement(events + "CreatePullPointSubscription", new XElement(events + "InitialTerminationTime", OnvifXml.Duration(initialTerminationTime))), cancellationToken).ConfigureAwait(false);
            var address = document.Descendant("SubscriptionReference")?.Descendant("Address")?.Value;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var subscriptionUri))
                throw new InvalidDataException("The ONVIF device did not return a valid PullPoint subscription address.");
            return new OnvifEventSubscription(Transport, subscriptionUri, document.Descendant("TerminationTime").DateTimeValue());
        }
    }

    internal sealed class OnvifEventSubscription(IOnvifSoapTransport transport, Uri subscriptionUri, DateTimeOffset? terminationTime) : IOnvifEventSubscription
    {
        private bool _unsubscribed;

        public Uri SubscriptionUri { get; } = subscriptionUri;
        public DateTimeOffset? TerminationTime { get; private set; } = terminationTime;

        public async Task<IReadOnlyList<OnvifNotification>> PullMessagesAsync(TimeSpan timeout, int messageLimit = 100, CancellationToken cancellationToken = default)
        {
            XNamespace events = OnvifNamespaces.Events;
            var document = await transport.SendAsync(SubscriptionUri, $"{OnvifNamespaces.Events}/PullPointSubscription/PullMessagesRequest",
                new XElement(events + "PullMessages", new XElement(events + "Timeout", OnvifXml.Duration(timeout)), new XElement(events + "MessageLimit", messageLimit)), cancellationToken).ConfigureAwait(false);
            return document.DescendantsNamed("NotificationMessage").Select(ParseNotification).ToList();
        }

        public async Task RenewAsync(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            XNamespace notification = OnvifNamespaces.WsNotification;
            var document = await transport.SendAsync(SubscriptionUri, $"{OnvifNamespaces.WsNotification}/SubscriptionManager/RenewRequest",
                new XElement(notification + "Renew", new XElement(notification + "TerminationTime", OnvifXml.Duration(duration))), cancellationToken).ConfigureAwait(false);
            TerminationTime = document.Descendant("TerminationTime").DateTimeValue();
        }

        public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (_unsubscribed)
                return;
            XNamespace notification = OnvifNamespaces.WsNotification;
            _ = await transport.SendAsync(SubscriptionUri, $"{OnvifNamespaces.WsNotification}/SubscriptionManager/UnsubscribeRequest",
                new XElement(notification + "Unsubscribe"), cancellationToken).ConfigureAwait(false);
            _unsubscribed = true;
        }

        private static OnvifNotification ParseNotification(XElement message)
        {
            var sources = ParseSimpleItems(message.Descendant("Source"));
            var data = ParseSimpleItems(message.Descendant("Data"));
            return new(
                message.Descendant("Topic")?.Value,
                message.Descendant("Message")?.AttributeValue("UtcTime") is { } time && DateTimeOffset.TryParse(time, out var timestamp) ? timestamp : null,
                sources,
                data,
                new XElement(message));
        }

        private static IReadOnlyDictionary<string, string> ParseSimpleItems(XElement? parent)
        {
            if (parent == null)
                return new Dictionary<string, string>();

            return parent.DescendantsNamed("SimpleItem")
                .Select(item => new { Name = item.AttributeValue("Name"), Value = item.AttributeValue("Value") })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.Ordinal);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_unsubscribed)
            {
                try
                {
                    await UnsubscribeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
    }
}
