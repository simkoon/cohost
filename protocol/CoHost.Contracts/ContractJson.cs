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
    /// Missing constructor parameters and null in non-nullable members are rejected
    /// (also when serializing).
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.Strict,
            AllowOutOfOrderMetadataProperties = true,
            // CR-01: reject a missing constructor parameter and null in a non-nullable member with
            // JsonException. Before this, a missing installKind silently became msi and a null payload
            // passed. The same checks apply when serializing non-null members.
            // v0.1 is unreleased, so tightening the receiver here is not a compatibility change.
            // A field added in a MINOR version must be a constructor parameter with a default value,
            // otherwise it becomes required (PROTOCOL.md section 2).
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
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
    /// The type name is log-safe (lowercase dot-separated ASCII identifiers, at most 64 characters),
    /// otherwise <c>"&lt;invalid&gt;"</c>.
    /// The payload is never included in exceptions or results (it may hold secrets).
    /// </returns>
    /// <exception cref="ContractParseException">
    /// Malformed JSON, an invalid envelope (<c>id</c>, <c>ts</c>, <c>payload</c>), a missing required
    /// field, null in a non-nullable field, or an unknown or integer enum value. The exception carries
    /// only <c>MessageType</c>, <c>Path</c> and the position, has no inner exception, and is safe to log.
    /// </exception>
    /// <exception cref="InvalidDataException">Input exceeds <see cref="MaxMessageBytes"/>.</exception>
    public static (T? Message, string? UnknownType) Parse<T>(ReadOnlySpan<byte> utf8) where T : Envelope
    {
        if (utf8.Length > MaxMessageBytes)
        {
            throw new InvalidDataException($"Message exceeds {MaxMessageBytes} bytes.");
        }

        T? message;
        try
        {
            message = JsonSerializer.Deserialize<T>(utf8, Options);
        }
        catch (JsonException ex)
        {
            // System.Text.Json reader errors echo the rest of the buffer (from a broken literal such as
            // `fals` to the end), which can include agentSecret/accessToken, so neither the original
            // message nor the exception is propagated (CR-01).
            var type = ReadTopLevelType(utf8);
            var messageType = type is null ? "<unknown>" : IsLogSafeTypeName(type) ? type : "<invalid>";
            var path = IsLogSafePath(ex.Path) ? ex.Path! : "$";
            throw new ContractParseException(messageType, path, ex.LineNumber, ex.BytePositionInLine);
        }

        if (message is null)
        {
            return (null, null);
        }

        if (message.GetType() != typeof(T))
        {
            return (message, null);
        }

        var unknown = ReadTopLevelType(utf8);
        return (null, unknown is null ? "<missing>" : IsLogSafeTypeName(unknown) ? unknown : "<invalid>");
    }

    internal const int MaxLogSafeTypeNameLength = 64;

    internal const int MaxLogSafePathLength = 256;

    /// <summary>
    /// True for lowercase dot-separated ASCII identifiers (<c>local.forceEnd</c>), 1 to 64 characters.
    /// Plain character comparisons, no regular expression: a .NET <c>$</c> also matches before a final newline.
    /// </summary>
    internal static bool IsLogSafeTypeName(string? value)
    {
        if (value is null || value.Length == 0 || value.Length > MaxLogSafeTypeNameLength)
        {
            return false;
        }

        var segmentStart = true;
        foreach (var c in value)
        {
            if (c == '.')
            {
                if (segmentStart)
                {
                    return false;
                }

                segmentStart = true;
                continue;
            }

            if (segmentStart ? !IsAsciiLower(c) : !IsAsciiLetterOrDigit(c))
            {
                return false;
            }

            segmentStart = false;
        }

        return !segmentStart;
    }

    /// <summary>
    /// True for a System.Text.Json path made only of <c>$</c>, <c>.name</c> (ASCII identifier) and
    /// <c>[digits]</c> units, 1 to 256 characters.
    /// </summary>
    internal static bool IsLogSafePath(string? value)
    {
        if (value is null || value.Length == 0 || value.Length > MaxLogSafePathLength || value[0] != '$')
        {
            return false;
        }

        var i = 1;
        while (i < value.Length)
        {
            if (value[i] == '.')
            {
                i++;
                if (i >= value.Length || !IsAsciiLetter(value[i]))
                {
                    return false;
                }

                i++;
                while (i < value.Length && IsAsciiLetterOrDigit(value[i]))
                {
                    i++;
                }
            }
            else if (value[i] == '[')
            {
                i++;
                var digits = 0;
                while (i < value.Length && IsAsciiDigit(value[i]))
                {
                    i++;
                    digits++;
                }

                if (digits is < 1 or > 9 || i >= value.Length || value[i] != ']')
                {
                    return false;
                }

                i++;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the top-level <c>type</c> string without deserializing. Returns null when it is absent,
    /// not a string, or the input is unreadable. Never throws: a lone surrogate makes
    /// <c>GetString()</c> throw <see cref="InvalidOperationException"/>.
    /// </summary>
    internal static string? ReadTopLevelType(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isType = reader.ValueTextEquals("type"u8);
                if (!reader.Read())
                {
                    return null;
                }

                if (isType)
                {
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }

                reader.Skip();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsAsciiLower(char c) => c is >= 'a' and <= 'z';

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || IsAsciiDigit(c);
}
