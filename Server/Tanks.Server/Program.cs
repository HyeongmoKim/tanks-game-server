using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;

//수신한 JSON 한 줄과 사용자가 입력하는 문자열의 최대 길이
const int MaximumPacketLength = 65536;
const int MaximumLoginIdLength = 32;
const int MaximumRoomNameLength = 32;
const int MaximumChatLength = 500;

//환경 변수의 연결 문자열로 PostgreSQL 연결 풀을 생성하고 서버 종료 시 비동기로 정리
await using NpgsqlDataSource dataSource =
    Database.CreateDataSource();
//실제 연결을 한 번 열어 데이터베이스 설정 오류를 서버 시작 단계에서 확인
await using (NpgsqlConnection connection =
             await dataSource.OpenConnectionAsync())
{
    Console.WriteLine("PostgreSQL 연결 성공");
}

//플레이어 조회, 생성, 전적 저장을 담당하는 Repository 생성
PlayerRepository playerRepository = new(dataSource);

TcpListener listener = new(
    IPAddress.Any,
    7777);

//접속과 종료가 여러 비동기 작업에서 발생하므로 동시성 컬렉션에 전체 세션 저장
ConcurrentDictionary<Guid, ClientSession> clients = new();

Dictionary<string, ClientSession> loggedInClients =
    new(StringComparer.OrdinalIgnoreCase);

//방 이름으로 방 상태를 찾으며 대소문자는 구분하지 않음
Dictionary<string, RoomState> rooms =
    new(StringComparer.OrdinalIgnoreCase);

//경기 도중 퇴장한 사람도 최종 패자에 포함하기 위해 시작 당시 참가자 명단 저장
Dictionary<string, string[]> matchParticipants =
    new(StringComparer.Ordinal);

HashSet<string> finalizingMatches =
    new(StringComparer.Ordinal);

//경기와 세션별 마지막 이동 패킷 순서를 저장해 오래된 위치 패킷 구분
Dictionary<(string MatchId, Guid SessionId), int>
    lastTankSequences = new();

//로그인, 방, 경기 공유 상태를 여러 클라이언트가 동시에 변경하지 못하도록 보호
object stateGate = new();

//서버가 종료될 때까지 클라이언트 접속을 계속 수락
await AcceptClientsAsync();

//새 클라이언트를 계속 수락하고 각 연결의 처리를 독립적인 작업으로 실행
async Task AcceptClientsAsync()
{
    listener.Start();

    Console.WriteLine("서버가 0.0.0.0:7777에 열림");

    while (true)
    {
        TcpClient client =
            await listener.AcceptTcpClientAsync();

        //작은 실시간 패킷이 TCP 버퍼에 모여 지연되지 않도록 Nagle 알고리즘 비활성화
        client.NoDelay = true;

        ClientSession session = new(client);

        //연결마다 생성된 세션 ID를 키로 전체 접속 목록에 등록
        clients.TryAdd(
            session.Id,
            session);

        Console.WriteLine(
            $"클라이언트 접속: {session.RemoteEndPoint}, " +
            $"현재 {clients.Count}명");

        //현재 클라이언트 처리를 기다리지 않고 다음 클라이언트 접속을 계속 수락
        //처리 중 발생하는 예외와 정리는 HandleClientAsync 내부에서 담당
        _ = HandleClientAsync(session);
    }
}

//한 클라이언트가 보내는 줄 단위 JSON 명령을 연결 종료까지 순서대로 처리
async Task HandleClientAsync(ClientSession session)
{
    try
    {
        while (true)
        {
            string? json =
                await session.ReadLineAsync();

            if (json is null)
            {
                break;
            }

            //지나치게 긴 JSON은 역직렬화하지 않고 거부
            if (json.Length > MaximumPacketLength)
            {
                await SendErrorAsync(
                    session,
                    "패킷 크기가 너무 큽니다.");

                continue;
            }

            ClientCommand? command;

            //잘못된 JSON 한 건에는 오류를 보내고 연결을 유지한 채 다음 명령 처리
            try
            {
                command =
                    Protocol.DeserializeCommand(json);
            }
            catch (Exception exception)
                when (exception is JsonException
                    or NotSupportedException)
            {
                await SendErrorAsync(
                    session,
                    "올바르지 않은 JSON입니다.");

                continue;
            }

            if (command is null ||
                string.IsNullOrWhiteSpace(command.Type))
            {
                await SendErrorAsync(
                    session,
                    "명령 종류가 없습니다.");

                continue;
            }

            await DispatchCommandAsync(
                session,
                command);
        }
    }
    //입출력, 소켓, 자원 정리 예외는 일반적인 연결 종료 과정에서도 발생하므로 무시
    catch (IOException)
    {
    }
    catch (SocketException)
    {
    }
    catch (ObjectDisposedException)
    {
    }
    catch (Exception exception)
    {
        Console.WriteLine(
            $"클라이언트 처리 오류: {exception}");
    }
    //정상 종료와 예외 종료 모두 방, 로그인, 소켓 상태를 반드시 정리
    finally
    {
        await CleanupSessionAsync(session);
    }
}

//모든 명령의 공통 조건을 검사하고 명령 종류에 맞는 처리 함수로 전달
async Task DispatchCommandAsync(
    ClientSession session,
    ClientCommand command)
{
    //버전이 지정된 경우 현재 서버 버전과 일치해야 하며 0은 필드가 생략된 기본값으로 허용
    if (command.ProtocolVersion != 0 &&
        command.ProtocolVersion != Protocol.CurrentVersion)
    {
        await SendErrorAsync(
            session,
            $"지원하지 않는 프로토콜 버전입니다. " +
            $"현재 버전: {Protocol.CurrentVersion}");

        return;
    }

    //공백과 대소문자 차이로 같은 명령이 다르게 처리되지 않도록 명령 이름 정규화
    string commandType =
        command.Type!.Trim().ToLowerInvariant();

    if (commandType != MessageType.Login &&
        !session.IsLoggedIn)
    {
        await SendErrorAsync(
            session,
            "먼저 로그인해야 합니다.");

        return;
    }

    //검증을 통과한 명령을 종류에 맞는 처리 함수로 분배
    switch (commandType)
    {
        case MessageType.Login:
            await LoginAsync(session, command);
            break;

        case MessageType.ListRooms:
            await ListRoomsAsync(session);
            break;

        case MessageType.CreateRoom:
            await CreateRoomAsync(session, command);
            break;

        case MessageType.JoinRoom:
            await JoinRoomAsync(session, command);
            break;

        case MessageType.LeaveRoom:
            await LeaveRoomAsync(session);
            break;

        case MessageType.Chat:
            await ChatAsync(session, command);
            break;

        case MessageType.StartGame:
            await StartGameAsync(session);
            break;

        case MessageType.TankState:
            await RelayTankStateAsync(session, command);
            break;

        case MessageType.Fire:
            await RelayFireAsync(session, command);
            break;

        case MessageType.TankHealth:
            await RelayTankHealthAsync(session, command);
            break;

        case MessageType.PlayerDead:
            await PlayerDeadAsync(session, command);
            break;

        default:
            await SendErrorAsync(
                session,
                $"알 수 없는 명령입니다: {commandType}");
            break;
    }
}

//로그인 ID를 검증하고 중복 접속을 막은 뒤 DB에서 플레이어 전적 조회
async Task LoginAsync(
    ClientSession session,
    ClientCommand command)
{
    //앞뒤 공백을 제거한 값을 실제 로그인 ID로 사용
    string loginId =
        command.LoginId?.Trim() ?? string.Empty;

    if (loginId.Length == 0)
    {
        await SendErrorAsync(
            session,
            "로그인 ID를 입력해야 합니다.");

        return;
    }

    if (loginId.Length > MaximumLoginIdLength)
    {
        await SendErrorAsync(
            session,
            $"로그인 ID는 {MaximumLoginIdLength}자 이하여야 합니다.");

        return;
    }

    if (loginId.Any(char.IsControl))
    {
        await SendErrorAsync(
            session,
            "로그인 ID에 제어 문자를 사용할 수 없습니다.");

        return;
    }

    string? error = null;

    //같은 ID의 동시 로그인을 막기 위해 DB 조회 전에 메모리 로그인 상태를 먼저 등록
    lock (stateGate)
    {
        if (session.IsLoggedIn)
        {
            error = "이미 로그인한 연결입니다.";
        }
        else if (loggedInClients.ContainsKey(loginId))
        {
            error = "이미 접속 중인 로그인 ID입니다.";
        }
        else
        {
            loggedInClients.Add(
                loginId,
                session);

            session.LoginId = loginId;
        }
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }

    Player player;

    //DB 작업 중 다른 서버 상태 처리가 멈추지 않도록 잠금을 해제한 뒤 조회
    try
    {
        player =
            await playerRepository.GetOrCreateAsync(
                loginId);
    }
    //DB 조회 실패 시 먼저 등록했던 로그인 상태를 원래대로 복구
    catch (Exception exception)
    {
        lock (stateGate)
        {
            RemoveLoggedInClientUnderLock(session);
            session.LoginId = null;
        }

        Console.WriteLine(
            $"로그인 DB 오류: {exception.Message}");

        await SendErrorAsync(
            session,
            "플레이어 정보를 불러오지 못했습니다.");

        return;
    }

    //로그인 성공 여부와 DB에서 읽은 승리·패배 전적을 클라이언트에 전달
    await session.SendAsync(
        new ServerMessage
        {
            Type = MessageType.LoginResult,
            ProtocolVersion = Protocol.CurrentVersion,
            Success = true,
            LoginId = player.LoginId,
            Wins = player.Wins,
            Losses = player.Losses
        });

    Console.WriteLine(
        $"로그인 완료: {player.LoginId}");
}
//현재 방 목록을 이름순으로 정렬해 요청한 클라이언트에 전달
async Task ListRoomsAsync(
    ClientSession session)
{
    RoomSummary[] summaries;

    //전송 도중 방 목록이 바뀌어도 안전하도록 잠금 안에서 요약 복사본 생성
    lock (stateGate)
    {
        summaries = rooms.Values
            .Select(room => room.ToSummary())
            .OrderBy(
                room => room.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    await session.SendAsync(
        new ServerMessage
        {
            Type = MessageType.RoomList,
            Rooms = summaries
        });
}

//방 이름을 검증하고 새 방 생성과 생성자의 입장을 함께 처리
async Task CreateRoomAsync(
    ClientSession session,
    ClientCommand command)
{
    string roomName =
        command.RoomName?.Trim() ?? string.Empty;

    if (!IsValidRoomName(
            roomName,
            out string? validationError))
    {
        await SendErrorAsync(
            session,
            validationError!);

        return;
    }

    RoomSnapshot? snapshot = null;
    string? error = null;

    //중복 방 확인, 방 생성, 참가자 등록 사이에 다른 요청이 끼어들지 않도록 보호
    lock (stateGate)
    {
        if (session.IsInRoom)
        {
            error = "이미 방에 들어가 있습니다.";
        }
        else if (rooms.ContainsKey(roomName))
        {
            error = "같은 이름의 방이 이미 있습니다.";
        }
        else
        {
            RoomState room = new(roomName);

            RoomJoinResult result =
                room.TryAddPlayer(session);

            if (result != RoomJoinResult.Success)
            {
                error = "방을 생성하지 못했습니다.";
            }
            else
            {
                rooms.Add(
                    room.Name,
                    room);
                //잠금 밖에서 메시지를 보낼 수 있도록 현재 방 상태와 수신자 목록 복사
                snapshot =
                    CaptureRoomUnderLock(room);
            }
        }
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }

    await session.SendAsync(
        CreateRoomMessage(
            MessageType.RoomJoined,
            snapshot!,
            session));

    await BroadcastLobbyRoomListAsync();
}

//방을 찾아 입장 조건을 검사하고 성공하면 참가자들에게 최신 방 상태 전달
async Task JoinRoomAsync(
    ClientSession session,
    ClientCommand command)
{
    string roomName =
        command.RoomName?.Trim() ?? string.Empty;

    if (roomName.Length == 0)
    {
        await SendErrorAsync(
            session,
            "방 이름을 입력해야 합니다.");

        return;
    }

    RoomSnapshot? snapshot = null;
    string? error = null;

    lock (stateGate)
    {
        if (session.IsInRoom)
        {
            error = "이미 방에 들어가 있습니다.";
        }
        else if (!rooms.TryGetValue(
                     roomName,
                     out RoomState? room))
        {
            error = "방을 찾을 수 없습니다.";
        }
        else
        {
            //RoomState의 입장 결과를 클라이언트가 이해할 오류 메시지로 변환
            RoomJoinResult result =
                room.TryAddPlayer(session);

            error = result switch
            {
                RoomJoinResult.Success => null,
                RoomJoinResult.AlreadyInRoom =>
                    "이미 방에 들어가 있습니다.",
                RoomJoinResult.MatchInProgress =>
                    "경기가 진행 중인 방입니다.",
                RoomJoinResult.Full =>
                    "방의 인원이 가득 찼습니다.",
                _ => "방에 입장하지 못했습니다."
            };

            if (result == RoomJoinResult.Success)
            {
                snapshot =
                    CaptureRoomUnderLock(room);
            }
        }
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }
    //새 참가자에게 입장 결과와 현재 참가자 목록 전달
    await session.SendAsync(
        CreateRoomMessage(
            MessageType.RoomJoined,
            snapshot!,
            session));
    //새 참가자는 방금 같은 상태를 받았으므로 제외하고 기존 참가자에게 상태 방송
    await SendRoomStateAsync(
        snapshot!,
        session);

    await BroadcastLobbyRoomListAsync();
}

//참가자를 방에서 제거하고 경기 중이면 사망과 경기 종료 결과까지 계산
async Task LeaveRoomAsync(
    ClientSession session)
{
    RoomDeparture? departure;
    //참가자 제거와 경기 상태 판정을 하나의 공유 상태 변경으로 처리
    lock (stateGate)
    {
        departure =
            RemoveFromRoomUnderLock(
                session,
                "left_room");
    }

    if (departure is null)
    {
        await SendErrorAsync(
            session,
            "현재 들어가 있는 방이 없습니다.");

        return;
    }

    await TrySendAsync(
        session,
        new ServerMessage
        {
            Type = MessageType.LeftRoom,
            Success = true,
            RoomName = departure.RoomName
        });
    //남은 참가자에게 사망, 방 상태, 경기 종료 알림을 필요한 순서대로 전송
    await PublishDepartureAsync(departure);
}

//방 안에서만 채팅을 허용하고 같은 방의 모든 참가자에게 메시지 중계
async Task ChatAsync(
    ClientSession session,
    ClientCommand command)
{
    string message =
        command.Message?.Trim() ?? string.Empty;

    if (message.Length == 0)
    {
        await SendErrorAsync(
            session,
            "채팅 메시지가 비어 있습니다.");

        return;
    }

    if (message.Length > MaximumChatLength)
    {
        await SendErrorAsync(
            session,
            $"채팅은 {MaximumChatLength}자 이하여야 합니다.");

        return;
    }

    ClientSession[] recipients =
        Array.Empty<ClientSession>();

    string? roomName = null;
    string? error = null;
    //전송 중 참가자 목록이 바뀌어도 안전하도록 잠금 안에서 수신자 목록 복사
    lock (stateGate)
    {
        RoomState? room =
            GetRoomUnderLock(session);

        if (room is null)
        {
            error = "방에 들어간 뒤 채팅할 수 있습니다.";
        }
        else
        {
            roomName = room.Name;
            recipients =
                room.GetPlayersSnapshot();
        }
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }
    //송신자를 포함한 현재 방 참가자 모두에게 같은 채팅 메시지 전달
    await SendManyAsync(
        recipients,
        new ServerMessage
        {
            Type = MessageType.Chat,
            RoomName = roomName,
            Sender = session.LoginId,
            Message = message
        });
}

//방장 여부, 최소 인원, 기존 경기 상태를 검사하고 새 경기 시작
async Task StartGameAsync(
    ClientSession session)
{
    RoomSnapshot? snapshot = null;
    string? matchId = null;
    string? error = null;

    lock (stateGate)
    {
        RoomState? room =
            GetRoomUnderLock(session);

        if (room is null)
        {
            error = "방에 들어가 있지 않습니다.";
        }
        else
        {
            //시작 조건을 검사하고 성공하면 이전 경기와 구분할 새로운 MatchId 생성
            MatchStartResult result =
                room.TryStartMatch(
                    session,
                    out matchId);

            error = result switch
            {
                MatchStartResult.Success => null,
                MatchStartResult.NotHost =>
                    "방장만 경기를 시작할 수 있습니다.",
                MatchStartResult.WaitingForPlayer =>
                    "경기를 시작하려면 2명 이상 필요합니다.",
                MatchStartResult.AlreadyPlaying =>
                    "이미 경기가 진행 중입니다.",
                _ => "경기를 시작하지 못했습니다."
            };

            if (result == MatchStartResult.Success)
            {
                snapshot =
                    CaptureRoomUnderLock(room);

                //경기 중 퇴장한 사람도 결과에 포함하도록 시작 당시 참가자 명단을 별도로 복사
                matchParticipants[matchId!] =
                    (string[])snapshot.Players.Clone();
            }
        }
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }
    //모든 참가자가 같은 MatchId와 참가자 순서로 게임을 시작하도록 메시지 방송
    await SendManyAsync(
        snapshot!.Recipients,
        new ServerMessage
        {
            Type = MessageType.GameStarted,
            RoomName = snapshot.RoomName,
            MatchId = matchId,
            Players = snapshot.Players
        });

    await BroadcastLobbyRoomListAsync();
}

//탱크의 위치와 회전을 검증한 뒤 현재 경기의 다른 참가자에게 전달
async Task RelayTankStateAsync(
    ClientSession session,
    ClientCommand command)
{
    //음수 sequence와 NaN 또는 Infinity가 포함된 위치·회전값 거부
    if (command.Sequence < 0 ||
        !AllFinite(
            command.Px,
            command.Py,
            command.Pz,
            command.Rx,
            command.Ry,
            command.Rz,
            command.Rw))
    {
        await SendErrorAsync(
            session,
            "올바르지 않은 탱크 상태입니다.");

        return;
    }

    GameplayContext? context;
    string? error;
    bool stalePacket = false;

    //방 상태 확인과 마지막 sequence 갱신을 하나의 잠금 안에서 처리
    lock (stateGate)
    {
        context =
            GetGameplayContextUnderLock(
                session,
                command.MatchId,
                out error);

        if (context is not null)
        {
            var sequenceKey =
                (context.MatchId, session.Id);
            //마지막으로 처리한 값 이하의 sequence는 중복되거나 오래된 패킷이므로 무시
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
        }
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }
    //이동 상태는 최신 값만 중요하므로 오래된 패킷에는 오류를 보내지 않고 버림
    if (stalePacket)
    {
        return;
    }
    //발신자를 제외한 참가자에게 인증된 세션의 LoginId와 탱크 상태 전달
    await SendManyAsync(
        context!.Recipients,
        new ServerMessage
        {
            Type = MessageType.TankState,
            RoomName = context.RoomName,
            MatchId = context.MatchId,
            LoginId = session.LoginId,
            Sequence = command.Sequence,
            Px = command.Px,
            Py = command.Py,
            Pz = command.Pz,
            Rx = command.Rx,
            Ry = command.Ry,
            Rz = command.Rz,
            Rw = command.Rw
        });
}

//포탄의 생성 위치, 속도, 피해량과 폭발 정보를 다른 참가자에게 중계
async Task RelayFireAsync(
    ClientSession session,
    ClientCommand command)
{
    //발사 수치는 모두 유한해야 하며 피해량, 폭발력, 폭발 반경은 음수가 될 수 없음
    if (!AllFinite(
            command.Px,
            command.Py,
            command.Pz,
            command.Vx,
            command.Vy,
            command.Vz,
            command.MaxDamage,
            command.ExplosionForce,
            command.ExplosionRadius) ||
        command.MaxDamage < 0 ||
        command.ExplosionForce < 0 ||
        command.ExplosionRadius < 0)
    {
        //서버는 포탄을 직접 생성하지 않고 검증된 발사 정보를 다른 참가자에게 전달
        await SendErrorAsync(
            session,
            "올바르지 않은 발사 정보입니다.");

        return;
    }

    GameplayContext? context;
    string? error;

    lock (stateGate)
    {
        context =
            GetGameplayContextUnderLock(
                session,
                command.MatchId,
                out error);
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }

    await SendManyAsync(
        context!.Recipients,
        new ServerMessage
        {
            Type = MessageType.Fire,
            RoomName = context.RoomName,
            MatchId = context.MatchId,
            LoginId = session.LoginId,
            Sequence = command.Sequence,
            Px = command.Px,
            Py = command.Py,
            Pz = command.Pz,
            Vx = command.Vx,
            Vy = command.Vy,
            Vz = command.Vz,
            MaxDamage = command.MaxDamage,
            ExplosionForce = command.ExplosionForce,
            ExplosionRadius = command.ExplosionRadius
        });
}

//탱크를 소유한 클라이언트가 계산한 체력과 생존 상태를 다른 참가자에게 전달
async Task RelayTankHealthAsync(
    ClientSession session,
    ClientCommand command)
{
    if (!float.IsFinite(command.Health))
    {
        await SendErrorAsync(
            session,
            "올바르지 않은 체력 정보입니다.");

        return;
    }

    GameplayContext? context;
    string? error;

    lock (stateGate)
    {
        context =
            GetGameplayContextUnderLock(
                session,
                command.MatchId,
                out error);
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }

    //Alive가 false여도 여기서는 사망 명단을 변경하지 않으며 실제 사망은 player_dead에서 처리
    await SendManyAsync(
        context!.Recipients,
        new ServerMessage
        {
            Type = MessageType.TankHealth,
            RoomName = context.RoomName,
            MatchId = context.MatchId,
            LoginId = session.LoginId,
            Health = command.Health,
            Alive = command.Alive
        });
}

//플레이어 사망을 한 번만 기록하고 생존자가 한 명 이하이면 경기 종료 시도
async Task PlayerDeadAsync(
    ClientSession session,
    ClientCommand command)
{
    ClientSession[] recipients =
        Array.Empty<ClientSession>();

    MatchConclusion? conclusion = null;

    string? roomName = null;
    string? matchId = null;
    string? error = null;

    //중복 사망 확인과 경기 종료 결정을 다른 요청이 끼어들지 못하게 잠금 안에서 처리
    lock (stateGate)
    {
        RoomState? room =
            GetRoomUnderLock(session);

        if (room is null)
        {
            error = "방에 들어가 있지 않습니다.";
        }
        //이전 경기에서 늦게 도착한 사망 메시지가 현재 경기에 적용되지 않도록 ID 확인
        else if (!room.IsCurrentMatch(
                     command.MatchId))
        {
            error = "현재 경기와 MatchId가 다릅니다.";
        }
        else if (finalizingMatches.Contains(
                     room.MatchId!))
        {
            error = "이미 경기 종료를 처리하고 있습니다.";
        }
        //처음 보고된 사망만 기록하여 같은 플레이어의 중복 사망 처리 방지
        else if (!room.TryReportDeath(session))
        {
            error = "이미 사망 처리된 플레이어입니다.";
        }
        else
        {
            roomName = room.Name;
            matchId = room.MatchId;
            recipients =
                room.GetPlayersSnapshot();
            //사망을 반영한 생존자 수로 경기 종료 여부와 승자·패자 계산
            conclusion =
                TryCreateConclusionUnderLock(
                    room,
                    "last_player_alive");
        }
    }

    if (error is not null)
    {
        await SendErrorAsync(session, error);
        return;
    }
    //사망자를 포함한 현재 방 참가자 모두에게 사망 상태 방송
    await SendManyAsync(
        recipients,
        new ServerMessage
        {
            Type = MessageType.PlayerDead,
            RoomName = roomName,
            MatchId = matchId,
            LoginId = session.LoginId,
            Loser = session.LoginId,
            Reason = "player_dead",
            Alive = false
        });

    if (conclusion is not null)
    {
        await CompleteMatchAsync(conclusion);
    }
}

//stateGate 잠금 안에서 종료 조건을 확인하고 한 번 사용할 경기 결과 생성
MatchConclusion? TryCreateConclusionUnderLock(
    RoomState room,
    string reason)
{
    ClientSession[] survivors =
        room.GetAlivePlayers();

    if (survivors.Length > 1)
    {
        return null;
    }

    string matchId = room.MatchId!;
    //처음 MatchId를 등록한 호출만 종료를 진행해 DB 저장과 종료 방송 중복 방지
    if (!finalizingMatches.Add(matchId))
    {
        return null;
    }

    string[] participants;

    //경기 시작 당시 참가자 목록을 사용하고 없을 때만 현재 방 참가자 목록 사용
    if (!matchParticipants.TryGetValue(
            matchId,
            out participants!))
    {
        participants =
            room.GetPlayerNames();
    }

    string? winner =
        survivors.Length == 1
            ? survivors[0].LoginId
            : null;

    string[] losers =
        winner is null
            ? (string[])participants.Clone()
            : participants
                .Where(loginId =>
                    !string.Equals(
                        loginId,
                        winner,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

    //잠금 밖에서 전적 저장과 종료 방송에 사용할 정보를 하나의 결과로 묶음
    return new MatchConclusion(
        room,
        room.Name,
        matchId,
        winner,
        losers,
        room.GetPlayersSnapshot(),
        reason);
}

//경기 결과를 DB에 저장하고 서버 상태를 정리한 뒤 참가자에게 종료 결과 방송
async Task CompleteMatchAsync(
    MatchConclusion conclusion)
{
    bool statsRecorded = false;

    if (conclusion.Winner is not null &&
        conclusion.Losers.Length > 0)
    {
        try
        {
            await playerRepository.RecordMatchResultAsync(
                conclusion.Winner,
                conclusion.Losers);

            statsRecorded = true;
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"전적 저장 실패: {exception}");
        }
    }

    lock (stateGate)
    {
        if (conclusion.Room.IsCurrentMatch(
                conclusion.MatchId))
        {
            conclusion.Room.FinishMatch();
        }

        matchParticipants.Remove(
            conclusion.MatchId);

        finalizingMatches.Remove(
            conclusion.MatchId);

        var sequenceKeys =
            lastTankSequences.Keys
                .Where(key =>
                    string.Equals(
                        key.MatchId,
                        conclusion.MatchId,
                        StringComparison.Ordinal))
                .ToArray();

        foreach (var sequenceKey in sequenceKeys)
        {
            lastTankSequences.Remove(sequenceKey);
        }

        if (conclusion.Room.IsEmpty &&
            rooms.TryGetValue(
                conclusion.RoomName,
                out RoomState? currentRoom) &&
            ReferenceEquals(
                currentRoom,
                conclusion.Room))
        {
            rooms.Remove(conclusion.RoomName);
        }
    }

    await SendManyAsync(
        conclusion.Recipients,
        new ServerMessage
        {
            Type = MessageType.MatchEnded,
            RoomName = conclusion.RoomName,
            MatchId = conclusion.MatchId,
            Winner = conclusion.Winner,
            Loser =
                conclusion.Losers.Length == 1
                    ? conclusion.Losers[0]
                    : null,
            Losers = conclusion.Losers,
            Reason = conclusion.Reason,
            StatsRecorded = statsRecorded
        });

    await BroadcastLobbyRoomListAsync();
}

//잠금 안에서 플레이어를 방에서 제거하고 잠금 밖에서 알림에 사용할 결과 생성
RoomDeparture? RemoveFromRoomUnderLock(
    ClientSession session,
    string reason)
{
    RoomState? room =
        GetRoomUnderLock(session);

    if (room is null)
    {
        session.RoomName = null;
        return null;
    }

    string roomName = room.Name;
    string loginId = session.LoginId!;

    string? deathMatchId = null;
    bool reportedDeath = false;

    //경기가 진행 중이지 않은 빈 방은 로비 방 목록에서 제거
    if (room.IsPlaying &&
        room.MatchId is not null &&
        !finalizingMatches.Contains(room.MatchId))
    {
        reportedDeath =
            room.TryReportDeath(session);

        if (reportedDeath)
        {
            deathMatchId = room.MatchId;
        }
    }

    if (!room.RemovePlayer(session))
    {
        return null;
    }

    MatchConclusion? conclusion =
        reportedDeath
            ? TryCreateConclusionUnderLock(
                room,
                reason)
            : null;

    RoomSnapshot? snapshot =
        room.IsEmpty
            ? null
            : CaptureRoomUnderLock(room);

    if (room.IsEmpty &&
        !room.IsPlaying)
    {
        rooms.Remove(room.Name);
    }

    return new RoomDeparture(
        roomName,
        loginId,
        deathMatchId,
        reason,
        snapshot?.Recipients
            ?? Array.Empty<ClientSession>(),
        snapshot,
        conclusion);
}

//퇴장 결과에 따라 사망, 방 상태, 경기 종료 또는 로비 목록을 전송
async Task PublishDepartureAsync(
    RoomDeparture departure)
{
    if (departure.DeathMatchId is not null)
    {
        await SendManyAsync(
            departure.RemainingRecipients,
            new ServerMessage
            {
                Type = MessageType.PlayerDead,
                RoomName = departure.RoomName,
                MatchId = departure.DeathMatchId,
                LoginId = departure.LoginId,
                Loser = departure.LoginId,
                Reason = departure.Reason,
                Alive = false
            });
    }

    if (departure.Snapshot is not null)
    {
        await SendRoomStateAsync(
            departure.Snapshot);
    }

    if (departure.Conclusion is not null)
    {
        await CompleteMatchAsync(
            departure.Conclusion);
    }
    else
    {
        await BroadcastLobbyRoomListAsync();
    }
}

//연결이 끝난 세션을 방, 로그인 목록, 전체 접속 목록에서 제거하고 자원 정리
async Task CleanupSessionAsync(
    ClientSession session)
{
    RoomDeparture? departure;

    lock (stateGate)
    {
        departure =
            RemoveFromRoomUnderLock(
                session,
                "disconnected");

        RemoveLoggedInClientUnderLock(session);
    }
    //이탈 알림 전송이 실패해도 세션 제거와 Dispose는 반드시 실행
    try
    {
        if (departure is not null)
        {
            await PublishDepartureAsync(departure);
        }
    }
    finally
    {
        clients.TryRemove(
            session.Id,
            out _);

        await session.DisposeAsync();

        Console.WriteLine(
            $"클라이언트 종료: {session.RemoteEndPoint}, " +
            $"현재 {clients.Count}명");
    }
}

//stateGate 잠금 안에서 현재 세션의 로그인 등록 제거
void RemoveLoggedInClientUnderLock(
    ClientSession session)
{
    if (session.LoginId is null)
    {
        return;
    }

    if (loggedInClients.TryGetValue(
            session.LoginId,
            out ClientSession? currentSession) &&
        ReferenceEquals(
            currentSession,
            session))
    {
        loggedInClients.Remove(
            session.LoginId);
    }
}

//RoomName뿐 아니라 실제 방 참가자 목록에도 세션이 포함되어 있는지 확인
RoomState? GetRoomUnderLock(
    ClientSession session)
{
    if (!session.IsInRoom)
    {
        return null;
    }

    if (!rooms.TryGetValue(
            session.RoomName!,
            out RoomState? room))
    {
        return null;
    }

    return room.Contains(session)
        ? room
        : null;
}
//방 소속, MatchId, 경기 종료 상태, 생존 여부를 검증하고 전달 대상 반환
GameplayContext? GetGameplayContextUnderLock(
    ClientSession session,
    string? requestedMatchId,
    out string? error)
{
    RoomState? room =
        GetRoomUnderLock(session);

    if (room is null)
    {
        error = "방에 들어가 있지 않습니다.";
        return null;
    }

    if (!room.IsCurrentMatch(requestedMatchId))
    {
        error = "현재 경기와 MatchId가 다릅니다.";
        return null;
    }

    if (finalizingMatches.Contains(room.MatchId!))
    {
        error = "경기 종료를 처리하고 있습니다.";
        return null;
    }

    bool isAlive =
        room.GetAlivePlayers()
            .Any(player =>
                ReferenceEquals(
                    player,
                    session));

    if (!isAlive)
    {
        error = "사망한 플레이어는 게임 명령을 보낼 수 없습니다.";
        return null;
    }

    error = null;

    return new GameplayContext(
        room.Name,
        room.MatchId!,
        room.GetOtherPlayers(session));
}

//현재 방 정보와 수신자 목록을 복사해 잠금 밖의 네트워크 전송에 사용
RoomSnapshot CaptureRoomUnderLock(
    RoomState room)
{
    return new RoomSnapshot(
        room.Name,
        room.GetPlayerNames(),
        room.GetPlayersSnapshot(),
        room.Host);
}
//수신자 자신이 방장인지 계산해 개인별 방 상태 메시지 생성
ServerMessage CreateRoomMessage(
    string messageType,
    RoomSnapshot snapshot,
    ClientSession recipient)
{
    return new ServerMessage
    {
        Type = messageType,
        Success = true,
        RoomName = snapshot.RoomName,
        Players = snapshot.Players,
        IsHost =
            ReferenceEquals(
                snapshot.Host,
                recipient)
    };
}
//모든 방 참가자에게 최신 인원과 각자의 방장 여부 전송
async Task SendRoomStateAsync(
    RoomSnapshot snapshot,
    ClientSession? excludedSession = null)
{
    foreach (ClientSession recipient
             in snapshot.Recipients)
    {
        if (ReferenceEquals(
                recipient,
                excludedSession))
        {
            continue;
        }

        await TrySendAsync(
            recipient,
            CreateRoomMessage(
                MessageType.RoomState,
                snapshot,
                recipient));
    }
}
//방 밖에 있는 로그인 사용자에게 이름순으로 정렬한 최신 방 목록 방송
async Task BroadcastLobbyRoomListAsync()
{
    ClientSession[] recipients;
    RoomSummary[] summaries;

    lock (stateGate)
    {
        recipients =
            loggedInClients.Values
                .Where(session =>
                    !session.IsInRoom)
                .Distinct()
                .ToArray();

        summaries =
            rooms.Values
                .Select(room =>
                    room.ToSummary())
                .OrderBy(
                    room => room.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    await SendManyAsync(
        recipients,
        new ServerMessage
        {
            Type = MessageType.RoomList,
            Rooms = summaries
        });
}
//여러 수신자에 대한 전송을 동시에 시작하고 모두 완료될 때까지 대기
async Task SendManyAsync(
    ClientSession[] recipients,
    ServerMessage message)
{
    Task[] tasks =
        recipients
            .Select(recipient =>
                TrySendAsync(
                    recipient,
                    message))
            .ToArray();

    await Task.WhenAll(tasks);
}

//한 클라이언트의 전송 실패가 다른 전송과 서버 처리를 중단하지 않도록 예외 처리
async Task TrySendAsync(
    ClientSession session,
    ServerMessage message)
{
    try
    {
        await session.SendAsync(message);
    }
    catch (Exception exception)
    {
        Console.WriteLine(
            $"전송 실패 {session.RemoteEndPoint}: " +
            $"{exception.Message}");
    }
}

//검증 실패 내용을 공통 Error 메시지 형식으로 해당 세션에 전달
Task SendErrorAsync(
    ClientSession session,
    string error)
{
    return session.SendAsync(
        new ServerMessage
        {
            Type = MessageType.Error,
            Success = false,
            Error = error
        });
}
//방 이름의 빈 값, 최대 길이, 제어 문자를 검사하고 실패 이유 반환
bool IsValidRoomName(
    string roomName,
    out string? error)
{
    if (roomName.Length == 0)
    {
        error = "방 이름을 입력해야 합니다.";
        return false;
    }

    if (roomName.Length > MaximumRoomNameLength)
    {
        error =
            $"방 이름은 {MaximumRoomNameLength}자 이하여야 합니다.";

        return false;
    }

    if (roomName.Any(char.IsControl))
    {
        error =
            "방 이름에 제어 문자를 사용할 수 없습니다.";

        return false;
    }

    error = null;
    return true;
}
//위치, 회전, 속도 등의 값에 NaN이나 Infinity가 포함됐는지 검사
bool AllFinite(
    params float[] values)
{
    return values.All(float.IsFinite);
}

//잠금 시점의 방 정보, 수신 대상과 방장을 묶어 잠금 밖에서 사용
internal sealed record RoomSnapshot(
    string RoomName,
    string[] Players,
    ClientSession[] Recipients,
    ClientSession? Host);

//게임 명령 검증을 통과한 방, 경기 ID와 전달 대상 세션을 묶음
internal sealed record GameplayContext(
    string RoomName,
    string MatchId,
    ClientSession[] Recipients);

//전적 저장, 경기 정리와 종료 방송에 필요한 최종 경기 결과를 묶음
internal sealed record MatchConclusion(
    RoomState Room,
    string RoomName,
    string MatchId,
    string? Winner,
    string[] Losers,
    ClientSession[] Recipients,
    string Reason);

//방 이탈 후 사망, 방 상태와 경기 종료 처리에 필요한 정보를 묶음
internal sealed record RoomDeparture(
    string RoomName,
    string LoginId,
    string? DeathMatchId,
    string Reason,
    ClientSession[] RemainingRecipients,
    RoomSnapshot? Snapshot,
    MatchConclusion? Conclusion);
