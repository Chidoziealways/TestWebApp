namespace GameProtocol;

public enum MessageType : byte
{
    // Connection & Authentication
    Auth = 1,
    AuthResult = 2,
    
    // Lobby Membership
    JoinLobby = 10,
    JoinLobbyResult = 11,
    PlayerJoined = 12,
    PlayerLeft = 13,
    
    // Social Features
    ChatMessage = 20,
    PlayerPresence = 21,
    
    // Connection Maintenance
    Ping = 30,
    Pong = 31,
    
    // Error
    Error = 40
}