using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPNX.Lib.Data.ORM.Serialization
{
    public class EntityJsonConverter<T>(IEnumerable<string> excludedProperties) : JsonConverter<T>
    {
        private readonly HashSet<string> _excludedProperties = [.. excludedProperties];

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            //var tempOptions = new JsonSerializerOptions(options);
            //tempOptions.Converters.Remove(this);
            options.Converters.Remove(this);

            var jsonElement = JsonSerializer.SerializeToElement(value, options);

            writer.WriteStartObject();

            foreach (var property in jsonElement.EnumerateObject())
            {
                if (!_excludedProperties.Contains(property.Name)) // 제외 리스트 확인
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }
    }
}
