# Tanks Game Server

Unity의 Tanks 튜토리얼 프로젝트를 멀티플레이어 게임으로 확장하기 위해 개발한 .NET 기반 비동기 TCP 서버

튜토리얼에서 제공하는 탱크 조작, 포탄, 체력, 카메라, 아트 리소스를 기반으로 사용했고, TCP 네트워크 클라이언트와 독립 서버, 로비, 방, 채팅, 전투 동기화, PostgreSQL 전적 저장, 컨테이너 배포와 부하 테스트를 추가

## 기술 스택

| 구분 | 기술 |
|---|---|
| Runtime | .NET 10 |
| Language | C# |
| Network | TCP, JSON Lines |
| Database | PostgreSQL 18 |
| Database Driver | Npgsql |
| Container | Docker |
| Deployment | Kubernetes on Amazon EKS |

## 문서 구성

1. 프로젝트 기반과 직접 구현 범위
2. 네트워크와 프로토콜
3. Unity 클라이언트 통합
4. 세션과 동시성
5. 로그인과 데이터베이스
6. 로비와 방 관리
7. 전투 동기화
8. 경기 종료와 전적 저장
9. 장애 대응과 입력 검증
10. Docker와 Kubernetes 배포
11. 부하 테스트
12. 한계와 개선 방향

---

## 1. 프로젝트 기반과 직접 구현 범위

### 기반 프로젝트

클라이언트의 게임플레이와 시각 리소스는 Unity Learn의 [Tanks: Make a battle game for web and mobile](https://learn.unity.com/course/tanks-make-a-battle-game-for-web-and-mobile?uv=6) 튜토리얼 기반

포트폴리오에서 직접 구현한 범위와 튜토리얼에서 가져온 범위를 명확히 구분

| 구분 | 내용 |
|---|---|
| 튜토리얼 기반 | 탱크 이동과 발사, 포탄 폭발, 체력, 카메라, 맵, 프리팹, 오디오와 아트 리소스 |
| 직접 구현 | TCP 클라이언트와 서버, JSON 프로토콜, 로그인, 로비, 방, 채팅, 경기 상태, 전투 패킷 중계, PostgreSQL, Docker, Kubernetes, AWS와 부하 테스트 |
| 튜토리얼 코드 확장 | 네트워크 경기 수명주기, 원격 탱크 제어, 발사 재현, 체력 권한과 원격 사망 처리 |

### 원본 씬을 복사해 별도 클라이언트 씬 구성

튜토리얼 데모 씬을 직접 덮어쓰지 않고 `Assets/PortfolioTanks` 아래에 복사한 뒤 네트워크 UI와 컴포넌트를 추가

<details>
<summary><strong>클라이언트 기준 씬 생성 코드 보기</strong></summary>

[`TanksProjectSetup.cs`](../Assets/PortfolioTanks/Editor/TanksProjectSetup.cs)의 핵심 부분

```csharp
private const string SourceScenePath =
    "Assets/_Tanks/Tutorial_Demo/Demo_Scenes/Demo_Game_Moon.unity";

private const string TargetScenePath =
    "Assets/PortfolioTanks/Scenes/TanksClient.unity";

if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath) == null)
{
    AssetDatabase.CopyAsset(
        SourceScenePath,
        TargetScenePath);
}
```

#### 코드 설명

- 튜토리얼 데모 씬을 포트폴리오 전용 씬으로 한 번만 복사
- 도구를 다시 실행해도 이미 수정한 클라이언트 씬을 덮어쓰지 않음
- 복사한 씬에 로그인, 로비, 방, 채팅과 네트워크 경기 UI를 구성

</details>

### 직접 추가한 주요 Unity 코드

| 파일 | 역할 |
|---|---|
| [`TanksNetworkClient.cs`](../Assets/PortfolioTanks/Multiplayer/TanksNetworkClient.cs) | TCP 연결, JSON 명령 송신과 응답 수신 |
| [`TanksChatUI.cs`](../Assets/PortfolioTanks/Multiplayer/TanksChatUI.cs) | 로그인, 로비, 방, 채팅과 경기 화면 전환 |
| [`TanksRoomRowView.cs`](../Assets/PortfolioTanks/Multiplayer/TanksRoomRowView.cs) | 방 목록 표시와 입장 버튼 상태 관리 |
| [`TanksMultiplayerMatch.cs`](../Assets/PortfolioTanks/Multiplayer/TanksMultiplayerMatch.cs) | 이동, 발사, 체력, 사망 동기화와 원격 탱크 보간 |
| [`TanksProjectSetup.cs`](../Assets/PortfolioTanks/Editor/TanksProjectSetup.cs) | 튜토리얼 씬 복사와 필수 참조 검증 |

### 네트워크 기능을 위해 확장한 튜토리얼 코드

| 파일 | 변경 내용 |
|---|---|
| [`GameManager.cs`](../Assets/_Tanks/Scripts/Managers/GameManager.cs) | 서버 메시지로 시작·종료되는 네트워크 경기 흐름 추가 |
| [`TankManager.cs`](../Assets/_Tanks/Scripts/Managers/TankManager.cs) | 로컬 탱크와 원격 탱크를 구분하고 원격 입력 비활성화 |
| [`TankShooting.cs`](../Assets/_Tanks/Scripts/Tank/TankShooting.cs) | 로컬 발사 이벤트와 원격 포탄 재현 기능 추가 |
| [`TankHealth.cs`](../Assets/_Tanks/Scripts/Tank/TankHealth.cs) | 로컬 피해 판정 권한과 원격 체력·사망 반영 기능 추가 |
| [`ShellExplosion.cs`](../Assets/_Tanks/Scripts/Shell/ShellExplosion.cs) | 로컬 소유 탱크만 피해를 계산하고 중복 폭발 피해 방지 |

이 구분은 튜토리얼의 결과물을 그대로 포트폴리오 성과로 주장하지 않고, 튜토리얼 위에 어떤 네트워크와 서버 기능을 설계하고 구현했는지 보여주기 위한 것

튜토리얼 자산과 제3자 리소스의 고지는 저장소의 [`Tanks!_Third-PartyNotice.txt`](../Assets/_Tanks/Tanks!_Third-PartyNotice.txt)에 유지

---

## 2. 네트워크와 프로토콜

### 해결하려는 문제

Unity 클라이언트와 서버가 연결을 유지하면서 이동, 발사, 채팅 같은 메시지를 양방향으로 전달할 수 있어야 함.

게임 트래픽은 연결이 유지되는 형태이므로 HTTP 요청 대신 TCP 서버를 사용. 각 메시지의 끝은 줄바꿈으로 구분하고, 메시지 내용은 JSON으로 표현.

### TCP 연결 수락

서버는 모든 네트워크 인터페이스의 TCP `7777` 포트에서 연결을 기다림.

<details>
<summary><strong>TCP 연결 처리 코드 보기</strong></summary>

[`Program.cs`](../Server/Tanks.Server/Program.cs)의 핵심 부분.

```csharp
TcpListener listener = new(
    IPAddress.Any,
    7777);

async Task AcceptClientsAsync()
{
    listener.Start();

    while (true)
    {
        TcpClient client =
            await listener.AcceptTcpClientAsync();

        client.NoDelay = true;

        ClientSession session = new(client);

        clients.TryAdd(
            session.Id,
            session);

        _ = HandleClientAsync(session);
    }
}
```

#### 코드 설명

- `IPAddress.Any`를 사용해 컨테이너의 모든 네트워크 인터페이스에서 연결을 받음.
- `AcceptTcpClientAsync()`로 스레드를 막지 않고 새 연결을 기다림.
- 연결마다 `ClientSession`을 생성해 네트워크 연결과 플레이어 상태를 함께 관리.
- `_ = HandleClientAsync(session)`으로 현재 클라이언트 처리와 다음 연결 수락을 분리.
- `NoDelay`로 Nagle 알고리즘을 비활성화해 작은 실시간 패킷의 지연을 줄임.

</details>

클라이언트마다 독립적인 비동기 작업이 실행되므로 한 사용자의 처리를 기다리는 동안에도 다른 연결을 계속 받을 수 있음.

### 메시지 경계 처리

TCP는 메시지 단위가 아닌 바이트 스트림을 제공. 따라서 한 번의 읽기가 하나의 완전한 JSON이라는 보장이 없음.

이 서버는 각 JSON 뒤에 줄바꿈을 추가하는 JSON Lines 방식을 사용.

<details>
<summary><strong>JSON Lines 송수신 코드 보기</strong></summary>

[`ClientSession.cs`](../Server/Tanks.Server/ClientSession.cs)의 핵심 부분.

```csharp
public Task<string?> ReadLineAsync()
{
    ThrowIfDisposed();
    return _reader.ReadLineAsync();
}

public async Task SendAsync(ServerMessage message)
{
    string json = Protocol.Serialize(message);
    await _writer.WriteLineAsync(json);
}
```

#### 코드 설명

- 송신자는 `WriteLineAsync()`로 JSON 뒤에 줄바꿈을 추가.
- 수신자는 `ReadLineAsync()`로 줄바꿈까지 읽어 하나의 메시지를 복원.
- 별도의 패킷 길이 헤더가 없어 구현과 디버깅이 단순.

</details>

<details>
<summary><strong>로그인 요청 JSON 보기</strong></summary>

```json
{"type":"login","protocolVersion":1,"loginId":"player-one"}
```

실제 네트워크에서는 위 JSON과 줄바꿈 문자 하나가 함께 전송.

</details>

### JSON 직렬화 규칙

C# 객체와 JSON 사이의 이름 규칙을 한곳에서 관리.

<details>
<summary><strong>JSON 직렬화 설정 코드 보기</strong></summary>

[`Protocol.cs`](../Server/Tanks.Server/Protocol.cs)의 핵심 부분.

```csharp
public const int CurrentVersion = 1;

public static JsonSerializerOptions JsonOptions { get; } = new()
{
    PropertyNamingPolicy =
        JsonNamingPolicy.CamelCase,

    PropertyNameCaseInsensitive = true,

    DefaultIgnoreCondition =
        JsonIgnoreCondition.WhenWritingNull
};
```

#### 코드 설명

- C#의 `LoginId`를 JSON의 `loginId`로 변환.
- 수신할 때는 JSON 속성 이름의 대소문자를 구분하지 않음.
- 값이 없는 선택 필드는 응답 JSON에서 제외해 불필요한 데이터를 줄임.
- `CurrentVersion`으로 클라이언트와 서버가 사용하는 규격을 구분.

</details>

### 프로토콜 버전 검사

클라이언트와 서버의 프로토콜이 달라졌을 때 호환되지 않는 메시지를 그대로 처리하지 않도록 버전을 검사.

<details>
<summary><strong>프로토콜 버전 검증 코드 보기</strong></summary>

```csharp
if (command.ProtocolVersion != 0 &&
    command.ProtocolVersion != Protocol.CurrentVersion)
{
    await SendErrorAsync(
        session,
        $"지원하지 않는 프로토콜 버전입니다. " +
        $"현재 버전: {Protocol.CurrentVersion}");

    return;
}
```

#### 코드 설명

- 현재 서버가 지원하는 버전은 `1`.
- 지원하지 않는 버전은 명령 처리 전에 거부.
- 잘못된 형식의 패킷이 서버의 방이나 경기 상태를 변경하는 것을 막음.

</details>

### 주요 메시지

| 클라이언트 명령 | 서버 응답 또는 동작 |
|---|---|
| `login` | `login_result` |
| `list_rooms` | `room_list` |
| `create_room` | `room_joined` |
| `join_room` | `room_joined`, `room_state` |
| `leave_room` | `left_room` |
| `chat` | 방 참가자에게 채팅 전달 |
| `start_game` | `game_started` |
| `tank_state` | 위치와 회전 전달 |
| `fire` | 포탄 정보 전달 |
| `tank_health` | 체력 정보 전달 |
| `player_dead` | 사망 및 경기 종료 처리 |
| 잘못된 요청 | `error` |

### 이 방식의 장단점

| 장점 | 단점 |
|---|---|
| 연결을 유지하므로 반복 연결 비용이 없다 | TCP 연결 상태를 서버가 계속 관리해야 한다 |
| JSON이라 패킷 내용을 확인하고 디버깅하기 쉽다 | 바이너리 프로토콜보다 패킷 크기가 크다 |
| 줄바꿈으로 메시지 경계를 쉽게 구분한다 | JSON 문자열의 줄바꿈 처리에 주의해야 한다 |
| 프로토콜 버전으로 호환성을 검사한다 | 버전별 메시지 변환 기능은 아직 없다 |

---

## 3. Unity 클라이언트 통합

### 해결하려는 문제

TCP 수신은 비동기로 계속 실행돼야 하지만 Unity 오브젝트와 UI는 메인 스레드에서 변경해야 함. 네트워크 수신 작업에서 Unity API를 직접 호출하면 스레드 안전성 문제가 발생할 수 있음.

### 수신 작업과 Unity 메인 스레드 분리

백그라운드 수신 루프에서는 메시지를 큐에 넣고, Unity의 `Update()`에서 큐를 비우며 이벤트를 발생.

<details>
<summary><strong>메인 스레드 메시지 전달 코드 보기</strong></summary>

[`TanksNetworkClient.cs`](../Assets/PortfolioTanks/Multiplayer/TanksNetworkClient.cs)의 핵심 부분.

```csharp
private readonly ConcurrentQueue<TanksServerMessage>
    receivedMessages = new();

private void Update()
{
    while (receivedMessages.TryDequeue(
               out TanksServerMessage message))
    {
        MessageReceived?.Invoke(message);
    }
}

private async Task ReceiveLoopAsync()
{
    while (IsConnected && reader != null)
    {
        string json = await reader.ReadLineAsync();

        TanksServerMessage message =
            JsonUtility.FromJson<TanksServerMessage>(json);

        receivedMessages.Enqueue(message);
    }
}
```

#### 코드 설명

- `ReceiveLoopAsync()`는 네트워크 응답을 기다리며 Unity 프레임을 막지 않음.
- `ConcurrentQueue`는 수신 작업과 Unity 메인 스레드 사이에서 메시지를 안전하게 전달.
- 실제 UI와 게임 오브젝트 변경은 `Update()`에서 발생한 이벤트를 통해 수행.

</details>

### 클라이언트 동시 송신 직렬화

이동, 발사, 체력 변경이 같은 시점에 발생해도 JSON 문자열이 서로 섞이지 않아야 함.

<details>
<summary><strong>클라이언트 송신 잠금 코드 보기</strong></summary>

```csharp
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
```

#### 코드 설명

- 여러 Unity 이벤트가 동시에 메시지를 보내더라도 하나씩 순서대로 전송.
- `finally`에서 잠금을 해제해 송신 실패 후에도 다음 메시지가 멈추지 않게 함.

</details>

### 튜토리얼 게임 루프를 네트워크 경기로 확장

기존 튜토리얼은 한 컴퓨터에서 라운드 승자를 직접 판정. 네트워크 경기에서는 서버가 `game_started`와 `match_ended`를 보내므로 별도의 경기 수명주기를 추가.

<details>
<summary><strong>네트워크 경기 시작 코드 보기</strong></summary>

[`GameManager.cs`](../Assets/_Tanks/Scripts/Managers/GameManager.cs)의 확장 부분.

```csharp
public void StartNetworkGame(PlayerData[] playerData)
{
    m_RoundNumber = 0;
    m_RoundWinner = null;
    m_GameWinner = null;
    m_IsNetworkMatch = true;
    m_TankData = playerData;
    m_PlayerCount = m_TankData.Length;

    ChangeGameState(GameState.Game);
}

private IEnumerator NetworkGameLoop()
{
    yield return StartCoroutine(RoundStarting());
    EnableTankControl();
    m_TitleText.text = string.Empty;
}
```

#### 코드 설명

- 로컬 라운드 반복과 승리 횟수 판정 대신 서버의 경기 상태를 사용.
- 참가자 배열을 기준으로 로컬 탱크와 원격 탱크를 생성.
- 서버가 보낸 경기 종료 메시지를 받으면 네트워크 경기 오브젝트를 정리.

</details>

---

## 4. 세션과 동시성

### 해결하려는 문제

여러 클라이언트가 동시에 접속하고 로그인하거나 같은 방의 상태를 변경. 동시 요청이 공유 컬렉션을 잘못 변경하거나 한 소켓에 여러 JSON이 섞여 전송되지 않도록 해야 함.

### 연결 목록과 공유 게임 상태 분리

<details>
<summary><strong>서버의 공유 상태 선언 코드 보기</strong></summary>

[`Program.cs`](../Server/Tanks.Server/Program.cs)의 핵심 부분.

```csharp
ConcurrentDictionary<Guid, ClientSession> clients = new();

Dictionary<string, ClientSession> loggedInClients =
    new(StringComparer.OrdinalIgnoreCase);

Dictionary<string, RoomState> rooms =
    new(StringComparer.OrdinalIgnoreCase);

object stateGate = new();
```

#### 코드 설명

- 전체 연결 목록은 접속과 종료가 서로 다른 작업에서 발생하므로 `ConcurrentDictionary`로 관리.
- 로그인과 방 상태는 여러 값을 하나의 원자적인 작업으로 변경해야 하므로 `stateGate` 잠금으로 보호.
- 로그인 ID와 방 이름은 대소문자를 구분하지 않아 유사한 중복 ID와 방 이름을 막음.

</details>

### 한 세션의 동시 전송 방지

채팅, 방 상태, 경기 패킷이 동시에 같은 클라이언트로 전송될 수 있음.

<details>
<summary><strong>서버 세션 송신 코드 보기</strong></summary>

[`ClientSession.cs`](../Server/Tanks.Server/ClientSession.cs)의 핵심 부분.

```csharp
private readonly SemaphoreSlim _sendLock = new(1, 1);

public async Task SendAsync(ServerMessage message)
{
    string json = Protocol.Serialize(message);

    await _sendLock.WaitAsync();
    try
    {
        ThrowIfDisposed();
        await _writer.WriteLineAsync(json);
    }
    finally
    {
        _sendLock.Release();
    }
}
```

#### 코드 설명

- 한 세션에는 항상 하나의 쓰기 작업만 실행.
- 잠금을 기다리는 중 연결이 종료될 수 있으므로 획득 후 종료 여부를 다시 검사.
- 비동기 친화적인 `SemaphoreSlim`을 사용해 대기 중인 스레드를 점유하지 않음.

</details>

### 잠금 안에서는 상태 복사, 네트워크 전송은 잠금 밖에서 수행

<details>
<summary><strong>방 목록 스냅샷 코드 보기</strong></summary>

```csharp
RoomSummary[] summaries;

lock (stateGate)
{
    summaries = rooms.Values
        .Select(room => room.ToSummary())
        .OrderBy(room => room.Name)
        .ToArray();
}

await session.SendAsync(
    new ServerMessage
    {
        Type = MessageType.RoomList,
        Rooms = summaries
    });
```

#### 코드 설명

- 잠금 안에서는 변경되지 않는 배열 복사본만 만듬.
- 느릴 수 있는 소켓 전송은 잠금을 해제한 뒤 실행.
- 특정 클라이언트의 네트워크 지연이 다른 로그인이나 방 요청을 막는 것을 방지.

</details>

### 중복 자원 정리 방지

<details>
<summary><strong>세션 정리 코드 보기</strong></summary>

```csharp
public async ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) != 0)
    {
        return;
    }

    await _sendLock.WaitAsync();
    try
    {
        await _writer.DisposeAsync();
        _reader.Dispose();
        Client.Dispose();
    }
    finally
    {
        _sendLock.Release();
    }
}
```

연결 오류와 정상 종료가 동시에 발생하더라도 `Interlocked.Exchange()`를 통과한 첫 작업만 네트워크 자원을 정리.

</details>

---

## 5. 로그인과 데이터베이스

### 연결 정보와 비밀 분리

데이터베이스 주소와 비밀번호는 코드나 이미지에 넣지 않고 환경 변수로 전달.

<details>
<summary><strong>연결 문자열 로딩 코드 보기</strong></summary>

[`Database.cs`](../Server/Tanks.Server/Database.cs)의 핵심 부분.

```csharp
private const string ConnectionStringEnvironmentVariable =
    "TANKS_DB_CONNECTION_STRING";

string? connectionString =
    Environment.GetEnvironmentVariable(
        ConnectionStringEnvironmentVariable);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "데이터베이스 연결 문자열이 설정되지 않았습니다.");
}

return NpgsqlDataSource.Create(connectionString);
```

#### 코드 설명

- 로컬에서는 PowerShell 환경 변수로 설정.
- Kubernetes에서는 `Secret`의 값을 컨테이너 환경 변수로 주입.
- `NpgsqlDataSource`가 연결 풀을 관리해 요청마다 새 연결을 만드는 비용을 줄임.

</details>

### PostgreSQL 시작 대기

Kubernetes에서 서버와 PostgreSQL이 동시에 시작되면 DB가 아직 연결을 받지 못할 수 있음.

<details>
<summary><strong>지수 백오프 재연결 코드 보기</strong></summary>

```csharp
const int DatabaseConnectionMaxAttempts = 8;

for (int attempt = 1; ; attempt++)
{
    try
    {
        await using NpgsqlConnection connection =
            await dataSource.OpenConnectionAsync();

        break;
    }
    catch (NpgsqlException)
        when (attempt < DatabaseConnectionMaxAttempts)
    {
        int delaySeconds =
            Math.Min(1 << (attempt - 1), 10);

        await Task.Delay(
            TimeSpan.FromSeconds(delaySeconds));
    }
}
```

재시도 간격은 `1, 2, 4, 8, 10...`초로 증가. 일시적인 DB 시작 지연 때문에 서버 컨테이너가 즉시 종료되는 문제를 줄임.

</details>

### 플레이어 조회 또는 생성

<details>
<summary><strong>로그인 UPSERT 쿼리 보기</strong></summary>

[`PlayerRepository.cs`](../Server/Tanks.Server/PlayerRepository.cs)의 핵심 부분.

```csharp
const string sql = """
INSERT INTO players (login_id)
VALUES($1)
ON CONFLICT (login_id)
DO UPDATE SET login_id = EXCLUDED.login_id
RETURNING player_id, login_id, wins, losses;
""";

await using NpgsqlCommand command =
    _dataSource.CreateCommand(sql);

command.Parameters.AddWithValue(loginId);
```

#### 코드 설명

- 첫 로그인이라면 플레이어 행을 생성.
- 이미 존재하는 ID라면 기존 전적을 반환.
- 매개변수 쿼리를 사용해 로그인 ID를 SQL 문자열에 직접 결합하지 않음.
- 조회 후 없으면 생성하는 두 단계 대신 하나의 원자적인 쿼리로 처리.

</details>

### 중복 로그인 방지와 실패 복구

<details>
<summary><strong>중복 로그인 예약 코드 보기</strong></summary>

```csharp
lock (stateGate)
{
    if (loggedInClients.ContainsKey(loginId))
    {
        error = "이미 접속 중인 로그인 ID입니다.";
    }
    else
    {
        loggedInClients.Add(loginId, session);
        session.LoginId = loginId;
    }
}

try
{
    player =
        await playerRepository.GetOrCreateAsync(loginId);
}
catch
{
    lock (stateGate)
    {
        RemoveLoggedInClientUnderLock(session);
        session.LoginId = null;
    }

    await SendErrorAsync(
        session,
        "플레이어 정보를 불러오지 못했습니다.");

    return;
}
```

DB 조회 전에 로그인 ID를 먼저 예약해 동시에 들어온 같은 ID를 차단. DB 작업은 잠금 밖에서 실행하고 실패하면 예약한 로그인 상태를 되돌림.

</details>

---

## 6. 로비와 방 관리

### 방 상태 모델

방은 최대 4명이며 가장 먼저 들어온 참가자가 방장. 경기 중인 방에는 새로 입장할 수 없음.

<details>
<summary><strong>방 입장 규칙 코드 보기</strong></summary>

[`RoomState.cs`](../Server/Tanks.Server/RoomState.cs)의 핵심 부분.

```csharp
public const int MaximumPlayers = 4;

public ClientSession? Host =>
    _players.Count == 0
        ? null
        : _players[0];

public RoomJoinResult TryAddPlayer(
    ClientSession session)
{
    if (session.IsInRoom || _players.Contains(session))
        return RoomJoinResult.AlreadyInRoom;

    if (IsPlaying)
        return RoomJoinResult.MatchInProgress;

    if (IsFull)
        return RoomJoinResult.Full;

    _players.Add(session);
    session.RoomName = Name;

    return RoomJoinResult.Success;
}
```

방 상태 변경과 세션의 `RoomName` 변경을 함께 처리해 서로 다른 상태가 남지 않도록 함.

</details>

### 경기 시작 조건

<details>
<summary><strong>경기 시작 검증 코드 보기</strong></summary>

```csharp
public MatchStartResult TryStartMatch(
    ClientSession requester,
    out string? matchId)
{
    matchId = null;

    if (IsPlaying)
        return MatchStartResult.AlreadyPlaying;

    if (!IsHost(requester))
        return MatchStartResult.NotHost;

    if (PlayerCount < 2)
        return MatchStartResult.WaitingForPlayer;

    IsPlaying = true;
    MatchId = Guid.NewGuid().ToString("N");
    _reportedDeaths.Clear();
    matchId = MatchId;

    return MatchStartResult.Success;
}
```

#### 코드 설명

- 방장만 경기를 시작할 수 있음.
- 최소 2명이 있어야 함.
- 매 경기마다 새로운 `MatchId`를 만들어 이전 경기 패킷과 구분.
- 새로운 경기가 시작될 때 이전 사망 기록을 초기화.

</details>

### 로비 방 목록 방송

방이 생성·변경·삭제되면 로비에 있는 로그인 사용자에게 최신 목록을 전송. 방 안에 있는 사용자는 불필요한 로비 갱신 대상에서 제외.

<details>
<summary><strong>방 목록 방송 코드 보기</strong></summary>

```csharp
lock (stateGate)
{
    recipients = loggedInClients.Values
        .Where(session => !session.IsInRoom)
        .Distinct()
        .ToArray();

    summaries = rooms.Values
        .Select(room => room.ToSummary())
        .OrderBy(room => room.Name)
        .ToArray();
}

await SendManyAsync(
    recipients,
    new ServerMessage
    {
        Type = MessageType.RoomList,
        Rooms = summaries
    });
```

</details>

---

## 7. 전투 동기화

### 동기화 모델

현재 전투는 각 클라이언트가 자신의 탱크 이동, 발사와 체력을 계산하고 서버가 같은 방의 다른 참가자에게 검증된 메시지를 중계하는 구조.

| 데이터 | 처리 방식 |
|---|---|
| 위치와 회전 | 소유 클라이언트가 20Hz로 전송, 원격 클라이언트가 보간 |
| 발사 | 생성 위치, 속도, 피해량과 폭발 정보를 전달해 원격에서 재현 |
| 체력 | 탱크 소유 클라이언트가 계산한 결과 전달 |
| 사망과 경기 종료 | 서버가 중복 사망을 제거하고 생존자 수로 종료 판정 |

### 로컬 탱크 상태 전송

<details>
<summary><strong>20Hz 상태 전송 코드 보기</strong></summary>

[`TanksMultiplayerMatch.cs`](../Assets/PortfolioTanks/Multiplayer/TanksMultiplayerMatch.cs)의 핵심 부분.

```csharp
private const float StateSendInterval = 0.05f;

private void TrySendLocalTankState()
{
    if (Time.unscaledTime < nextStateSendTime)
    {
        return;
    }

    nextStateSendTime =
        Time.unscaledTime + StateSendInterval;

    Transform tankTransform =
        localTank.m_Instance.transform;

    int packetSequence = sequence++;

    _ = networkClient.SendTankStateAsync(
        matchId,
        packetSequence,
        tankTransform.position,
        tankTransform.rotation);
}
```

#### 코드 설명

- `0.05`초마다 전송하므로 목표 전송 빈도는 초당 20회.
- 렌더링 프레임마다 네트워크 패킷을 보내지 않아 트래픽을 제한.
- 각 이동 패킷에 증가하는 `sequence`를 포함.

</details>

### 서버의 오래된 이동 패킷 제거

<details>
<summary><strong>Sequence 검증 코드 보기</strong></summary>

[`Program.cs`](../Server/Tanks.Server/Program.cs)의 이동 패킷 처리 부분.

```csharp
var sequenceKey =
    (context.MatchId, session.Id);

if (lastTankSequences.TryGetValue(
        sequenceKey,
        out int lastSequence) &&
    command.Sequence <= lastSequence)
{
    stalePacket = true;
}
else
{
    lastTankSequences[sequenceKey] =
        command.Sequence;
}

if (stalePacket)
{
    return;
}
```

같은 경기와 세션에서 이미 처리한 번호 이하의 패킷은 중복되거나 오래된 상태로 판단해 조용히 버림. 이동 상태는 오류 응답보다 최신 값 유지가 더 중요하기 때문.

</details>

### 원격 탱크 보간

네트워크 상태를 수신할 때 원격 탱크를 즉시 순간 이동시키지 않고 목표 위치와 회전을 저장한 뒤 매 프레임 보간.

<details>
<summary><strong>위치와 회전 보간 코드 보기</strong></summary>

```csharp
float positionAmount =
    1f - Mathf.Exp(
        -PositionLerpSpeed * Time.deltaTime);

float rotationAmount =
    1f - Mathf.Exp(
        -RotationLerpSpeed * Time.deltaTime);

state.Transform.position =
    Vector3.Lerp(
        state.Transform.position,
        state.TargetPosition,
        positionAmount);

state.Transform.rotation =
    Quaternion.Slerp(
        state.Transform.rotation,
        state.TargetRotation,
        rotationAmount);
```

#### 코드 설명

- 위치는 `Vector3.Lerp`, 회전은 `Quaternion.Slerp`를 사용.
- 지수 형태의 보간 비율을 사용해 프레임 속도가 달라도 비슷한 반응 속도를 유지.
- 현재 구현은 단순 보간이며 지연 보상이나 클라이언트 예측은 포함하지 않음.

</details>

### 원격 발사 재현

튜토리얼의 발사 코드를 로컬 발사와 원격 재현이 같은 포탄 생성 함수를 사용하도록 분리.

<details>
<summary><strong>원격 포탄 재현 코드 보기</strong></summary>

[`TankShooting.cs`](../Assets/_Tanks/Scripts/Tank/TankShooting.cs)의 확장 부분.

```csharp
public void ReplayNetworkFire(
    Vector3 position,
    Vector3 velocity,
    float maxDamage,
    float explosionForce,
    float explosionRadius)
{
    Quaternion rotation =
        velocity.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(
                velocity.normalized)
            : m_FireTransform.rotation;

    SpawnShell(
        position,
        rotation,
        velocity,
        maxDamage,
        explosionForce,
        explosionRadius);
}
```

원격 발사 재현에서는 로컬 발사 이벤트를 다시 발생시키지 않음. 그렇지 않으면 받은 발사가 서버로 다시 전송되는 반복이 생길 수 있음.

</details>

### 피해 판정 권한

<details>
<summary><strong>로컬 피해 판정 권한 코드 보기</strong></summary>

[`TankHealth.cs`](../Assets/_Tanks/Scripts/Tank/TankHealth.cs)의 확장 부분.

```csharp
public bool HasDamageAuthority { get; private set; } = true;

public void SetDamageAuthority(
    bool hasAuthority)
{
    HasDamageAuthority = hasAuthority;
}

public void TakeDamage(float amount)
{
    if (!HasDamageAuthority ||
        m_Dead ||
        m_IsInvincible ||
        amount <= 0f ||
        float.IsNaN(amount) ||
        float.IsInfinity(amount))
    {
        return;
    }

    // 자신의 탱크 피해만 계산한다.
}
```

각 클라이언트는 자신이 소유한 탱크의 피해만 계산, 원격 탱크에는 서버가 중계한 체력 상태만 적용. 여러 클라이언트가 같은 충돌을 각각 계산해 피해가 중복되는 것을 줄이기 위한 방식.

</details>

---

## 8. 경기 종료와 전적 저장

### 중복 사망과 중복 종료 방지

체력 패킷과 사망 패킷이 연속으로 도착하거나 연결 종료와 사망 보고가 동시에 발생해도 한 경기 결과는 한 번만 만들어야 함.

<details>
<summary><strong>경기 종료 결과 생성 코드 보기</strong></summary>

```csharp
ClientSession[] survivors =
    room.GetAlivePlayers();

if (survivors.Length > 1)
{
    return null;
}

string matchId = room.MatchId!;

if (!finalizingMatches.Add(matchId))
{
    return null;
}

string? winner =
    survivors.Length == 1
        ? survivors[0].LoginId
        : null;
```

#### 코드 설명

- `RoomState`의 사망 목록은 같은 사용자의 사망을 한 번만 등록.
- 생존자가 한 명 이하일 때만 종료 결과를 만듬.
- `finalizingMatches`에 `MatchId`를 처음 추가한 요청만 DB 저장과 종료 방송을 수행.
- 경기 시작 당시 참가자 명단을 별도로 보관해 도중에 나간 사용자도 패자 계산에 포함.

</details>

### 승패를 하나의 트랜잭션으로 저장

<details>
<summary><strong>전적 저장 트랜잭션 코드 보기</strong></summary>

[`PlayerRepository.cs`](../Server/Tanks.Server/PlayerRepository.cs)의 핵심 부분.

```csharp
await using NpgsqlConnection connection =
    await _dataSource.OpenConnectionAsync();

await using NpgsqlTransaction transaction =
    await connection.BeginTransactionAsync();

try
{
    // 승자의 wins 증가
    await winnerCommand.ExecuteNonQueryAsync();

    // 모든 패자의 losses 증가
    await loserCommand.ExecuteNonQueryAsync();

    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

승리 횟수만 증가하고 패배 횟수 저장은 실패하는 불완전한 결과를 막기 위해 두 갱신을 하나의 트랜잭션으로 묶음. 갱신된 행 수까지 확인해 존재하지 않는 플레이어가 결과에 포함되는 것도 검사.

</details>

### DB 오류와 게임 상태 분리

전적 저장이 실패해도 메모리의 경기 종료와 참가자 알림은 계속 진행. 응답의 `statsRecorded` 값으로 클라이언트가 저장 성공 여부를 구분할 수 있음.

<details>
<summary><strong>경기 종료 메시지 코드 보기</strong></summary>

```csharp
await SendManyAsync(
    conclusion.Recipients,
    new ServerMessage
    {
        Type = MessageType.MatchEnded,
        MatchId = conclusion.MatchId,
        Winner = conclusion.Winner,
        Losers = conclusion.Losers,
        Reason = conclusion.Reason,
        StatsRecorded = statsRecorded
    });
```

</details>

---

## 9. 장애 대응과 입력 검증

### 잘못된 패킷을 연결 전체 장애로 확대하지 않기

<details>
<summary><strong>패킷 크기와 JSON 오류 처리 코드 보기</strong></summary>

```csharp
const int MaximumPacketLength = 65536;

if (json.Length > MaximumPacketLength)
{
    await SendErrorAsync(
        session,
        "패킷 크기가 너무 큽니다.");

    continue;
}

try
{
    command = Protocol.DeserializeCommand(json);
}
catch (JsonException)
{
    await SendErrorAsync(
        session,
        "올바르지 않은 JSON입니다.");

    continue;
}
```

잘못된 메시지 한 건에는 `error` 응답을 보내고 연결을 유지. 패킷 길이 제한은 지나치게 큰 입력을 역직렬화하는 비용을 제한.

</details>

### 입력 제한

| 항목 | 제한 또는 검증 |
|---|---|
| JSON 패킷 | 최대 65,536자 |
| 로그인 ID | 필수, 최대 32자, 제어 문자 금지 |
| 방 이름 | 필수, 최대 32자, 제어 문자 금지 |
| 채팅 | 필수, 최대 500자 |
| 이동 Sequence | 0 이상, 이전 번호보다 커야 함 |
| 위치·회전·속도·피해량 | `NaN`과 무한대 금지 |
| 피해량·폭발력·폭발 반경 | 음수 금지 |
| 게임 명령 | 현재 방, `MatchId`, 생존 상태 검증 |

### 연결 종료 정리

정상 종료, 소켓 오류와 클라이언트 강제 종료 모두 `finally`에서 같은 정리 경로를 사용.

<details>
<summary><strong>세션 종료 처리 코드 보기</strong></summary>

```csharp
finally
{
    await CleanupSessionAsync(session);
}

lock (stateGate)
{
    departure = RemoveFromRoomUnderLock(
        session,
        "disconnected");

    RemoveLoggedInClientUnderLock(session);
}

clients.TryRemove(session.Id, out _);
await session.DisposeAsync();
```

경기 중 연결이 끊기면 방에서 제거하는 것에 그치지 않고 사망과 경기 종료 조건까지 계산.

</details>

---

## 10. Docker와 Kubernetes 배포

### 멀티 스테이지 Docker 이미지

SDK 이미지에서 빌드한 결과만 작은 Runtime 이미지로 복사.

<details>
<summary><strong>Dockerfile 보기</strong></summary>

[`Dockerfile`](../Server/Tanks.Server/Dockerfile)의 내용.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Tanks.Server.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish Tanks.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

COPY --from=build /app/publish .
EXPOSE 7777

USER $APP_UID
ENTRYPOINT ["dotnet", "Tanks.Server.dll"]
```

#### 코드 설명

- 최종 이미지에 SDK와 소스 코드를 포함하지 않음.
- ASP.NET이 필요 없는 TCP 콘솔 서버이므로 .NET Runtime 이미지를 사용.
- 컨테이너를 기본 비루트 사용자로 실행.

</details>

### 로컬 빌드와 실행

PostgreSQL과 `players` 테이블이 준비돼 있어야 하며 실제 비밀번호는 문서나 Git에 저장하지 않음.

<details>
<summary><strong>로컬 실행 명령 보기</strong></summary>

프로젝트 루트의 PowerShell에서 실행.

```powershell
dotnet build .\Server\Tanks.Server\Tanks.Server.csproj

$env:TANKS_DB_CONNECTION_STRING = "Host=localhost;Port=5432;Database=tanks_game;Username=tanks_app;Password=<PASSWORD>;SSL Mode=Disable"

dotnet run --project .\Server\Tanks.Server\Tanks.Server.csproj
```

실행이 끝나면 현재 PowerShell 세션의 연결 문자열을 제거.

```powershell
Remove-Item Env:TANKS_DB_CONNECTION_STRING
```

</details>

### 로컬 Docker 이미지 실행

<details>
<summary><strong>Docker 빌드와 실행 명령 보기</strong></summary>

```powershell
docker build `
  -t tanks-server:local `
  .\Server\Tanks.Server

docker run --rm `
  -p 7777:7777 `
  -e "TANKS_DB_CONNECTION_STRING=Host=host.docker.internal;Port=5432;Database=tanks_game;Username=tanks_app;Password=<PASSWORD>;SSL Mode=Disable" `
  tanks-server:local
```

`host.docker.internal`은 Docker 컨테이너에서 Windows 호스트의 로컬 PostgreSQL에 접근하기 위해 사용.

</details>

### Kubernetes 상태 검사와 자원 제한

<details>
<summary><strong>게임 서버 컨테이너 설정 보기</strong></summary>

[`20-server.yaml`](../k8s/base/20-server.yaml)의 핵심 부분.

```yaml
env:
  - name: TANKS_DB_CONNECTION_STRING
    valueFrom:
      secretKeyRef:
        name: tanks-db
        key: TANKS_DB_CONNECTION_STRING

startupProbe:
  tcpSocket:
    port: game
  periodSeconds: 2
  failureThreshold: 30

readinessProbe:
  tcpSocket:
    port: game

livenessProbe:
  tcpSocket:
    port: game

resources:
  requests:
    cpu: 250m
    memory: 256Mi
  limits:
    cpu: "1"
    memory: 768Mi
```

#### 코드 설명

- DB 연결 문자열은 Kubernetes Secret에서 가져옴.
- 시작 검사는 DB 재연결 대기 중인 서버를 즉시 재시작하지 않도록 여유.
- 준비 검사를 통과한 Pod만 트래픽을 받음.
- 생존 검사가 실패하면 Kubernetes가 컨테이너를 다시 시작.
- 요청량과 상한을 설정해 스케줄링 기준과 자원 폭주 제한을 제공.

</details>

### 외부 TCP 진입점

<details>
<summary><strong>NLB Service 설정 보기</strong></summary>

```yaml
apiVersion: v1
kind: Service
metadata:
  name: tanks-server
  annotations:
    service.beta.kubernetes.io/aws-load-balancer-scheme: internet-facing
    service.beta.kubernetes.io/aws-load-balancer-nlb-target-type: ip
spec:
  type: LoadBalancer
  loadBalancerClass: service.k8s.aws/nlb
  ports:
    - name: game
      port: 7777
      targetPort: game
      protocol: TCP
```

AWS Load Balancer Controller가 인터넷 공개형 Network Load Balancer를 생성하고 TCP 7777 트래픽을 게임 서버 Pod IP로 전달.

</details>

인프라 전체 구성과 배포 절차는 별도의 [`infrastructure.md`](./infrastructure.md) 문서에서 다룸.

---

## 11. 부하 테스트

### 테스트 도구

별도의 .NET 콘솔 프로그램이 여러 TCP 연결을 만들고 고유 ID로 로그인한 뒤 1초마다 `list_rooms`를 요청.

<details>
<summary><strong>가상 클라이언트 시나리오 코드 보기</strong></summary>

[`LoadTest/Program.cs`](../LoadTest/Tanks.LoadTest/Program.cs)의 핵심 부분.

```csharp
using TcpClient client = new();

await client.ConnectAsync(
    host,
    port,
    cancellationToken);

await writer.WriteLineAsync(loginRequest);
await reader.ReadLineAsync(cancellationToken);

while (!cancellationToken.IsCancellationRequested)
{
    long startedAt = Stopwatch.GetTimestamp();

    await writer.WriteLineAsync(listRoomsRequest);
    string? response =
        await reader.ReadLineAsync(cancellationToken);

    statistics.LatenciesMs.Add(
        Stopwatch.GetElapsedTime(startedAt)
            .TotalMilliseconds);

    await Task.Delay(
        TimeSpan.FromSeconds(1),
        cancellationToken);
}
```

연결은 클라이언트마다 20ms 간격으로 생성해 한순간에 모든 연결이 몰리지 않도록 함.

</details>

### 측정 결과

| 동시 클라이언트 | 시간 | 성공 요청 | 실패 | 처리량 | p50 | p95 | p99 |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 20 | 30초 | 599 | 0 | 19.96 req/s | 12.04ms | 16.52ms | 17.97ms |
| 100 | 60초 | 5,832 | 0 | 97.17 req/s | 12.40ms | 15.39ms | 22.81ms |
| 300 | 60초 | 16,942 | 0 | 282.25 req/s | 12.20ms | 15.42ms | 19.49ms |
| 1,000 | 90초 | 78,606 | 0 | 873.17 req/s | 12.39ms | 16.57ms | 83.62ms |

1,000개 동시 연결까지 요청 실패와 Pod 재시작은 발생하지 않았다. p50과 p95는 비교적 일정했지만 1,000명 테스트의 p99는 `83.62ms`로 증가해 일부 요청의 꼬리 지연이 나타남.

이 결과는 로그인과 로비 조회 시나리오의 측정 결과이며 서버의 최대 한계나 실제 전투 트래픽 성능을 의미하지 않음.

자세한 환경과 제한 사항은 [부하 테스트 결과](./load-test-results.md)에 기록.

---

## 12. 한계와 개선 방향

| 현재 한계 | 개선 방향 |
|---|---|
| 로그인 ID만 사용하고 인증이 없음 | 계정 인증, 세션 토큰과 만료 정책 추가 |
| TCP 통신에 TLS가 없음 | TLS 프록시 또는 애플리케이션 계층 암호화 적용 |
| 방과 경기 상태가 한 서버 프로세스 메모리에 존재 | Redis 같은 공유 상태 저장소 또는 방 단위 샤딩 검토 |
| 게임 서버 Pod가 1개 | 세션 고정, 방 디렉터리와 수평 확장 구조 설계 |
| 이동과 피해가 클라이언트 계산 중심 | 서버 권위 이동·충돌 판정과 치팅 검증 도입 |
| 단순 위치 보간만 사용 | 스냅샷 버퍼, 보간 지연, 예측과 보정 추가 |
| JSON 텍스트 프로토콜 | 트래픽 증가 시 MessagePack 또는 Protobuf 비교 |
| 종료 신호 처리와 연결 드레이닝 부족 | `CancellationToken`, SIGTERM 처리와 graceful shutdown 추가 |
| 자동화 테스트 부족 | 프로토콜 단위 테스트, DB 통합 테스트와 다중 클라이언트 시나리오 추가 |
| 부하 생성기가 한 컴퓨터에 집중 | 여러 리전 또는 여러 인스턴스에서 분산 부하 테스트 |

### 구현을 통해 검증한 내용

-TCP 스트림에는 메시지 경계가 없으므로 JSON Lines 형식으로 메시지를 구분하고, 비동기 수신 루프를 통해 여러 클라이언트 연결을 동시에 처리했다.
-네트워크 수신 작업은 메시지를 ConcurrentQueue에 저장하고, Unity 메인 스레드는 Update()에서 메시지를 꺼내 처리하도록 구성해 스레드 간 안전성을 확보했다.
-로그인·방·경기 상태를 임계 구역으로 보호하고 네트워크 송신은 잠금 밖에서 수행해, 동시 요청으로 인한 상태 손상과 교착 위험을 줄였다.
-경기 패킷의 MatchId로 현재 경기의 요청인지 확인하고 플레이어별 sequence를 비교해 오래되거나 중복된 패킷을 제거했다.
-Npgsql 연결 풀과 UPSERT를 사용해 플레이어 정보를 효율적으로 조회·생성하고, 경기 결과는 트랜잭션으로 처리해 승패 데이터의 일관성을 보장했다.
-서버를 Docker 이미지로 패키징하고 Kubernetes 매니페스트와 Terraform으로 AWS EKS에 배포해, NLB를 통해 외부에서 접속할 수 있는 TCP 서비스를 구성했다.
-실제 AWS NLB를 통해 최대 1,000개의 동시 TCP 연결로 로그인·로비 요청 부하 테스트를 수행했으며, 78,606건의 요청을 실패 없이 처리하고 약 873 req/s와 p95 16.57ms를 기록했다.
