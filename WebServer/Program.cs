using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebServer;


var authTickets = new ConcurrentDictionary<string, AuthTicket>();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=players.db"));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.IncludeFields = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// app.MapGet("/weatherforecast", () =>
//     {
//         var forecast = Enumerable.Range(1, 5).Select(index =>
//                 new WeatherForecast
//                 (
//                     DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
//                     Random.Shared.Next(-20, 55),
//                     summaries[Random.Shared.Next(summaries.Length)]
//                 ))
//             .ToArray();
//         return forecast;
//     })
//     .WithName("GetWeatherForecast");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    await db.Database.MigrateAsync();
}

app.MapPut("/api/players/{id:int}", async (int id, Player input, AppDbContext db) =>
{
    var player = await db.Players.FindAsync(id);

    if (player is null)
    {
        player = new Player { Id = id };
        db.Players.Add(player);
    }
    
    player.Username = input.Username;
    player.Level = input.Level;

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        Message = "Player Saved!",
        Id = id,
        Player = player
    });
});

app.MapGet("/api/players/{id:int}", async (int id, AppDbContext db) =>
{
    var player = await db.Players.FindAsync(id);

    return player is null
        ? Results.NotFound()
        : Results.Ok(player);
});

app.MapGet("/api/players", async (AppDbContext db) =>
{
    var database = await db.Players.ToListAsync();
    return Results.Ok(database);

});

app.MapPost("/api/players", async (Player input, AppDbContext db) =>
{
    var player = new Player { Username = input.Username, Level = input.Level };
    db.Players.Add(player);

    await db.SaveChangesAsync();

    return Results.Created(
        $"/api/players/{player.Id}",
        new {player.Id, player.Username, player.Level}
        );
});

app.MapDelete("/api/players/{id:int}", async (int id, AppDbContext db) =>
{
    var player = await db.Players.FindAsync(id);
    if (player != null) 
        db.Players.Remove(player);
    else
    {
        return Results.NotFound();
    }

    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPatch("/api/players/{id:int}", async (int id, Patch patch, AppDbContext db) =>
{
    var player = await db.Players.FindAsync(id);
    if (player == null) return Results.NotFound();
    
    Console.WriteLine($"Patch type: {patch.Type}");
    Console.WriteLine($"Level: {patch.Value.Level}");
    Console.WriteLine($"Username: '{patch.Value.Username}'");

    switch (patch.Type)
    {
        case PatchType.Level:
        {
            player.Level = patch.Value.Level;
            break;
        }

        case PatchType.Username:
        {
            player.Username = patch.Value.Username;
            break;
        }
        default:
            return Results.BadRequest("Invalid patch type.");
    }

    await db.SaveChangesAsync();
    
    return Results.Ok();
});

app.MapPost("/api/sessions", async(GameSession input, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name))
    {
        return Results.BadRequest("Session Name Must not be Null or WhiteSpace!");
    }

    if (input.MaxPlayers is < 2 or > 100)
    {
        return Results.BadRequest("MaxPlayers must be above 0!");
    }

    Console.WriteLine(input.ServerIPAddress);
    Console.WriteLine(input.ServerPort);

    var gameSession = new GameSession { Name = input.Name, MaxPlayers = input.MaxPlayers, ServerIPAddress = input.ServerIPAddress, ServerPort = input.ServerPort, MinimumPlayers = input.MinimumPlayers};
    db.GameSessions.Add(gameSession);

    await db.SaveChangesAsync();

    return Results.Created(
        $"/api/sessions/{gameSession.Id}",
        new {gameSession.Id, gameSession.Name, gameSession.MaxPlayers, gameSession.IsStarted}
    );
});

app.MapGet("/api/sessions/{id:int}", async (int id, AppDbContext db) =>
{
    var session = await db.GameSessions
        .Where(s => s.Id == id)
        .Select(s => new
        {
            s.Id,
            s.Name,
            s.MaxPlayers,
            s.IsStarted,
            Players = s.Players.Select(p => new
            {
                p.PlayerId,
                p.Player.Username
            }).ToList()
        })
        .FirstOrDefaultAsync();

    return session is null
        ? Results.NotFound()
        : Results.Ok(session);
});

app.MapGet("/api/sessions", async (AppDbContext db) =>
{
    var availableSessions = await db.GameSessions
        .Where(s => !s.IsStarted)
        .Select(s => new
        {
            s.Id,
            s.Name,
            s.MaxPlayers,
            s.IsStarted,
            Players = s.Players.Select(gp => new
            {
                gp.PlayerId,
                gp.Player.Username
            }).ToList()
        })
        .ToListAsync();

    return Results.Ok(availableSessions);
});

app.MapPost("/api/sessions/{id:int}/join", async (int id, JoinSessionRequest request, AppDbContext db) =>
{
    var session = await db.GameSessions.FindAsync(id);
    if (session is null) return Results.NotFound("There was no session with this id!");

    if (session.IsStarted) return Results.BadRequest("Session has already started!");
    
    var currentPlayers = await db.GamePlayers.CountAsync(
        gp => gp.GameSessionId == id);

    if (currentPlayers >= session.MaxPlayers) return Results.Conflict("Too many players in Session!");

    var player = await db.Players.FindAsync(request.PlayerId);
    if (player is null) return Results.NotFound("There is no player with this id!");

    if (await db.GamePlayers.AnyAsync(gp => gp.PlayerId == request.PlayerId)) return Results.Conflict("The player is already in a session!");

    var gamePlayer = new GamePlayer {PlayerId = player.Id, Player = player, GameSessionId = session.Id, GameSession = session};
    db.GamePlayers.Add(gamePlayer);

    await db.SaveChangesAsync();

    string token = Convert.ToHexString(
        RandomNumberGenerator.GetBytes(32)
    );

    authTickets[token] = new AuthTicket(
        player.Id, 
        session.Id,
        DateTime.UtcNow.AddMinutes(2));
    
    return Results.Created(
        $"/api/sessions/{session.Id}",
        new { SessionId = session.Id, PlayerId = player.Id, player.Username, session.ServerIPAddress, session.ServerPort, Token = token }
    );
});

app.MapDelete("/api/sessions/{id:int}/players/{playerId:int}", async (int id, int playerId, AppDbContext db) =>
{
    var membership = await db.GamePlayers
        .FirstOrDefaultAsync(gp => 
            gp.GameSessionId == id &&
            gp.PlayerId == playerId);
    if (membership is null) return Results.NotFound("Membership was NOT found!");

    db.GamePlayers.Remove(membership);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/api/sessions/{id:int}/start", async (int id, AppDbContext db) =>
{
    var session = await db.GameSessions.FindAsync(id);
    if (session is null) return Results.NotFound("Couldn't find the session!");

    if (session.IsStarted) return Results.Conflict("Session has already started!");
    
    var currentPlayers = await db.GamePlayers
        .CountAsync(gp => gp.GameSessionId == id);
    
    if (currentPlayers < session.MinimumPlayers) return Results.Conflict($"Not enough players! minimum required: {minimumNeeded}");

    session.IsStarted = true;
    await db.SaveChangesAsync();

    return Results.Ok("Session Started!");
});

app.MapPost("/api/sessions/{id:int}/end", async (int id, AppDbContext db) =>
{
    var session = await db.GameSessions.FindAsync(id);

    if (session is null) return Results.NotFound("Couldn't find the Session!");

    if (!session.IsStarted) return Results.Conflict("Session isn't currently running!");

    session.IsStarted = false;
    await db.SaveChangesAsync();

    return Results.Ok("Match ended! Lobby is open again!");
});

app.MapPost("/api/auth/validate", (ValidateTokenRequest request) =>
{
    if (!authTickets.TryRemove(request.Token, out var ticket))
    {
        return Results.Unauthorized();
    }

    if (ticket.ExpiresAt <= DateTime.UtcNow)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        ticket.PlayerId,
        ticket.SessionId
    });
});

app.Run();

struct Patch(PatchType type, PatchValue value)
{
    public PatchType Type = type;
    public PatchValue Value = value;
}

enum PatchType
{
    Level,
    Username
}

struct PatchValue
{
    public int Level;
    public string Username;

    public PatchValue(int level = 0, string username = "")
    {
        this.Level = level;
        this.Username = username;
    }
}

public class JoinSessionRequest
{
    public int PlayerId { get; set; }
}

public record ValidateTokenRequest(string Token);