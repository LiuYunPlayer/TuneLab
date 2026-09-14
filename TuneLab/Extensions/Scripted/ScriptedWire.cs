using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Scripted;

// 脚本 sidecar 的 JSON 编解码（设计文档 §5.3）：DataInfo 全族 + PropertyObject/PropertyValue/
// PropertyArray/Map 是宿主↔插件的唯一序列化真源；façade 依同一规范（wire 规范文件 scripting/wire/
// v1.json 随 M1 提交，映射封条测试钉住）。
//
// PropertyValue 线上形态（写侧恒带标签、读侧宽容收裸值；Multiple 是 UI 三态、永不跨线，见下）：
//   bool → {"b":true}   number → {"n":1.5}（NaN/±Infinity 用 JSON 具名字面量）
//   string → {"s":"x"}  object → {"o":{...}}  array → {"a":[...]}   Null → null
// 读侧裸值推断：JSON 原生 bool/number/string/array/null 直落对应型；对象若"恰含一个保留标签键
// 且值型相符"按 tagged 解，否则视为嵌套 PropertyObject（宿主写侧永不产生该歧义形，宽容只兜第三方手搓）。
// Map<string,T> ↔ JSON 对象（键序按 Map 的插入序写出）；非 string 键的 Map 不支持（抛 NotSupportedException）。
// 属性名 camelCase、公有字段（Point.X/Y）参与序列化、枚举写小写名（"voice"）读宽容（名或数）。
internal static class ScriptedWire
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new DoubleWireConverter());
        o.Converters.Add(new PropertyValueConverter());
        o.Converters.Add(new PropertyObjectConverter());
        o.Converters.Add(new PropertyArrayConverter());
        o.Converters.Add(new PartInfoConverter());
        o.Converters.Add(new MapConverterFactory());
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static JsonNode SerializeNode<T>(T value) => JsonSerializer.SerializeToNode(value, Options)!;

    public static T? DeserializeNode<T>(JsonNode? node) => node == null ? default : node.Deserialize<T>(Options);

    // ── 转换器 ──

    // wire 规范形态：NaN/±Infinity 一律 JSON 字符串 "NaN"|"Infinity"|"-Infinity"（合法 JSON，且
    // net8 的 JsonDocument——转换器共用它的严格解析——不接受裸具名 token）。读侧宽容：数字、
    // 字符串、（经 NumberHandling 的）裸 token 都能收。
    sealed class DoubleWireConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String => Parse(reader.GetString()!),
                JsonTokenType.True or JsonTokenType.False => throw new JsonException("bool is not a number"),
                _ => throw new JsonException("unexpected token for double"),
            };

        static double Parse(string s) => s switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new JsonException("bad number '" + s + "'"),
        };

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsNaN(value)) writer.WriteStringValue("NaN");
            else if (double.IsPositiveInfinity(value)) writer.WriteStringValue("Infinity");
            else if (double.IsNegativeInfinity(value)) writer.WriteStringValue("-Infinity");
            else writer.WriteNumberValue(value);
        }
    }

    // PartInfo 是抽象类（midi/audio 两 concrete 子型）：线上形态 = 子型字段 + 判别器 "kind": "midi"|"audio"。
    sealed class PartInfoConverter : JsonConverter<PartInfo>
    {
        public override PartInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
            switch (kind)
            {
                case "audio": return root.Deserialize<AudioPartInfo>(options);
                case "midi":
                case null: return root.Deserialize<MidiPartInfo>(options);
                default: throw new JsonException(string.Format(CultureInfo.InvariantCulture, "unknown part kind '{0}' (expected midi|audio)", kind));
            }
        }

        public override void Write(Utf8JsonWriter writer, PartInfo value, JsonSerializerOptions options)
        {
            var concreteType = value is AudioPartInfo ? typeof(AudioPartInfo) : typeof(MidiPartInfo);
            var node = JsonSerializer.SerializeToNode(value, concreteType, options)!.AsObject();
            node["kind"] = value is AudioPartInfo ? "audio" : "midi";
            node.WriteTo(writer);
        }
    }

    sealed class PropertyValueConverter : JsonConverter<PropertyValue>
    {
        public override PropertyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null or JsonTokenType.True or JsonTokenType.False
                or JsonTokenType.Number or JsonTokenType.String or JsonTokenType.StartArray or JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return ReadValue(doc.RootElement);
            }
            throw new JsonException(string.Format(CultureInfo.InvariantCulture, "unexpected token {0} for PropertyValue", reader.TokenType));
        }

        // 统一递归：tagged 优先（宿主写侧恒 tagged），裸值宽容（第三方手搓）。
        internal static PropertyValue ReadValue(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Null:
                    return default;
                case JsonValueKind.True:
                    return PropertyValue.Create(true);
                case JsonValueKind.False:
                    return PropertyValue.Create(false);
                case JsonValueKind.Number:
                    return PropertyValue.Create(el.GetDouble());
                case JsonValueKind.String:
                    return PropertyValue.Create(el.GetString()!);
                case JsonValueKind.Array:
                    return PropertyValue.Create(new PropertyArray(el.EnumerateArray().Select(ReadValue).ToList()));
                case JsonValueKind.Object:
                {
                    var props = el.EnumerateObject().ToArray();
                    if (props.Length == 1)
                    {
                        var p = props[0];
                        switch (p.Name)
                        {
                            case "b" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                                return PropertyValue.Create(p.Value.GetBoolean());
                            case "n" when p.Value.ValueKind is JsonValueKind.Number:
                                return PropertyValue.Create(p.Value.GetDouble());
                            case "n" when p.Value.ValueKind == JsonValueKind.String:
                                return PropertyValue.Create(ParseNumberLiteral(p.Value.GetString()!));
                            case "s" when p.Value.ValueKind == JsonValueKind.String:
                                return PropertyValue.Create(p.Value.GetString()!);
                            case "o" when p.Value.ValueKind == JsonValueKind.Object:
                                return PropertyValue.Create(ReadObjectMap(p.Value));
                            case "a" when p.Value.ValueKind == JsonValueKind.Array:
                                return PropertyValue.Create(new PropertyArray(p.Value.EnumerateArray().Select(ReadValue).ToList()));
                        }
                    }
                    return PropertyValue.Create(ReadObjectMap(el));   // 非 tagged 形 = 原生属性对象（键撞保留字的裸 map 见规范注记）
                }
                default:
                    return default;
            }
        }

        internal static PropertyObject ReadObjectMap(JsonElement el)
        {
            var map = new Map<string, PropertyValue>();
            foreach (var p in el.EnumerateObject())
                map[p.Name] = ReadValue(p.Value);
            return new PropertyObject(map);
        }

        internal static double ParseNumberLiteral(string s) => s switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : throw new JsonException("bad number literal '" + s + "'"),
        };

        public override void Write(Utf8JsonWriter writer, PropertyValue value, JsonSerializerOptions options)
        {
            if (value.IsMultiple())
                throw new NotSupportedException("PropertyValue.Multiple is a transient tri-state and must never cross the wire");
            if (value.IsNull())
            {
                writer.WriteNullValue();
                return;
            }
            if (value.ToBoolean(out var b))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("b", b);
                writer.WriteEndObject();
                return;
            }
            if (value.ToDouble(out var d))
            {
                writer.WriteStartObject();
                if (double.IsNaN(d))
                    writer.WriteString("n", "NaN");
                else if (double.IsPositiveInfinity(d))
                    writer.WriteString("n", "Infinity");
                else if (double.IsNegativeInfinity(d))
                    writer.WriteString("n", "-Infinity");
                else
                    writer.WriteNumber("n", d);
                writer.WriteEndObject();
                return;
            }
            if (value.ToString(out var s))
            {
                writer.WriteStartObject();
                writer.WriteString("s", s);
                writer.WriteEndObject();
                return;
            }
            if (value.ToObject(out var o))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("o");
                new PropertyObjectConverter().Write(writer, o, options);
                writer.WriteEndObject();
                return;
            }
            if (value.ToArray(out var a))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("a");
                new PropertyArrayConverter().Write(writer, a, options);
                writer.WriteEndObject();
                return;
            }
            writer.WriteNullValue();
        }
    }

    sealed class PropertyObjectConverter : JsonConverter<PropertyObject>
    {
        public override PropertyObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return PropertyObject.Empty;
            using var doc = JsonDocument.ParseValue(ref reader);
            var map = new Map<string, PropertyValue>();
            foreach (var p in doc.RootElement.EnumerateObject())
                map[p.Name] = JsonSerializer.Deserialize<PropertyValue>(p.Value.GetRawText(), options);
            return new PropertyObject(map);
        }

        public override void Write(Utf8JsonWriter writer, PropertyObject value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kv in value.Map)
            {
                writer.WritePropertyName(kv.Key);
                new PropertyValueConverter().Write(writer, kv.Value, options);
            }
            writer.WriteEndObject();
        }
    }

    sealed class PropertyArrayConverter : JsonConverter<PropertyArray>
    {
        public override PropertyArray Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return PropertyArray.Empty;
            var list = new List<PropertyValue>();
            reader.Read(); // StartArray
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                list.Add(JsonSerializer.Deserialize<PropertyValue>(ref reader, options));
                reader.Read();
            }
            return new PropertyArray(list);
        }

        public override void Write(Utf8JsonWriter writer, PropertyArray value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            var pv = new PropertyValueConverter();
            foreach (var item in value)
                pv.Write(writer, item, options);
            writer.WriteEndArray();
        }
    }

    sealed class MapConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Map<,>)
            && typeToConvert.GetGenericArguments()[0] == typeof(string);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var valueType = typeToConvert.GetGenericArguments()[1];
            var convType = typeof(MapConverter<>).MakeGenericType(valueType);
            return (JsonConverter)Activator.CreateInstance(convType)!;
        }

        sealed class MapConverter<TValue> : JsonConverter<Map<string, TValue>>
        {
            public override Map<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var map = new Map<string, TValue>();
                if (reader.TokenType == JsonTokenType.Null)
                    return map;
                reader.Read(); // StartObject
                while (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var key = reader.GetString()!;
                    reader.Read();
                    map[key] = JsonSerializer.Deserialize<TValue>(ref reader, options)!;
                    reader.Read();
                }
                return map;
            }

            public override void Write(Utf8JsonWriter writer, Map<string, TValue> value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                foreach (var kv in value)
                {
                    writer.WritePropertyName(kv.Key);
                    JsonSerializer.Serialize(writer, kv.Value, options);
                }
                writer.WriteEndObject();
            }
        }
    }
}
