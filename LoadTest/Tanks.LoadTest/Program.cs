using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

if (args.Length < 1)
{
    Console.WriteLine(
        "사용법: dotnet run -- <host> [clients] [duration-seconds]");
    return 1;
}

string host = args[0];
int clientCount = ParseNumber(args, 1, 20);
int durationSeconds = ParseNumber(args, 2, 30);

if (clientCount is < 1 or > 2000 ||
    durationSeconds is < 1 or > 3600)
{
    Console.WriteLine("clients 또는 duration 값이 범위를 벗어났습니다.");
    return 1;
}

const int port = 7777;
string runId = Guid.NewGuid().ToString("N")[..6];

LoadStatistics statistics = new();
using CancellationTokenSource cancellation =
    new(TimeSpan.FromSeconds(durationSeconds));

Console.WriteLine($"Host: {host}:{port}");
Console.WriteLine($"Clients: {clientCount}");
Console.WriteLine($"Duration: {durationSeconds}s");
Console.WriteLine();

Stopwatch totalTimer = Stopwatch.StartNew();

Task[] clients = Enumerable.Range(1, clientCount)
    .Select(clientNumber => RunClientAsync(
        host,
        port,
        runId,
        clientNumber,
        statistics,
        cancellation.Token))
    .ToArray();

await Task.WhenAll(clients);
totalTimer.Stop();

double[] latencies = statistics.LatenciesMs.ToArray();
Array.Sort(latencies);

Console.WriteLine();
Console.WriteLine("===== Load test result =====");
Console.WriteLine($"Connections OK : {statistics.ConnectionsSucceeded}");
Console.WriteLine($"Logins OK      : {statistics.LoginsSucceeded}");
Console.WriteLine($"Requests OK    : {statistics.RequestsSucceeded}");
Console.WriteLine($"Requests failed: {statistics.RequestsFailed}");
Console.WriteLine($"Client failures: {statistics.ClientFailures}");
Console.WriteLine(
    $"Throughput     : " +
    $"{statistics.RequestsSucceeded / totalTimer.Elapsed.TotalSeconds:F2} req/s");

if (latencies.Length > 0)
{
    Console.WriteLine($"Latency p50    : {Percentile(latencies, 0.50):F2} ms");
    Console.WriteLine($"Latency p95    : {Percentile(latencies, 0.95):F2} ms");
    Console.WriteLine($"Latency p99    : {Percentile(latencies, 0.99):F2} ms");
}

foreach (string error in statistics.Errors)
{
    Console.WriteLine($"Error: {error}");
}

return statistics.ClientFailures == 0 &&
       statistics.RequestsFailed == 0
    ? 0
    : 1;

static async Task RunClientAsync(
    string host,
    int port,
    string runId,
    int clientNumber,
    LoadStatistics statistics,
    CancellationToken cancellationToken)
{
    try
    {
        // 모든 연결이 정확히 동시에 몰리지 않도록 짧게 분산
        await Task.Delay(clientNumber * 20, cancellationToken);

        using TcpClient client = new();
        client.NoDelay = true;

        await client.ConnectAsync(
            host,
            port,
            cancellationToken);

        Interlocked.Increment(
            ref statistics.ConnectionsSucceeded);

        await using NetworkStream stream = client.GetStream();

        UTF8Encoding utf8 = new(false);

        using StreamReader reader = new(
            stream,
            utf8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);

        await using StreamWriter writer = new(
            stream,
            utf8,
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = true
        };

        string loginId =
            $"load-{runId}-{clientNumber:D5}";

        string loginRequest = JsonSerializer.Serialize(new
        {
            type = "login",
            protocolVersion = 1,
            loginId
        });

        await writer.WriteLineAsync(loginRequest);

        string? loginResponse =
            await reader.ReadLineAsync(cancellationToken);

        if (loginResponse is null ||
            !ResponseHasType(
                loginResponse,
                "login_result",
                requireSuccess: true))
        {
            throw new InvalidOperationException(
                "로그인 응답이 올바르지 않습니다.");
        }

        Interlocked.Increment(
            ref statistics.LoginsSucceeded);

        const string listRoomsRequest =
            """{"type":"list_rooms","protocolVersion":1}""";

        while (!cancellationToken.IsCancellationRequested)
        {
            long startedAt = Stopwatch.GetTimestamp();

            await writer.WriteLineAsync(listRoomsRequest);

            string? response =
                await reader.ReadLineAsync(cancellationToken);

            if (response is null)
            {
                throw new IOException(
                    "서버가 연결을 종료했습니다.");
            }

            double elapsedMilliseconds =
                Stopwatch.GetElapsedTime(startedAt)
                    .TotalMilliseconds;

            if (ResponseHasType(
                    response,
                    "room_list",
                    requireSuccess: false))
            {
                Interlocked.Increment(
                    ref statistics.RequestsSucceeded);

                statistics.LatenciesMs.Add(
                    elapsedMilliseconds);
            }
            else
            {
                Interlocked.Increment(
                    ref statistics.RequestsFailed);
            }

            await Task.Delay(
                TimeSpan.FromSeconds(1),
                cancellationToken);
        }
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
        // 테스트 시간이 끝나 발생한 정상적인 취소
    }
    catch (Exception exception)
    {
        Interlocked.Increment(
            ref statistics.ClientFailures);

        if (statistics.Errors.Count < 5)
        {
            statistics.Errors.Enqueue(
                exception.Message);
        }
    }
}

static bool ResponseHasType(
    string json,
    string expectedType,
    bool requireSuccess)
{
    try
    {
        using JsonDocument document =
            JsonDocument.Parse(json);

        JsonElement root = document.RootElement;

        bool typeMatches =
            root.TryGetProperty(
                "type",
                out JsonElement type) &&
            type.GetString() == expectedType;

        if (!typeMatches)
        {
            return false;
        }

        return !requireSuccess ||
               root.TryGetProperty(
                   "success",
                   out JsonElement success) &&
               success.ValueKind ==
                   JsonValueKind.True;
    }
    catch (JsonException)
    {
        return false;
    }
}

static int ParseNumber(
    string[] arguments,
    int index,
    int defaultValue)
{
    return arguments.Length > index &&
           int.TryParse(arguments[index], out int value)
        ? value
        : defaultValue;
}

static double Percentile(
    double[] sortedValues,
    double percentile)
{
    int index = (int)Math.Ceiling(
        percentile * sortedValues.Length) - 1;

    return sortedValues[
        Math.Clamp(index, 0, sortedValues.Length - 1)];
}

internal sealed class LoadStatistics
{
    public int ConnectionsSucceeded;
    public int LoginsSucceeded;
    public long RequestsSucceeded;
    public long RequestsFailed;
    public int ClientFailures;

    public ConcurrentBag<double> LatenciesMs { get; } = [];
    public ConcurrentQueue<string> Errors { get; } = [];
}