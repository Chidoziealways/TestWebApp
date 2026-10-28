namespace GameProtocol;

public sealed class GameMessage
{
    public byte Version { get; init; } = ProtocolConstants.Version;
    
    public MessageType Type { get; init; }

    public byte[] Payload { get; init; } = [];
}