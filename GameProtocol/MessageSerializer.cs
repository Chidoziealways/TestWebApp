namespace GameProtocol;

public static class MessageSerializer
{
    public static byte[] Serialize(GameMessage message)
    {
        using var stream = new MemoryStream();
        
        stream.WriteByte(message.Version);
        stream.WriteByte((byte)message.Type);
        stream.Write(message.Payload);

        return stream.ToArray();
    }

    public static GameMessage Deserialize(byte[] data)
    {
        if (data.Length < 2)
            throw new InvalidDataException("Message is too short!");

        byte version = data[0];

        if (version != ProtocolConstants.Version)
            throw new InvalidDataException(
                $"Unsupported Protocol Version: {version}");

        var type = (MessageType)data[1];
        if (!Enum.IsDefined(type))
            throw new InvalidDataException(
                $"Unknown message type: {(byte)type}");

        byte[] payload = data[2..];

        return new GameMessage
        {
            Version = version,
            Type = type,
            Payload = payload
        };
    }
}