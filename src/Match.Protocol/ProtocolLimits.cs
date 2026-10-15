namespace Match.Protocol;

public static class ProtocolLimits
{
    // Version 2 (M6) adds rejoin with seat tokens, pausing and the longer Welcome. Version 1 is refused.
    public const int Version = 2;

    // A client message is one WebSocket text message of at most this many bytes. The largest legal command (a
    // 16-waypoint move) is well under 2 KB.
    public const int MaxClientMessageBytes = 16 * 1024;

    // What a client accepts from the server; the match setup event is the largest message.
    public const int MaxServerMessageBytes = 1024 * 1024;

    public const int MaxDepth = 16;

    // Join codes and command ids: short printable ASCII.
    public const int MaxIdLength = 64;

    // A seat token is 32 random bytes in base64url without padding.
    public const int SeatTokenLength = 43;
}
