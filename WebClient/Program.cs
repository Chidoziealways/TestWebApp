using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using GameProtocol;
using LiteNetLib;
using Microsoft.VisualBasic.FileIO;

using var client = new HttpClient();

string url = "http://localhost:5157";

string? token = null;

var stop = false;

var listener = new EventBasedNetListener();
var sClient = new NetManager(listener);

listener.PeerConnectedEvent += peer =>
{
    Console.WriteLine("Connected to GameServer!");
    if (token is null) return;
    byte[] payload = System.Text.Encoding.UTF8.GetBytes(token);

    var message = new GameMessage
    {
        Type = MessageType.Auth,
        Payload = payload
    };
    
    peer.Send(
        MessageSerializer.Serialize(message),
        DeliveryMethod.ReliableOrdered);
    
    Console.WriteLine("Sent: Auth");
};

listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
{
    try
    {
        byte[] bytes = new byte[reader.AvailableBytes];
        reader.GetBytes(bytes, bytes.Length);
        
        GameMessage message = MessageSerializer.Deserialize(bytes);
        Console.WriteLine($"Received: {message.Type}");
        if (message.Type == MessageType.AuthResult)
        {
            Console.WriteLine("Authentication succeeded!");

            var ping = MessageSerializer.Serialize(new GameMessage
            {
                Type = MessageType.Ping,
                Payload = []
            });

            peer.Send(ping, DeliveryMethod.ReliableOrdered);
            Console.WriteLine("Sent: Ping");
        }
        else if (message.Type == MessageType.ChatMessage)
        {
            ChatPayload? chat =
                JsonSerializer.Deserialize<ChatPayload>(message.Payload);

            if (chat is not null)
                Console.WriteLine($"\n{chat.Text}");
        } 
        else if (message.Type == MessageType.Error)
        {
            ChatPayload? payload = JsonSerializer.Deserialize<ChatPayload>(message.Payload);
            if (payload is not null)
                Console.Error.WriteLine($"\n{payload.Text}");
        }
    }
    catch (InvalidDataException ex)
    {
        Console.WriteLine($"Invalid message: {ex.Message}");
        peer.Disconnect();
    }
};

listener.PeerDisconnectedEvent += (peer, info) =>
{
    Console.WriteLine("Disconnected from GameServer.");
};

sClient.Start();

var networkTask = Task.Run(async () =>
{
    while (!stop)
    {
        sClient.PollEvents();
        await Task.Delay(15);
    }
});

while (!stop)
{
    var original = new GameMessage
    {
        Type = MessageType.Ping,
        Payload = []
    };

    byte[] bytes = MessageSerializer.Serialize(original);
    GameMessage decoded = MessageSerializer.Deserialize(bytes);

    Console.WriteLine($"Bytes: {Convert.ToHexString(bytes)}");
    Console.WriteLine($"Type: {decoded.Type}");
    Console.WriteLine($"Version: {decoded.Version}");
    
    Console.WriteLine("Say either GET, POST, PUT, GETALL, DELETE, PATCH, CREATESESSION, LISTSESSIONS, GETSESSION, JOINSESSION, LEAVESESSION, STARTSESSION, ENDSESSION, SEND, or STOP");
    var input = Console.ReadLine();

    switch (input)
    {
        case "GET":
        {
            Console.WriteLine("Please provide the ID:");
            var idString = Console.ReadLine();

            if (!int.TryParse(idString, out var id))
            {
                Console.WriteLine("Invalid ID!");
                break;
            }

            await Get(id);
            break;
        }

        case "POST":
        {
            Console.WriteLine("Please provide the username:");
            var username = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Username cannot be empty!");
                break;
            }

            Console.WriteLine("Please provide the level:");
            var levelString = Console.ReadLine();

            if (!int.TryParse(levelString, out var level))
            {
                Console.WriteLine("Invalid level!");
                break;
            }

            await Post(username, level);
            break;
        }

        case "PUT":
        {
            Console.WriteLine("Please provide the ID");
            var idSt = Console.ReadLine();
            if (!int.TryParse(idSt, out var id))
            {
                Console.WriteLine("Invalid id!");
                break;
            }

            Console.WriteLine("Please provide the username:");
            var username = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Username cannot be empty!");
                break;
            }

            Console.WriteLine("Please provide the level:");
            var levelString = Console.ReadLine();

            if (!int.TryParse(levelString, out var level))
            {
                Console.WriteLine("Invalid level!");
                break;
            }

            await Put(id, username, level);
            break;
        }

        case "GETALL":
        {
            await GetAll();
            break;
        }

        case "DELETE":
        {
            Console.WriteLine("Please provide the ID");
            var ids = Console.ReadLine();
            if (!int.TryParse(ids, out var id))
            {
                Console.WriteLine("Invalid ID");
                break;
            }

            await Delete(id);
            break;
        }

        case "PATCH":
        {
            Console.WriteLine("Please provide the ID");
            var ids = Console.ReadLine();
            if (!int.TryParse(ids, out var id))
            {
                Console.WriteLine("Invalid ID");
                break;
            }
            Console.WriteLine("Please provide the type. Either Level, or Username. Case Matters.");
            var types = Console.ReadLine();
            if (!Enum.TryParse<PatchType>(types, ignoreCase: false, out var type))
            {
                Console.WriteLine("Invalid Type");
                break;
            }
            PatchValue value;
            switch (type)
            {
                case PatchType.Level:
                {
                    Console.WriteLine("Please provide the new level.");

                    var inputs = Console.ReadLine();

                    if (!int.TryParse(inputs, out var level) || level < 0)
                    {
                        Console.WriteLine("Invalid level");
                        break;
                    }

                    value = new PatchValue(level, "");
                    var patch = new Patch(type, value);

                    await Patch(id, patch);
                    break;
                }
                case PatchType.Username:
                {
                    Console.WriteLine("Please provide the new username.");

                    var username = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(username))
                    {
                        Console.WriteLine("Invalid username");
                        break;
                    }

                    value = new PatchValue(0, username);
                    var patch = new Patch(type, value);

                    await Patch(id, patch);
                    break;  
                }
            }

            break;
        }
        
        case "CREATESESSION":
        {
            Console.WriteLine("Session name:");
            var name = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(name))
            {
                Console.WriteLine("Name cannot be empty!");
                break;
            }

            Console.WriteLine("Maximum players (2–100):");
            var maxString = Console.ReadLine();

            if (!int.TryParse(maxString, out var maxPlayers)
                || maxPlayers < 2 || maxPlayers > 100)
            {
                Console.WriteLine("Invalid player limit!");
                break;
            }
            
            Console.WriteLine("IP Address:");
            var ip = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(ip))
            {
                Console.WriteLine("IP cannot be empty!");
                break;
            }
            
            Console.WriteLine("Port:");
            var ports = Console.ReadLine();

            if (!int.TryParse(ports, out var port))
            {
                Console.WriteLine("Invalid port!");
                break;
            }

            await CreateSession(name, maxPlayers, ip, port);
            break;
        }

        case "LISTSESSIONS":
        {
            await ListSessions();
            break;
        }

        case "GETSESSION":
        {
            Console.WriteLine("Session Id:");
            var ints = Console.ReadLine();
            if (!int.TryParse(ints, out var id))
            {
                Console.WriteLine("Invalid id!");
                break;
            }

            await GetSession(id);
            break;
        }

        case "JOINSESSION":
        {
            // 127.0.0.1
            Console.WriteLine("Session Id:");
            var sids = Console.ReadLine();
            if (!int.TryParse(sids, out var sessionId))
            {
                Console.WriteLine("Invalid SessionId!");
                break;
            } 
            Console.WriteLine("Player Id:");
            var pids = Console.ReadLine();
            if (!int.TryParse(pids, out var playerId))
            {
                Console.WriteLine("Invalid PlayerId!");
                break;
            }

            await JoinSession(sessionId, playerId);
            break;
        }

        case "LEAVESESSION":
        {
            Console.WriteLine("Session Id:");
            var sids = Console.ReadLine();
            if (!int.TryParse(sids, out var sessionId))
            {
                Console.WriteLine("Invalid SessionId!");
                break;
            }
            
            Console.WriteLine("Player Id:");
            var pids = Console.ReadLine();
            if (!int.TryParse(pids, out var playerId))
            {
                Console.WriteLine("Invalid PlayerId!");
                break;
            }

            await LeaveSession(sessionId, playerId);
            break;
        }

        case "STARTSESSION":
        {
            Console.WriteLine("Session Id:");
            var sids = Console.ReadLine();
            if (!int.TryParse(sids, out var sessionId))
            {
                Console.WriteLine("Invalid SessionID!");
                break;
            }

            await StartSession(sessionId);
            break;
        }
        
        case "ENDSESSION":
        {
            Console.WriteLine("Session Id:");
            var sids = Console.ReadLine();
            if (!int.TryParse(sids, out var sessionId))
            {
                Console.WriteLine("Invalid SessionID!");
                break;
            }

            await EndSession(sessionId);
            break;
        }

        case "SEND":
        {
            Console.WriteLine("What do you wish to send?");
            var text = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.Error.WriteLine("Invalid Text!");
                break;
            }
            
            SendChatMessage(sClient.FirstPeer, text);
            break;
        }

        case "STOP":
        {
            stop = true;
            sClient.Stop();
            await networkTask;
            break;
        }

        default:
        {
            Console.WriteLine("IDG");
            break;
        }
    }
}

async Task StartSession(int session)
{
    var response = await client.PostAsync(
        $"{url}/api/sessions/{session}/start", null);
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
}

async Task EndSession(int session)
{
    var response = await client.PostAsync(
        $"{url}/api/sessions/{session}/end", null);
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
}

async Task LeaveSession(int session, int player)
{
    var response = await client.DeleteAsync(
        $"{url}/api/sessions/{session}/players/{player}");
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
    
    if (!response.IsSuccessStatusCode)
        return;

    List<NetPeer> peers = new();

    sClient.GetConnectedPeers(peers);
    
    foreach (var netPeer in peers)
    {
        netPeer.Disconnect();
    }

    token = null;
}

async Task JoinSession(int session, int player)
{
    var response = await client.PostAsJsonAsync(
        $"{url}/api/sessions/{session}/join",
        new JoinSessionRequest(player));
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
    
    using var document = JsonDocument.Parse(body);
    var root = document.RootElement;

    string ipAddress = root.GetProperty("serverIPAddress").GetString()!;
    int port = root.GetProperty("serverPort").GetInt32();

    Console.WriteLine($"Game server: {ipAddress}:{port}");

    token = root.GetProperty("token").GetString() ?? "";
    
    sClient.Connect(ipAddress, port, "protocol-test-key");
    
}

async Task GetSession(int id) {
    var response = await client.GetAsync(
        $"{url}/api/sessions/{id}");
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
}

async Task ListSessions()
{
    var response = await client.GetAsync(
            $"{url}/api/sessions");
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
}

async Task CreateSession(string name, int maxPlayers, string ip, int port)
{
    var req = new CreateSessionRequest(name, maxPlayers, ip, port);
    var response = await client.PostAsJsonAsync(
        $"{url}/api/sessions",
        req);
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
}

async Task GetAll()
{
    var response = await client.GetAsync(
        $"{url}/api/players");
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
}


async Task Get(int id)
{
    var response = await client.GetAsync(
        $"http://localhost:5157/api/players/{id}");
    
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);
}

async Task Post(string username, int level)
{
    var response = await client.PostAsJsonAsync(
        $"{url}/api/players",
        new Player(username, level));
        
    Console.WriteLine($"Status: {(int)response.StatusCode}");
    string body = await response.Content.ReadAsStringAsync();
    Console.WriteLine(body);

    if (!response.IsSuccessStatusCode)
    {
        return;
    }

    using var document = JsonDocument.Parse(body);
    int id = document.RootElement.GetProperty("id").GetInt32();
    Console.WriteLine(id);
}

async Task Put(int id, string username, int level)
{
    var response = await client.PutAsJsonAsync(
     $"http://localhost:5157/api/players/{id}",
     new Player(username, level));
    
    Console.WriteLine($"Status: {(int)response.StatusCode}"); 
    var body = await response.Content.ReadAsStringAsync(); 
    Console.WriteLine(body);
}

async Task Delete(int id)
{
    var response = await client.DeleteAsync(
        $"{url}/api/players/{id}");
    
    Console.WriteLine($"Status: {(int)response.StatusCode}"); 
    var body = await response.Content.ReadAsStringAsync(); 
    Console.WriteLine(body);
}

async Task Patch(int id, Patch patch)
{
    var options = new JsonSerializerOptions
    {
        IncludeFields = true
    };
    Console.WriteLine(
        System.Text.Json.JsonSerializer.Serialize(patch, options)
    );
    var response = await client.PatchAsJsonAsync(
        $"{url}/api/players/{id}",
        patch,
        options);
    
    Console.WriteLine($"Status: {(int)response.StatusCode}"); 
    var body = await response.Content.ReadAsStringAsync(); 
    Console.WriteLine(body);
}

void SendChatMessage(NetPeer peer, string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return;

    if (text.Length > ProtocolConstants.MaxChatMessageLength)
    {
        Console.Error.WriteLine("Message is too long (max 256 characters)");
        return;
    }

    byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
        new ChatPayload { Text = text });

    byte[] packet = MessageSerializer.Serialize(
        new GameMessage
        {
            Type = MessageType.ChatMessage,
            Payload = payload
        });
    
    peer.Send(packet, DeliveryMethod.ReliableOrdered);
}

// 
//
// response = await client.GetAsync(
//     $"http://localhost:5157/api/players/1");
//     
// Console.WriteLine($"Status: {(int)response.StatusCode}");
// body = await response.Content.ReadAsStringAsync();
// Console.WriteLine(body);

record Player(string Username, int Level = 0);

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

record JoinSessionRequest(int PlayerId);

record CreateSessionRequest(string Name, int MaxPlayers, string ServerIPAddress, int ServerPort, int MinimumPlayers);