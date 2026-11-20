using System.Globalization;
using System.Text.Json;

namespace CoHost.Contracts;

/// <summary>
/// Thrown by <see cref="ContractJson.Parse{T}"/> when a message cannot be parsed. It carries only a
/// log-safe type name (<see cref="MessageType"/>), a log-safe JSON path (<see cref="JsonException.Path"/>)
/// and the position, and never an inner exception, because System.Text.Json reader messages can echo
/// raw payload bytes such as <c>agentSecret</c>.
/// </summary>
public sealed class ContractParseException : JsonException
{
    internal ContractParseException(string messageType, string path, long? lineNumber, long? bytePositionInLine)
        : base(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Invalid '{messageType}' message at {path} (line {Format(lineNumber)}, byte {Format(bytePositionInLine)})."),
            path,
            lineNumber,
            bytePositionInLine)
    {
        MessageType = messageType;
    }

    /// <summary>
    /// The top-level <c>type</c> when it is log-safe, <c>&lt;invalid&gt;</c> when it is not,
    /// <c>&lt;unknown&gt;</c> when it could not be read.
    /// </summary>
    public string MessageType { get; }

    private static string Format(long? value) =>
        value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "?";
}
