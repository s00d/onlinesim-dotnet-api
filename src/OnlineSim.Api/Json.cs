using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlineSim.Api;

/// <summary>
/// JSON reading shared by the sections and <see cref="Webhook"/>. Models keep the server's field names
/// (snake_case on the wire, PascalCase in C#) with the client-side normalisations of the contract:
/// decimal-string money becomes a number, maps sent as a PHP list (<c>[]</c>, or <c>[a, b]</c> when the keys are
/// 0..n) become index-keyed maps, and nullable numbers are lenient (a number, a numeric string, otherwise <c>null</c>).
/// </summary>
internal static class Json
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new LenientMapConverterFactory());
        options.Converters.Add(new LenientNullableConverter<decimal>(TryDecimal));
        options.Converters.Add(new LenientNullableConverter<long>(Long));
        options.Converters.Add(new LenientNullableConverter<int>(Int));
        return options;
    }

    /// <summary>Deserialize <paramref name="element"/>; a shape that does not fit becomes <see cref="TransportException"/>.</summary>
    internal static T Read<T>(JsonElement element)
    {
        try
        {
            return element.Deserialize<T>(Options)!;
        }
        catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException)
        {
            throw new TransportException($"Unexpected response shape: {e.Message}", null, OnlineSimClient.Excerpt(element.GetRawText()), e);
        }
    }

    /// <summary>Property <paramref name="name"/> of an object, deserialized; <c>default</c> when absent.</summary>
    internal static T? Field<T>(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? Read<T>(value) : default;

    /// <summary>Number from a number or a numeric string (<c>"1442.330"</c>); <c>null</c> when missing or not numeric.</summary>
    internal static decimal? Num(JsonElement element) => TryDecimal(element);

    private static decimal? TryDecimal(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number) return e.TryGetDecimal(out var d) ? d : null;
        if (e.ValueKind == JsonValueKind.String &&
            decimal.TryParse(e.GetString()!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    internal static long? Long(JsonElement e)
    {
        var d = TryDecimal(e);
        return d.HasValue && d.Value == decimal.Truncate(d.Value) && d.Value >= long.MinValue && d.Value <= long.MaxValue ? (long)d.Value : null;
    }

    internal static int? Int(JsonElement e)
    {
        var l = Long(e);
        return l.HasValue && l.Value >= int.MinValue && l.Value <= int.MaxValue ? (int)l.Value : null;
    }

    /// <summary><c>T?</c> from a number or numeric string; anything else (<c>false</c>, <c>"?"</c>, objects) is <c>null</c>.</summary>
    private sealed class LenientNullableConverter<T> : JsonConverter<T?> where T : struct
    {
        private readonly Func<JsonElement, T?> _parse;

        public LenientNullableConverter(Func<JsonElement, T?> parse) => _parse = parse;

        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return _parse(doc.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value.HasValue) JsonSerializer.Serialize(writer, value.Value);
            else writer.WriteNullValue();
        }
    }

    /// <summary>
    /// Maps (<see cref="IReadOnlyDictionary{TKey, TValue}"/>): PHP sends a map whose keys are 0..n as a JSON array
    /// (an empty one as <c>[]</c>); read it as the map it is, keyed by index.
    /// </summary>
    private sealed class LenientMapConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var args = typeToConvert.GetGenericArguments();
            return (JsonConverter)Activator.CreateInstance(typeof(LenientMapConverter<,>).MakeGenericType(args[0], args[1]))!;
        }
    }

    private sealed class LenientMapConverter<TKey, TValue> : JsonConverter<IReadOnlyDictionary<TKey, TValue>> where TKey : notnull
    {
        public override IReadOnlyDictionary<TKey, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var items = JsonSerializer.Deserialize<List<TValue>>(ref reader, options)!;
                var map = new Dictionary<TKey, TValue>();
                for (var i = 0; i < items.Count; i++) map[(TKey)Convert.ChangeType(i, typeof(TKey), CultureInfo.InvariantCulture)] = items[i];
                return map;
            }
            return JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(ref reader, options) ?? new Dictionary<TKey, TValue>();
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TKey, TValue> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, ToDictionary(value), options);

        private static Dictionary<TKey, TValue> ToDictionary(IReadOnlyDictionary<TKey, TValue> value)
        {
            var copy = new Dictionary<TKey, TValue>();
            foreach (var pair in value) copy[pair.Key] = pair.Value;
            return copy;
        }
    }
}

/// <summary>
/// A string field the server may also send as a number: the number becomes its JSON text (rent SMS <c>code</c> is
/// the integer <c>0</c> for a <c>notify</c> SMS → <c>"0"</c>). Booleans likewise; objects and arrays read as <c>null</c>.
/// </summary>
internal sealed class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        using var doc = JsonDocument.ParseValue(ref reader);
        var kind = doc.RootElement.ValueKind;
        return kind == JsonValueKind.Number || kind == JsonValueKind.True || kind == JsonValueKind.False ? doc.RootElement.GetRawText() : null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>Query string builder with the contract's encoding: unset/empty omitted, booleans <c>1</c>/<c>0</c>, lists a comma list.</summary>
internal sealed class Query
{
    private readonly List<KeyValuePair<string, string>> _pairs = new List<KeyValuePair<string, string>>();

    internal IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;

    internal Query Add(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) _pairs.Add(new KeyValuePair<string, string>(key, value!));
        return this;
    }

    internal Query Add(string key, long? value) => Add(key, value?.ToString(CultureInfo.InvariantCulture));

    internal Query Add(string key, bool value) => Add(key, value ? "1" : "0");

    internal Query Add(string key, IEnumerable<string>? values) => Add(key, values == null ? null : string.Join(",", values));
}
