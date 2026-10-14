using System.Text.Json;

namespace Match.Protocol;

// A message that could not be read: the protocol error and a short explanation for the sender.
public sealed record ParseFailure(ProtocolError Error, string Text);

// Reading and writing whole messages. Reading never throws: anything that is not a well-formed message of this
// protocol version, within the size and depth limits, comes back as a ParseFailure.
public static class Codec
{
    public static byte[] Encode(ClientMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Wire.Options);

    public static byte[] Encode(ServerMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Wire.Options);

    public static ClientMessage? ParseClient(ReadOnlySpan<byte> utf8, out ParseFailure? failure) =>
        Parse<ClientMessage>(utf8, ProtocolLimits.MaxClientMessageBytes, out failure);

    public static ServerMessage? ParseServer(ReadOnlySpan<byte> utf8, out ParseFailure? failure) =>
        Parse<ServerMessage>(utf8, ProtocolLimits.MaxServerMessageBytes, out failure);

    private static T? Parse<T>(ReadOnlySpan<byte> utf8, int maxBytes, out ParseFailure? failure) where T : class
    {
        failure = null;
        if (utf8.Length > maxBytes)
        {
            failure = new ParseFailure(ProtocolError.TooLarge, $"A message is at most {maxBytes} bytes.");
            return null;
        }
        if (!System.Text.Unicode.Utf8.IsValid(utf8))
        {
            failure = new ParseFailure(ProtocolError.Malformed, "A message is UTF-8 text.");
            return null;
        }
        try
        {
            var message = JsonSerializer.Deserialize<T>(utf8, Wire.Options);
            if (message is null)
            {
                failure = new ParseFailure(ProtocolError.Malformed, "A message is a JSON object.");
            }
            return message;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            failure = Classify(e);
            return null;
        }
    }

    // Only the classification and a fixed text go back to the sender, never the exception's own message.
    private static ParseFailure Classify(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case Wire.UnsupportedVersionException:
                    return new ParseFailure(ProtocolError.UnsupportedVersion, $"This server speaks protocol version {ProtocolLimits.Version}.");
                case Wire.UnknownTypeException unknown:
                    return new ParseFailure(ProtocolError.UnknownType, $"Unknown {unknown.Kind} type.");
            }
        }
        return Wire.IsInvalidValue(exception)
            ? new ParseFailure(ProtocolError.InvalidValue, "A value is out of range or not allowed.")
            : new ParseFailure(ProtocolError.Malformed, "The message is not a well-formed protocol message.");
    }
}
