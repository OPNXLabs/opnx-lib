using OPNX.Lib.Data.ORM.Datas.Attributes;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPNX.Lib.Data.ORM.Serialization
{
    public class EntityJsonConverter<T> : JsonConverter<T>
    {
        private static readonly PropertyInfo[] SerializableProperties =
        [
            .. typeof(T).GetProperties()
                .Where(property =>
                    property.CanRead &&
                    property.CanWrite &&
                    property.GetIndexParameters().Length == 0 &&
                    Attribute.IsDefined(property, typeof(DataColumnAttribute)) &&
                    !Attribute.IsDefined(property, typeof(ForeignKeyAttribute)) &&
                    !Attribute.IsDefined(property, typeof(JsonIgnoreAttribute)))
        ];

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return default!;

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected JSON object for {typeToConvert.Name}.");

            var entity = Activator.CreateInstance(typeToConvert)
                ?? throw new JsonException($"Could not create an instance of {typeToConvert.Name}.");

            var propertyMap = SerializableProperties.ToDictionary(
                property => options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name,
                property => property,
                options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return (T)entity;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException($"Expected property name while reading {typeToConvert.Name}.");

                var propertyName = reader.GetString();
                reader.Read();

                if (propertyName == null || !propertyMap.TryGetValue(propertyName, out var property))
                {
                    reader.Skip();
                    continue;
                }

                var value = JsonSerializer.Deserialize(ref reader, property.PropertyType, options);
                property.SetValue(entity, value);
            }

            throw new JsonException($"Unexpected end of JSON while reading {typeToConvert.Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();

            foreach (var property in SerializableProperties)
            {
                var propertyValue = property.GetValue(value);
                var propertyName = options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;

                writer.WritePropertyName(propertyName);
                JsonSerializer.Serialize(writer, propertyValue, property.PropertyType, options);
            }

            writer.WriteEndObject();
        }
    }
}
