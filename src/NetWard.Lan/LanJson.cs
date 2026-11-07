using System;
using System.Text;

#if NET48
using System.Web.Script.Serialization;
#else
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace NetWard.Lan
{
    /// <summary>
    /// Wire format for every device-to-device message.
    /// Property names are PascalCase on the wire so that both serializers used by the two editions
    /// (JavaScriptSerializer on .NET Framework, System.Text.Json on .NET 9) read the same bytes.
    /// Only strings, numbers, booleans and nested objects are used: timestamps travel as round-trip
    /// ISO-8601 strings so no framework-specific date format can leak into the protocol.
    /// </summary>
    internal static class LanJson
    {
        public const int MaximumDocumentBytes = 512 * 1024;

#if NET48
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = MaximumDocumentBytes
        };

        public static string Serialize(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            return Serializer.Serialize(value);
        }

        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "消息体为空。");
            }

            if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "消息体超过大小上限。");
            }

            try
            {
                return Serializer.Deserialize<T>(json);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "消息体不是有效的 JSON。", exception);
            }
        }
#else
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32
        };

        public static string Serialize(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            return JsonSerializer.Serialize(value, value.GetType(), Options);
        }

        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "消息体为空。");
            }

            if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "消息体超过大小上限。");
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, Options);
            }
            catch (JsonException exception)
            {
                throw new LanProtocolException(LanErrorCodes.BadRequest, "消息体不是有效的 JSON。", exception);
            }
        }
#endif
    }
}
