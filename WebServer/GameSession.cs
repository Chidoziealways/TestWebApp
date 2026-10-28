namespace WebServer;

public class GameSession
{
    public int Id { get; set; }
    public string Name { get; set; } = "GameSession";
    public int MaxPlayers { get; set; } = 10;
    public bool IsStarted { get; set; } = false;
    
	public string? ServerIPAddress {get; set;}
    public int? ServerPort {get; set;}
    
    public int MinimumPlayers {get; set;} = 2;

    public List<GamePlayer> Players { get; set; } = new();
}