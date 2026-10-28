namespace WebServer;

public record AuthTicket(
    int PlayerId,
    int SessionId,
    DateTime ExpiresAt);