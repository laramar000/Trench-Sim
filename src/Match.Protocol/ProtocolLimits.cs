namespace Match.Protocol;

public static class ProtocolLimits
{
    public const int Version = 1;

    // A client message is one WebSocket text message of at most this many bytes. The largest legal command (a
    // 16-waypoint move) is well under 2 KB.
    public const int MaxClientMessageBytes = 16 * 1024;

    // What a client accepts from the server; the match setup event is the largest message.
    public const int MaxServerMessageBytes = 1024 * 1024;

    public const int MaxDepth = 16;

    // Join codes and command ids: short printable ASCII.
    public const int MaxIdLength = 64;
}
