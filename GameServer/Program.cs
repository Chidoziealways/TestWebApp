using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameProtocol;
using LiteNetLib;

var listener = new EventBasedNetListener();
var server = new NetManager(listener);

using var client = new HttpClient();

var authenticatedPeers = new Dictionary<NetPeer, (int PlayerId, int SessionId)>();

string url = "http://localhost:5157";

listener.ConnectionRequestEvent += request =>
{
    request.Accept();
};

listener.PeerDisconnectedEvent += (peer, info) =>
{
    authenticatedPeers.Remove(peer);
    Console.WriteLine("Client disconnected.");
};

listener.PeerConnectedEvent += peer =>
{
    Console.WriteLine($"Client connected: {peer.Address}:{peer.Port}");
};

listener.NetworkReceiveEvent += async (peer, reader, channel, deliveryMethod) =>
{
    try
    {
        byte[] bytes = new byte[reader.AvailableBytes];
        reader.GetBytes(bytes, bytes.Length);

        GameMessage message = MessageSerializer.Deserialize(bytes);

        Console.WriteLine($"Recieved: {message.Type}");

        if (message.Type != MessageType.Auth &&
            !authenticatedPeers.ContainsKey(peer))
        {
            Console.WriteLine("Rejected message from unauthenticated peer.");
            peer.Disconnect();
            return;
        }

        if (message.Type == MessageType.Ping)
        {
            byte[] response = MessageSerializer.Serialize(
                new GameMessage
                {
                    Type = MessageType.Pong,
                    Payload = []
                });

            peer.Send(response, DeliveryMethod.ReliableOrdered);
            Console.WriteLine("Sent: Pong");
        }
        else if (message.Type == MessageType.Auth)
        {
            byte[] payload = message.Payload;
            string token = Encoding.UTF8.GetString(payload);
            
            var response = await client.PostAsJsonAsync(
                $"{url}/api/auth/validate",
                new {token});
            Console.WriteLine($"Status: {(int)response.StatusCode}");
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine(body);
            
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Console.WriteLine("Authentication Failed!");
                peer.Disconnect();
                return;
            }
    
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            int playerId = root.GetProperty("playerId").GetInt32();
            int sessionId = root.GetProperty("sessionId").GetInt32();

            authenticatedPeers[peer] = (playerId, sessionId);
            
            byte[] rPayload =
                Encoding.UTF8.GetBytes($"{playerId}:{sessionId}");

            var reply = MessageSerializer.Serialize(new GameMessage
            {
                Type = MessageType.AuthResult,
                Payload = rPayload
            });
            peer.Send(reply, DeliveryMethod.ReliableOrdered);
            Console.WriteLine($"Authenticated player {playerId} in session {sessionId}");
        }
        else if (message.Type == MessageType.ChatMessage)
        {
            if (!authenticatedPeers.TryGetValue(peer, out var sender))
            {
                Console.Error.WriteLine("Peer not authenticated!");
                peer.Disconnect();
                return;
            }
            
            var response = await client.GetAsync(
                $"{url}/api/sessions/{sender.SessionId}");
            Console.WriteLine($"Status: {(int)response.StatusCode}");
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine(body);
    
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            bool isStarted = root.GetProperty("isStarted").GetBoolean();

            if (!isStarted)
            {
                 var errorP = JsonSerializer.SerializeToUtf8Bytes(
                     new ChatPayload
                     {
                         Text = $"Server hasn't started yet!"
                     });

                byte[] error = MessageSerializer.Serialize(
                    new GameMessage
                    {
                        Type = MessageType.Error,
                        Payload = errorP
                    });
                
                peer.Send(error, DeliveryMethod.ReliableOrdered);
                return;
            }

            if (message.Payload.Length is 0 or > 1100)
            {
                Console.Error.WriteLine("Rejected invalid chat message length!");
                return;
            }

            ChatPayload chat;

            try
            {
                chat = JsonSerializer.Deserialize<ChatPayload>(message.Payload) ??
                       throw new InvalidDataException("Empty chat payload!");
            }
            catch (JsonException)
            {
                Console.Error.WriteLine("Rejected malformed chat message!");
                return;
            }

            if (string.IsNullOrWhiteSpace(chat.Text) ||
                chat.Text.Length > ProtocolConstants.MaxChatMessageLength)
            {
                Console.Error.WriteLine("Rejected invalid chat text!");
                return;
            }

            var outgoingPayload = JsonSerializer.SerializeToUtf8Bytes(
                new ChatPayload
                {
                    Text = $"Player {sender.PlayerId}: {chat.Text}"
                });

            byte[] outgoingMessage = MessageSerializer.Serialize(
                new GameMessage
                {
                    Type = MessageType.ChatMessage,
                    Payload = outgoingPayload
                });

            foreach (var (recipient, identity) in authenticatedPeers.ToArray())
            {
                if (identity.SessionId == sender.SessionId
                    && recipient.ConnectionState == ConnectionState.Connected)
                {
                    recipient.Send(
                        outgoingMessage,
                        DeliveryMethod.ReliableOrdered);
                }
            }
            
            Console.WriteLine($"Session {sender.SessionId}: Player {sender.PlayerId}: {chat.Text}");
        }
    }
    catch (InvalidDataException ex)
    {
        Console.WriteLine($"Invalid Message: {ex.Message}");
        peer.Disconnect();
    }
};

if (!server.Start(9050))
{
    Console.WriteLine("Failed to start server");
    return;
}

var stop = false;

var networkTask = Task.Run(async () =>
{
    while (!stop)
    {
        server.PollEvents();
        await Task.Delay(15);
    }
});

Console.WriteLine("GameServer listening on UDP port 9050.");
Console.WriteLine("Press Enter to stop.");

Console.ReadLine();
stop = true;
await networkTask;
server.Stop();