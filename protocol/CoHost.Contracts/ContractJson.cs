using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoHost.Contracts;

/// <summary>
/// The single serializer configuration for every protocol message and REST DTO.
/// </summary>
public static partial class ContractJson
{
    /// <summary>Upper bound for one JSON message. Larger input is rejected before deserialization.</summary>
    public const int MaxMessageBytes = 1_048_576;

    /// <summary>
    /// camelCase names, snake_case enum strings (integers rejected), strict numbers,
    /// discriminator may appear anywhere in the object, null is always written.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.Strict,
            AllowOutOfOrderMetadataProperties = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// Parses one message. <typeparamref name="T"/> must be a <c>[JsonPolymorphic]</c> channel base
    /// that is not abstract, so an unrecognized <c>type</c> falls back to the base instance.
    /// </summary>
    /// <returns>
    /// <c>(message, null)</c> for a known type; <c>(null, type)</c> for an unknown type
    /// (<c>"&lt;missing&gt;"</c> when there is no <c>type</c>); <c>(null, null)</c> for JSON <c>null</c>.
    /// The payload is never included in exceptions or results (it may hold secrets).
    /// </returns>
    /// <exception cref="InvalidDataException">Input exceeds <see cref="MaxMessageBytes"/>.</exception>
    public static (T? Message, string? UnknownType) Parse<T>(ReadOnlySpan<byte> utf8) where T : Envelope
    {
        if (utf8.Length > MaxMessageBytes)
        {
            throw new InvalidDataException($"Message exceeds {MaxMessageBytes} bytes.");
        }

        var message = JsonSerializer.Deserialize<T>(utf8, Options);
        if (message is null)
        {
            return (null, null);
        }

        if (message.GetType() != typeof(T))
        {
            return (message, null);
        }

        using var doc = JsonDocument.Parse(utf8.ToArray());
        var type = doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("type", out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        return (null, type ?? "<missing>");
    }
}
