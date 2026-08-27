using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class TanksNetworkClient : MonoBehaviour
{
    private const int CurrentProtocolVersion = 1;
    [SerializeField]
    private string serverAddress = "127.0.0.1";

    [SerializeField]
    private int serverPort = 7777;

    private readonly ConcurrentQueue<TanksServerMessage> receivedMessages = new();
    private readonly ConcurrentQueue<string> disconnectMessages = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);

    private TcpClient client;
    private StreamReader reader;
    private StreamWriter writer;

    public event Action<TanksServerMessage> MessageReceived;
    public event Action<string> Disconnected;

    public bool IsConnected { get; private set; }

    public string Endpoint => $"{serverAddress}:{serverPort}";

    private void Update()
    {
        while (receivedMessages.TryDequeue(out TanksServerMessage message))
        {
            MessageReceived?.Invoke(message);
        }

        while (disconnectMessages.TryDequeue(out string reason))
        {
            Disconnected?.Invoke(reason);
        }
    }

    public async Task ConnectAsync()
    {
        if (IsConnected)
        {
            return;
        }

        CleanupConnection();
        client = new TcpClient
        {
            NoDelay = true
        };

        try
        {
            await client.ConnectAsync(serverAddress, serverPort);

            NetworkStream stream = client.GetStream();
            UTF8Encoding utf8 = new(false);

            reader = new StreamReader(
                stream,
                utf8,
                false,
                4096,
                true);

            writer = new StreamWriter(
                stream,
                utf8,
                4096,
                true)
            {
                AutoFlush = true
            };

            IsConnected = true;
            _ = ReceiveLoopAsync();

            Debug.Log($"[Tanks] Connected to {Endpoint}.");
        }
        catch
        {
            CleanupConnection();
            throw;
        }
    }

    public Task LoginAsync(string loginId)
    {
        return SendCommandAsync(
            new TanksClientCommand
            {
                type = "login",
                loginId = loginId?.Trim()
            });
    }

    public Task RequestRoomListAsync()
    {
        return SendCommandAsync(
            new TanksClientCommand { type = "list_rooms" });
    }

    public Task CreateRoomAsync(string roomName)
    {
        return SendCommandAsync(
            new TanksClientCommand
            {
                type = "create_room",
                roomName = roomName?.Trim()
            });
    }

    public Task JoinRoomAsync(string roomName)
    {
        return SendCommandAsync(
            new TanksClientCommand
            {
                type = "join_room",
                roomName = roomName?.Trim()
            });
    }

    public Task LeaveRoomAsync()
    {
        return SendCommandAsync(
            new TanksClientCommand { type = "leave_room" });
    }

    public Task SendChatAsync(string message)
    {
        return SendCommandAsync(
            new TanksClientCommand
            {
                type = "chat",
                message = message?.Trim()
            });
    }

    public Task StartGameAsync()
    {
        return SendCommandAsync(
            new TanksClientCommand { type = "start_game" });
    }

    public Task SendTankStateAsync(string matchId,int sequence, Vector3 position, Quaternion rotation)
    {
        return SendCommandAsync(
            new TanksClientCommand{
            type = "tank_state",
            matchId=matchId?.Trim(),
            sequence=sequence,
            px = position.x,
            py = position.y,
            pz = position.z,
            rx = rotation.x,
            ry = rotation.y,
            rz = rotation.z,
            rw = rotation.w});
        }
    public Task SendFireAsync(
    string matchId,
    int sequence,
    Vector3 position,
    Vector3 velocity,
    float maxDamage,
    float explosionForce,
    float explosionRadius)
{
    return SendCommandAsync(
        new TanksClientCommand
        {
            type = "fire",
            matchId = matchId?.Trim(),
            sequence = sequence,
            px = position.x,
            py = position.y,
            pz = position.z,
            vx = velocity.x,
            vy = velocity.y,
            vz = velocity.z,
            maxDamage = maxDamage,
            explosionForce = explosionForce,
            explosionRadius = explosionRadius
        });
}

    public Task SendTankHealthAsync(
        string matchId,
        float health,
        bool alive)
    {
        return SendCommandAsync(
            new TanksClientCommand
            {
                type = "tank_health",
                matchId = matchId?.Trim(),
                health = health,
                alive = alive
            });
    }

    public Task SendPlayerDeadAsync(
        string matchId)
    {
        return SendCommandAsync(
            new TanksClientCommand
            {
                type = "player_dead",
                matchId = matchId?.Trim(),
                alive = false
            });
    }

    public async Task SendCommandAsync(TanksClientCommand command)
    {
        if (command == null)
        {
        throw new ArgumentNullException(nameof(command));
        }

        command.protocolVersion = CurrentProtocolVersion;

        if (!IsConnected || writer == null)
        {
            throw new InvalidOperationException(
                "The client is not connected to the server.");
        }

        string json = JsonUtility.ToJson(command);

        await sendLock.WaitAsync();
        try
        {
            await writer.WriteLineAsync(json);
        }
        finally
        {
            sendLock.Release();
        }
    }

    public void Disconnect()
    {
        IsConnected = false;
        CleanupConnection();
    }

    private async Task ReceiveLoopAsync()
    {
        string reason = "The server closed the connection.";

        try
        {
            while (IsConnected && reader != null)
            {
                string json = await reader.ReadLineAsync();
                if (json == null)
                {
                    break;
                }

                TanksServerMessage message =
                    JsonUtility.FromJson<TanksServerMessage>(json);

                if (message == null || string.IsNullOrWhiteSpace(message.type))
                {
                    Debug.LogWarning($"[Tanks] Ignored server JSON: {json}");
                    continue;
                }

                Debug.Log($"[Tanks] Received: {message.type}");
                receivedMessages.Enqueue(message);
            }
        }
        catch (ObjectDisposedException)
        {
            reason = "Disconnected from the server.";
        }
        catch (IOException exception)
        {
            reason = exception.Message;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            if (IsConnected)
            {
                IsConnected = false;
                CleanupConnection();
                disconnectMessages.Enqueue(reason);
            }
        }
    }

    private void CleanupConnection()
    {
        writer?.Dispose();
        reader?.Dispose();
        client?.Dispose();

        writer = null;
        reader = null;
        client = null;
    }

    private void OnDestroy()
    {
        IsConnected = false;
        CleanupConnection();
        sendLock.Dispose();
    }
}
[Serializable]
public sealed class TanksClientCommand
{
    public string type;
    public int protocolVersion;

    public string loginId;
    public string roomName;
    public string message;
    public string matchId;

    public int sequence;

    public float px;
    public float py;
    public float pz;

    public float rx;
    public float ry;
    public float rz;
    public float rw;

    public float vx;
    public float vy;
    public float vz;

    public float maxDamage;
    public float explosionForce;
    public float explosionRadius;

    public float health;
    public bool alive;
}
[Serializable]
public sealed class TanksServerMessage
{
    public string type;
    public int protocolVersion;
    public bool success;
    public string error;

    public string loginId;
    public int wins;
    public int losses;

    public string roomName;
    public TanksRoomSummary[] rooms;
    public string[] players;
    public bool isHost;

    public string sender;
    public string message;

    public string matchId;
    public string winner;
    public string loser;
    public string[] losers;
    public string reason;
    public bool statsRecorded;

    public int sequence;

    public float px;
    public float py;
    public float pz;

    public float rx;
    public float ry;
    public float rz;
    public float rw;

    public float vx;
    public float vy;
    public float vz;

    public float maxDamage;
    public float explosionForce;
    public float explosionRadius;

    public float health;
    public bool alive;
}

[Serializable]
public sealed class TanksRoomSummary
{
    public string name;
    public int playerCount;
    public bool isPlaying;
}
