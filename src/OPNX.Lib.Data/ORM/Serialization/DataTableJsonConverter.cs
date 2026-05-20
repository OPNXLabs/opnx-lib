using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPNX.Lib.Data.ORM.Serialization
{
    public class DataTableJsonConverter : JsonConverter<DataTable>
    {
        public override DataTable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var json = JsonDocument.ParseValue(ref reader).RootElement;
            var table = new DataTable();

            if (json.ValueKind != JsonValueKind.Array)
                return table;

            foreach (var element in json.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                var row = table.NewRow();

                foreach (var prop in element.EnumerateObject())
                {
                    if (!table.Columns.Contains(prop.Name))
                    {
                        // JsonElement는 타입 추론이 필요함
                        object? sampleValue = GetJsonElementValue(prop.Value);
                        table.Columns.Add(prop.Name, sampleValue?.GetType() ?? typeof(string));
                    }

                    row[prop.Name] = GetJsonElementValue(prop.Value) ?? DBNull.Value;
                }

                table.Rows.Add(row);
            }

            return table;
        }

        public override void Write(Utf8JsonWriter writer, DataTable value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (DataRow row in value.Rows)
            {
                writer.WriteStartObject();
                foreach (DataColumn col in value.Columns)
                {
                    writer.WritePropertyName(col.ColumnName);
                    JsonSerializer.Serialize(writer, row[col], options);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static object? GetJsonElementValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:

                    if (element.TryGetInt32(out var intVal))
                        return intVal;

                    if (element.TryGetInt64(out var longVal))
                        return longVal;

                    if (element.TryGetDouble(out var doubleVal))
                        return doubleVal;

                    return element.GetRawText();
                case JsonValueKind.String:
                    if (element.TryGetDateTime(out var dt))
                        return dt;
                    return element.GetString();

                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.GetRawText();
            }
        }

        private static object TryParseDateTimeOrString(string value)
        {
            return DateTime.TryParse(value, out var dt) ? dt : value;
        }
    }
}
