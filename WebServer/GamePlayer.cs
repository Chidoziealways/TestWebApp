namespace WebServer;

public class GamePlayer
{
    public int Id { get; set; }
    
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    
    public int GameSessionId { get; set; }
    public GameSession GameSession { get; set; } = null!;
}