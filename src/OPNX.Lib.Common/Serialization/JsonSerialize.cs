using OPNX.Lib.Common.Buffers;
using System.Buffers;
using System.Text.Json;

namespace OPNX.Lib.Common.Serialization
{
    public static class JsonSerialize
    {
        public static string ToJsonString<T>(T? obj)
        {
            if (obj is null) return string.Empty;

            try
            {
                return JsonSerializer.Serialize(obj, JsonDefaults.SerializerOptions);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 큐잉/비동기 전송 안전: MemoryPool 기반으로 JSON UTF8 바이트를 반환한다.
        /// 반환된 owner는 호출자(예: Packet)가 반드시 Dispose 해야 한다.
        /// </summary>
        public static (IMemoryOwner<byte> owner, int written) SerializeToUtf8Pooled<T>(T? obj, int initialSize = 4096)
        {
            if (obj is null)
                return (MemoryPool<byte>.Shared.Rent(1), 0);

            var buffer = new PooledBufferWriter(initialSize);
            try
            {
                using var writer = new Utf8JsonWriter(buffer, JsonDefaults.WriterOptions);
                JsonSerializer.Serialize(writer, obj, JsonDefaults.SerializerOptions);
                writer.Flush();

                var result = buffer.Detach();
                buffer.Dispose(); // Detach 이후 더미 owner dispose
                return result;
            }
            catch
            {
                buffer.Dispose();
                return (MemoryPool<byte>.Shared.Rent(1), 0);
            }
        }

        public static T? Deserialize<T>(object? data)
        {
            if (data is null) return default;

            try
            {
                return data switch
                {
                    string str => string.IsNullOrEmpty(str)
                        ? default
                        : JsonSerializer.Deserialize<T>(str, JsonDefaults.SerializerOptions),

                    JsonElement je => JsonSerializer.Deserialize<T>(je, JsonDefaults.SerializerOptions),

                    byte[] bytes => bytes.Length == 0
                        ? default
                        : JsonSerializer.Deserialize<T>(bytes, JsonDefaults.SerializerOptions),

                    Memory<byte> mem => mem.IsEmpty
                        ? default
                        : JsonSerializer.Deserialize<T>(mem.Span, JsonDefaults.SerializerOptions),

                    ReadOnlyMemory<byte> rom => rom.IsEmpty
                        ? default
                        : JsonSerializer.Deserialize<T>(rom.Span, JsonDefaults.SerializerOptions),

                    _ => default
                };
            }
            catch
            {
                return default;
            }
        }

        public static T? Deserialize<T>(byte[]? data) where T : class
        {
            if (data is null || data.Length == 0) return null;

            try
            {
                return JsonSerializer.Deserialize<T>(data, JsonDefaults.SerializerOptions);
            }
            catch
            {
                return default;
            }
        }
    }
}


