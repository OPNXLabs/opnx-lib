using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPNX.Lib.Common.Serialization
{
    public static class JsonDefaults
    {
        public static readonly JsonSerializerOptions SerializerOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static readonly JsonWriterOptions WriterOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false
        };
    }
}
