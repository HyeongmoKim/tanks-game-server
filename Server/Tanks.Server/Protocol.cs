using System.Text.Json;
using System.Text.Json.Serialization;

//Json 직렬화 규칙 및 프로토콜 버전
internal static class Protocol
{
    //서버와 클라이언트 버전 호환 확인
    public const int CurrentVersion = 1;

    //파스칼 케이스를 카멜케이스로 변경
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    //Json 문자열을 서버 명령 객체로 바꿈
    public static ClientCommand? DeserializeCommand(string json)
    {
        return JsonSerializer.Deserialize<ClientCommand>(
            json,
            JsonOptions);
    }
     //서버 메시지를 TCP로 전송할 JSON으로 변경   
    public static string Serialize<T>(T message)
        where T : notnull
    {
        return JsonSerializer.Serialize(
            message,
            JsonOptions);
    }
}
// 메시지 종류들을 문자열 상수로 관리
internal static class MessageType
{
    public const string Login = "login";
    public const string LoginResult = "login_result";

    public const string ListRooms = "list_rooms";
    public const string RoomList = "room_list";
    public const string CreateRoom = "create_room";
    public const string JoinRoom = "join_room";
    public const string RoomJoined = "room_joined";
    public const string RoomState = "room_state";
    public const string LeaveRoom = "leave_room";
    public const string LeftRoom = "left_room";

    public const string Chat = "chat";

    public const string StartGame = "start_game";
    public const string GameStarted = "game_started";
    public const string TankState = "tank_state";
    public const string Fire = "fire";
    public const string TankHealth = "tank_health";
    public const string PlayerDead = "player_dead";
    public const string MatchEnded = "match_ended";

    public const string Error = "error";
}

// 클라이언트에서 서버로 보내는 공통 데이터 형식
internal sealed class ClientCommand
{
    // 모든 명령어 공통 사용정보
    public string? Type { get; set; }
    public int ProtocolVersion { get; set; }

    // 로그인,방,채팅에 사용하는 정보
    public string? LoginId { get; set; }
    public string? RoomName { get; set; }
    public string? Message { get; set; }
    public string? MatchId { get; set; }
    // 이동패킷 구분을 위한 순서 번호
    public int Sequence { get; set; }
    // 탱크의 월드 좌표
    public float Px { get; set; }
    public float Py { get; set; }
    public float Pz { get; set; }
    //탱크 회전 각도 값
    public float Rx { get; set; }
    public float Ry { get; set; }
    public float Rz { get; set; }
    public float Rw { get; set; }
    //발사한 포탄 속도
    public float Vx { get; set; }
    public float Vy { get; set; }
    public float Vz { get; set; }
    // 포탄 피해량 및 물리정보
    public float MaxDamage { get; set; }
    public float ExplosionForce { get; set; }
    public float ExplosionRadius { get; set; }
    //체력과 생존 상태
    public float Health { get; set; }
    public bool Alive { get; set; }
}

// 서버에서 클라까지 보내는 모든 공통 데이터 형식
internal sealed class ServerMessage
{
    //생성 후 변경 못하도록 required 와 init을 사용
    public required string Type { get; init; }

    public int? ProtocolVersion { get; init; }
    public bool? Success { get; init; }
    public string? Error { get; init; }

    public string? LoginId { get; init; }
    public int? Wins { get; init; }
    public int? Losses { get; init; }

    public string? RoomName { get; init; }
    //로비의 방 목록에 표시할 요약정보
    public RoomSummary[]? Rooms { get; init; }
    public string[]? Players { get; init; }
    public bool? IsHost { get; init; }

    public string? Sender { get; init; }
    public string? Message { get; init; }

    public string? MatchId { get; init; }
    public string? Winner { get; init; }
    public string? Loser { get; init; }
    public string[]? Losers {get;init;}
    public string? Reason { get; init; }
    public bool? StatsRecorded { get; init; }

    public int? Sequence { get; init; }

    public float? Px { get; init; }
    public float? Py { get; init; }
    public float? Pz { get; init; }

    public float? Rx { get; init; }
    public float? Ry { get; init; }
    public float? Rz { get; init; }
    public float? Rw { get; init; }

    public float? Vx { get; init; }
    public float? Vy { get; init; }
    public float? Vz { get; init; }

    public float? MaxDamage { get; init; }
    public float? ExplosionForce { get; init; }
    public float? ExplosionRadius { get; init; }

    public float? Health { get; init; }
    public bool? Alive { get; init; }
}

internal sealed record RoomSummary(
    string Name,
    int PlayerCount,
    bool IsPlaying);
