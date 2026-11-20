using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlineSim.Api;

/// <summary>Inbound webhook body as normalised by <see cref="Webhook.Parse(string)"/>: missing or malformed fields are <c>null</c>.</summary>
[JsonConverter(typeof(WebhookPayloadConverter))]
public sealed class WebhookPayload
{
    /// <summary><see cref="Webhook.ReceivingSms"/>, <see cref="Webhook.RentSms"/>, or <c>null</c>.</summary>
    public string? WebhookType { get; init; }

    /// <summary><c>tzid</c> of the operation or rent.</summary>
    public long? OperationId { get; init; }

    /// <summary>Account id.</summary>
    public long? UserId { get; init; }

    /// <summary>Country code of the number.</summary>
    public int? CountryCode { get; init; }

    /// <summary>Activations: <c>+</c> and the full number (e.g. <c>+79001234567</c>), or <c>hidden</c>; rent: the full number in digits without <c>+</c>.</summary>
    public string? Number { get; init; }

    /// <summary>Sender of the SMS, e.g. <c>VK</c>.</summary>
    public string? Sender { get; init; }

    /// <summary>Full SMS text.</summary>
    public string? Message { get; init; }

    /// <summary>Code extracted from the SMS; <c>""</c> when none.</summary>
    public string? Code { get; init; }

    /// <summary>Activations: the order time; rent: the SMS time.</summary>
    public string? TimeStart { get; init; }

    /// <summary>Time left: seconds for activations, minutes for rent.</summary>
    public long? TimeLeft { get; init; }
}

/// <summary>Parses inbound webhooks (<c>receiving_sms</c>, <c>rent_sms</c>).</summary>
public static class Webhook
{
    /// <summary><c>webhook_type</c> of a number operation SMS.</summary>
    public const string ReceivingSms = "receiving_sms";

    /// <summary><c>webhook_type</c> of a rent SMS.</summary>
    public const string RentSms = "rent_sms";

    /// <summary>
    /// Parse an inbound webhook body. Lenient: numeric fields may arrive as strings, missing or malformed fields
    /// become <c>null</c>, unknown fields are dropped. Throws <see cref="OnlineSimException"/> only when the
    /// input is not a JSON object.
    /// </summary>
    public static WebhookPayload Parse(string payload)
    {
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload);
        }
        catch (JsonException e)
        {
            throw new OnlineSimException("Webhook payload is not valid JSON", e);
        }
        using (doc) return Parse(doc.RootElement);
    }

    /// <summary>Parse an already parsed webhook body; see <see cref="Parse(string)"/>.</summary>
    public static WebhookPayload Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw new OnlineSimException("Webhook payload is not a JSON object");
        var type = Str(payload, "webhook_type");
        return new WebhookPayload
        {
            WebhookType = type == ReceivingSms || type == RentSms ? type : null,
            OperationId = Long(payload, "operation_id"),
            UserId = Long(payload, "user_id"),
            CountryCode = Int(payload, "country_code"),
            Number = Str(payload, "number"),
            Sender = Str(payload, "sender"),
            Message = Str(payload, "message"),
            Code = Str(payload, "code"),
            TimeStart = Str(payload, "time_start"),
            TimeLeft = Long(payload, "time_left"),
        };
    }

    private static string? Str(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
        return null;
    }

    private static long? Long(JsonElement o, string name) => o.TryGetProperty(name, out var v) ? Json.Long(v) : null;

    private static int? Int(JsonElement o, string name) => o.TryGetProperty(name, out var v) ? Json.Int(v) : null;
}

/// <summary>Reads <see cref="WebhookPayload"/> through <see cref="Webhook.Parse(JsonElement)"/> (lenient); writes the wire names.</summary>
internal sealed class WebhookPayloadConverter : JsonConverter<WebhookPayload>
{
    public override WebhookPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.ValueKind == JsonValueKind.Object ? Webhook.Parse(doc.RootElement) : new WebhookPayload();
    }

    public override void Write(Utf8JsonWriter writer, WebhookPayload value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("webhook_type", value.WebhookType);
        Number(writer, "operation_id", value.OperationId);
        Number(writer, "user_id", value.UserId);
        Number(writer, "country_code", value.CountryCode);
        writer.WriteString("number", value.Number);
        writer.WriteString("sender", value.Sender);
        writer.WriteString("message", value.Message);
        writer.WriteString("code", value.Code);
        writer.WriteString("time_start", value.TimeStart);
        Number(writer, "time_left", value.TimeLeft);
        writer.WriteEndObject();
    }

    private static void Number(Utf8JsonWriter writer, string name, long? value)
    {
        if (value.HasValue) writer.WriteNumber(name, value.Value);
        else writer.WriteNull(name);
    }
}
