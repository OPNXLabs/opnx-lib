using System.Globalization;
using System.Xml.Linq;

namespace OPNX.Lib.Onvif.Internal
{
    internal static class OnvifXml
    {
        public static string? AttributeValue(this XElement? element, string localName) =>
            element?.Attributes().FirstOrDefault(x => x.Name.LocalName == localName)?.Value;

        public static XElement? Descendant(this XContainer container, string localName) =>
            container.Descendants().FirstOrDefault(x => x.Name.LocalName == localName);

        public static IEnumerable<XElement> DescendantsNamed(this XContainer container, string localName) =>
            container.Descendants().Where(x => x.Name.LocalName == localName);

        public static float? FloatValue(this XElement? element) =>
            float.TryParse(element?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

        public static DateTimeOffset? DateTimeValue(this XElement? element) =>
            DateTimeOffset.TryParse(element?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;

        public static TimeSpan? DurationValue(this XElement? element)
        {
            if (element is null)
                return null;

            try
            {
                return System.Xml.XmlConvert.ToTimeSpan(element.Value);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static string Number(float value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        public static string Duration(TimeSpan value) => System.Xml.XmlConvert.ToString(value);
    }
}
