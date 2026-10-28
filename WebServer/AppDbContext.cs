using Microsoft.EntityFrameworkCore;

namespace WebServer;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();

    public DbSet<GameSession> GameSessions => Set<GameSession>();

    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GamePlayer>()
            .HasIndex(gp => gp.PlayerId)
            .IsUnique();
    }
}

public class Player
{
    public int Id { get; set; }
    public string Username { get; set; } = "";

    public int Level { get; set; }
}
