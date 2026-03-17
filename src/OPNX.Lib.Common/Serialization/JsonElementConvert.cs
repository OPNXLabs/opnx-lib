using OPNX.Lib.Common.Logging;
using System.Text.Json;

namespace OPNX.Lib.Common.Serialization
{
    public static class JsonElementConvert
    {
        public static object? ConvertStringValue(string? strValue, Type targetType)
        {
            if (strValue is null)
                return GetDefaultOrNull(targetType);

            // Nullable이면 underlying으로 변환
            var underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null)
                return ConvertStringValue(strValue, underlying);

            try
            {
                switch (Type.GetTypeCode(targetType))
                {
                    case TypeCode.Boolean:
                        return bool.TryParse(strValue, out var b) ? b : GetDefaultOrNull(targetType);

                    case TypeCode.Int32:
                        return int.TryParse(strValue, out var i) ? i : GetDefaultOrNull(targetType);

                    case TypeCode.Int64:
                        return long.TryParse(strValue, out var l) ? l : GetDefaultOrNull(targetType);

                    case TypeCode.Double:
                        return double.TryParse(strValue, out var d) ? d : GetDefaultOrNull(targetType);

                    case TypeCode.DateTime:
                        return DateTime.TryParse(strValue, out var dt) ? dt : GetDefaultOrNull(targetType);

                    case TypeCode.String:
                        return strValue;

                    default:
                        if (targetType.IsEnum)
                        {
                            // enum 이름 또는 숫자 모두 지원
                            if (Enum.TryParse(targetType, strValue, ignoreCase: true, out var enumObj))
                                return enumObj;

                            if (int.TryParse(strValue, out var enumInt))
                                return Enum.ToObject(targetType, enumInt);

                            return GetDefaultOrNull(targetType);
                        }

                        return Convert.ChangeType(strValue, targetType);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return GetDefaultOrNull(targetType);
            }
        }

        public static object? ConvertJsonElement(JsonElement jsonElement, Type targetType)
        {
            if (jsonElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return GetDefaultOrNull(targetType);

            // Nullable이면 underlying으로 변환
            var underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null)
                return ConvertJsonElement(jsonElement, underlying);

            try
            {
                return jsonElement.ValueKind switch
                {
                    JsonValueKind.String => ConvertStringValue(jsonElement.GetString(), targetType),

                    JsonValueKind.Number => ConvertNumber(jsonElement, targetType),

                    JsonValueKind.True or JsonValueKind.False => targetType == typeof(bool)
                        ? jsonElement.GetBoolean()
                        : GetDefaultOrNull(targetType),

                    JsonValueKind.Object or JsonValueKind.Array =>
                        JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType, JsonDefaults.SerializerOptions),

                    _ => GetDefaultOrNull(targetType)
                };
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return GetDefaultOrNull(targetType);
            }
        }

        public static object? ConvertValue(object? value, Type targetType)
        {
            if (value is null)
                return GetDefaultOrNull(targetType);

            try
            {
                // JsonElement 처리
                if (value is JsonElement je)
                    return ConvertJsonElement(je, targetType);

                // 이미 assign 가능하면 그대로
                var valueType = value.GetType();
                if (targetType.IsAssignableFrom(valueType))
                    return value;

                // Nullable이면 underlying으로 변환
                var underlying = Nullable.GetUnderlyingType(targetType);
                if (underlying != null)
                    return ConvertValue(value, underlying);

                // 문자열 처리
                if (value is string s)
                    return ConvertStringValue(s, targetType);

                // enum 처리
                if (targetType.IsEnum)
                {
                    return value is string es
                        ? ConvertStringValue(es, targetType)
                        : Enum.ToObject(targetType, value);
                }

                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                return GetDefaultOrNull(targetType);
            }
        }

        private static object? ConvertNumber(JsonElement jsonElement, Type targetType)
        {
            if (targetType == typeof(int))
                return jsonElement.TryGetInt32(out var i) ? i : GetDefaultOrNull(targetType);

            if (targetType == typeof(long))
                return jsonElement.TryGetInt64(out var l) ? l : GetDefaultOrNull(targetType);

            if (targetType == typeof(double))
                return jsonElement.TryGetDouble(out var d) ? d : GetDefaultOrNull(targetType);

            if (targetType == typeof(float))
                return jsonElement.TryGetDouble(out var fd) ? (float)fd : GetDefaultOrNull(targetType);

            if (targetType.IsEnum)
            {
                if (jsonElement.TryGetInt32(out var ev))
                    return Enum.ToObject(targetType, ev);

                return GetDefaultOrNull(targetType);
            }

            // 기타 숫자 타입은 double 경유 fallback
            if (jsonElement.TryGetDouble(out var dv))
                return Convert.ChangeType(dv, targetType);

            return GetDefaultOrNull(targetType);
        }

        private static object? GetDefaultOrNull(Type type)
            => type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
