using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if EVENTBOARD_NEWTONSOFT_JSON
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace Yasin.EventBoard
{
    /// <summary>
    /// Abstraction for serializing and deserializing individual channel values in EventBoard snapshots.
    /// Swap via EventBoard.Serializer to customize JSON formatting or use a custom serializer.
    /// </summary>
    public interface IEventBoardSerializer
    {
        string SerializeValue<T>(T value);
        bool TryDeserializeValue<T>(string json, out T value);
    }

    /// <summary>
    /// Built-in zero-dependency serializer backed by UnityEngine.JsonUtility.
    /// Safe across all system cultures (tr-TR, en-US, etc.) and preserves reference-type null values.
    /// </summary>
    public sealed class UnityJsonSerializer : IEventBoardSerializer
    {
        public static readonly UnityJsonSerializer Instance = new UnityJsonSerializer();

        [Serializable]
        private struct JsonValueBox<T>
        {
            public bool isNull;
            public T value;
        }

        public string SerializeValue<T>(T value)
        {
            bool isNull = !typeof(T).IsValueType && value == null;
            return JsonUtility.ToJson(new JsonValueBox<T>
            {
                isNull = isNull,
                value = isNull ? default : value
            });
        }

        public bool TryDeserializeValue<T>(string json, out T value)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                value = default;
                return false;
            }

            try
            {
                JsonValueBox<T> box = JsonUtility.FromJson<JsonValueBox<T>>(json);
                value = (!typeof(T).IsValueType && box.isNull) ? default : box.value;
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }
    }

#if EVENTBOARD_NEWTONSOFT_JSON
    /// <summary>
    /// Automatically activated serializer when com.unity.nuget.newtonsoft-json is installed.
    /// Supports Dictionary, HashSet, DateTime, Guid, nullable value types, and properties,
    /// while converting Unity math structs without self-referencing loop errors and maintaining JsonUtility shape compatibility.
    /// </summary>
    public sealed class NewtonsoftEventBoardSerializer : IEventBoardSerializer
    {
        public static readonly NewtonsoftEventBoardSerializer Instance = new NewtonsoftEventBoardSerializer();

        [Serializable]
        private struct JsonValueBox<T>
        {
            public bool isNull;
            public T value;
        }

        private readonly JsonSerializerSettings _settings;

        public NewtonsoftEventBoardSerializer(JsonSerializerSettings settings = null)
        {
            _settings = settings ?? CreateDefaultSettings();
        }

        public static JsonSerializerSettings CreateDefaultSettings()
        {
            return new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Formatting.None,
                Converters = new List<JsonConverter>
                {
                    new Vector2Converter(),
                    new Vector3Converter(),
                    new Vector4Converter(),
                    new QuaternionConverter(),
                    new ColorConverter(),
                    new Color32Converter(),
                    new Vector2IntConverter(),
                    new Vector3IntConverter(),
                    new RectConverter(),
                    new BoundsConverter()
                }
            };
        }

        public string SerializeValue<T>(T value)
        {
            bool isNull = !typeof(T).IsValueType && value == null;
            return JsonConvert.SerializeObject(new JsonValueBox<T>
            {
                isNull = isNull,
                value = isNull ? default : value
            }, _settings);
        }

        public bool TryDeserializeValue<T>(string json, out T value)
        {
            if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.Ordinal))
            {
                value = default;
                return false;
            }

            try
            {
                JsonValueBox<T> box = JsonConvert.DeserializeObject<JsonValueBox<T>>(json, _settings);
                value = (!typeof(T).IsValueType && box.isNull) ? default : box.value;
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        private sealed class Vector2Converter : JsonConverter<Vector2>
        {
            public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WriteEndObject();
            }

            public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Vector2(obj.Value<float>("x"), obj.Value<float>("y"));
            }
        }

        private sealed class Vector3Converter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WriteEndObject();
            }

            public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Vector3(obj.Value<float>("x"), obj.Value<float>("y"), obj.Value<float>("z"));
            }
        }

        private sealed class Vector4Converter : JsonConverter<Vector4>
        {
            public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WritePropertyName("w"); writer.WriteValue(value.w);
                writer.WriteEndObject();
            }

            public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Vector4(obj.Value<float>("x"), obj.Value<float>("y"), obj.Value<float>("z"), obj.Value<float>("w"));
            }
        }

        private sealed class QuaternionConverter : JsonConverter<Quaternion>
        {
            public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WritePropertyName("w"); writer.WriteValue(value.w);
                writer.WriteEndObject();
            }

            public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Quaternion(obj.Value<float>("x"), obj.Value<float>("y"), obj.Value<float>("z"), obj.Value<float>("w"));
            }
        }

        private sealed class ColorConverter : JsonConverter<Color>
        {
            public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("r"); writer.WriteValue(value.r);
                writer.WritePropertyName("g"); writer.WriteValue(value.g);
                writer.WritePropertyName("b"); writer.WriteValue(value.b);
                writer.WritePropertyName("a"); writer.WriteValue(value.a);
                writer.WriteEndObject();
            }

            public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Color(obj.Value<float>("r"), obj.Value<float>("g"), obj.Value<float>("b"), obj.Value<float>("a"));
            }
        }

        private sealed class Color32Converter : JsonConverter<Color32>
        {
            public override void WriteJson(JsonWriter writer, Color32 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("r"); writer.WriteValue(value.r);
                writer.WritePropertyName("g"); writer.WriteValue(value.g);
                writer.WritePropertyName("b"); writer.WriteValue(value.b);
                writer.WritePropertyName("a"); writer.WriteValue(value.a);
                writer.WriteEndObject();
            }

            public override Color32 ReadJson(JsonReader reader, Type objectType, Color32 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Color32(obj.Value<byte>("r"), obj.Value<byte>("g"), obj.Value<byte>("b"), obj.Value<byte>("a"));
            }
        }

        private sealed class Vector2IntConverter : JsonConverter<Vector2Int>
        {
            public override void WriteJson(JsonWriter writer, Vector2Int value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WriteEndObject();
            }

            public override Vector2Int ReadJson(JsonReader reader, Type objectType, Vector2Int existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Vector2Int(obj.Value<int>("x"), obj.Value<int>("y"));
            }
        }

        private sealed class Vector3IntConverter : JsonConverter<Vector3Int>
        {
            public override void WriteJson(JsonWriter writer, Vector3Int value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WriteEndObject();
            }

            public override Vector3Int ReadJson(JsonReader reader, Type objectType, Vector3Int existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Vector3Int(obj.Value<int>("x"), obj.Value<int>("y"), obj.Value<int>("z"));
            }
        }

        private sealed class RectConverter : JsonConverter<Rect>
        {
            public override void WriteJson(JsonWriter writer, Rect value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("width"); writer.WriteValue(value.width);
                writer.WritePropertyName("height"); writer.WriteValue(value.height);
                writer.WriteEndObject();
            }

            public override Rect ReadJson(JsonReader reader, Type objectType, Rect existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                return new Rect(obj.Value<float>("x"), obj.Value<float>("y"), obj.Value<float>("width"), obj.Value<float>("height"));
            }
        }

        private sealed class BoundsConverter : JsonConverter<Bounds>
        {
            public override void WriteJson(JsonWriter writer, Bounds value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("center"); serializer.Serialize(writer, value.center);
                writer.WritePropertyName("extents"); serializer.Serialize(writer, value.extents);
                writer.WriteEndObject();
            }

            public override Bounds ReadJson(JsonReader reader, Type objectType, Bounds existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return default;
                JObject obj = JObject.Load(reader);
                JToken centerToken = obj["center"] ?? obj["m_Center"];
                JToken extentsToken = obj["extents"] ?? obj["m_Extents"];
                Vector3 center = centerToken != null ? centerToken.ToObject<Vector3>(serializer) : Vector3.zero;
                Vector3 extents = extentsToken != null ? extentsToken.ToObject<Vector3>(serializer) : Vector3.zero;
                return new Bounds(center, new Vector3(extents.x * 2f, extents.y * 2f, extents.z * 2f));
            }
        }
    }
#endif
}
