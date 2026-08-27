using System.Linq;

//방 입장 시도의 성공 또는 실패 원인을 나타내는 결과
internal enum RoomJoinResult
{
    Success,
    AlreadyInRoom,
    MatchInProgress,
    Full
}
//경기 시작 요청의 성공 또는 실패 원인을 나타내는 결과
internal enum MatchStartResult
{
    Success,
    NotHost,
    WaitingForPlayer,
    AlreadyPlaying
}
//참가자와 경기상태 관리
internal sealed class RoomState
{
    //한 방에 들어갈 수 있는 플레이어 수
    public const int MaximumPlayers = 4;
    //방에 들어온 순서대로 세션 저장
    private readonly List<ClientSession> _players= new(MaximumPlayers);
    //사망처리 저장 문자열 ordinalignorecase로 대소문자 구분 x
    private readonly HashSet<string> _reportedDeaths = new(StringComparer.OrdinalIgnoreCase);

    //빈 방 이름 거부 하고 앞 뒤 공백 제거
    public RoomState(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name=name.Trim();
    }
    //방 상태 실시간 계산용 프로퍼티들
    public string Name {get;}
    public int PlayerCount => _players.Count;
    public bool IsEmpty => _players.Count==0;
    public bool IsFull => _players.Count>=MaximumPlayers;
    //경기 진행중인지와 해당 경기의 ID(이전 경기 패킷이 새 경기에 영향이 없도록)
    public bool IsPlaying{get; private set;}
    public string? MatchId{get; private set;}

    //배열 0번 플레이어가 방장 -> 방 만든사람
    public ClientSession? Host =>
        _players.Count==0 ? null : _players[0];


    //방 입장 시도 안될때는 예외처리
    public RoomJoinResult TryAddPlayer(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsInRoom || _players.Contains(session))
        {
            return RoomJoinResult.AlreadyInRoom;
        }
        if (IsPlaying)
        {
            return RoomJoinResult.MatchInProgress;
        }
        if (IsFull)
        {
            return RoomJoinResult.Full;
        }
        //참가자 목록과 세션 방 이름 같이 설정
        _players.Add(session);
        session.RoomName = Name;
        return RoomJoinResult.Success;
    }
    
    //참가자 방에서 제거 후 세션 방 상태 초기화
    public bool RemovePlayer (ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_players.Remove(session))
        {
            return false;
        }
        if (string.Equals(session.RoomName, Name, StringComparison.OrdinalIgnoreCase))
        {
            session.RoomName = null;
        }
        return true;
    }
    //해당 플레이어가 현재 방 참가자인지 확인
    public bool Contains(ClientSession session)
    {
        return _players.Contains(session);
    }
    //같은 클라이언트세션 객체인지 확인
    public bool IsHost(ClientSession session)
    {
        return ReferenceEquals(Host,session);
    }
    //이동,발사,체력 전달할때 모든 참가자에게 반환
    public ClientSession[] GetOtherPlayers(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _players.Where(player=>!ReferenceEquals(player,session)).ToArray();
    }
    //내부의 리스트가 외부에서 변경되지 않도록 복사본 반환
    public ClientSession[] GetPlayersSnapshot()
    {
        return _players.ToArray();
    }
    //로그인 아이디만 추출
    public string[] GetPlayerNames()
    {
        return _players
            .Select(player=>player.LoginId!)
            .ToArray();
    }
    //경기중 생존자만 반환
    public ClientSession[] GetAlivePlayers()
    {
        return _players.Where(
            player=>!_reportedDeaths.Contains(player.LoginId!)
        ).ToArray();
    }
    //최소 정보만 요약해서 반환 이름, 플레이어수, 게임중인지
    public RoomSummary ToSummary()
    {
        return new RoomSummary(
            Name,
            PlayerCount,
            IsPlaying
        );
    }
    //경기시작 요청 확인하고 2명 이상이면 매치id 생성
    public MatchStartResult TryStartMatch(
        ClientSession requester,
        out string? matchId
    )
    {
        ArgumentNullException.ThrowIfNull(requester);
        //시작에 실패하면 경기ID 전달안함
        matchId=null;
        if (IsPlaying)
        {
            return MatchStartResult.AlreadyPlaying;
        }
        if (!IsHost(requester))
        {
            return MatchStartResult.NotHost;
        }

        if (PlayerCount < 2)
        {
            return MatchStartResult.WaitingForPlayer;
        }
        IsPlaying = true;
        //매 경기마다 매치아이디 새로 만듬
        MatchId = Guid.NewGuid().ToString("N");
        //새 경기이므로 이전 경기의 사망 기록 초기화
        _reportedDeaths.Clear();
        matchId = MatchId;
        return MatchStartResult.Success;
    }
    //받은경기 id가 현재 진행중인거와 맞는지
    public bool IsCurrentMatch(string? matchId)
    {
        return IsPlaying&&!string.IsNullOrWhiteSpace(matchId)
            &&string.Equals(MatchId,matchId,StringComparison.Ordinal);
    }
    //현재 경기 참가자의 사망을 기록 (최초 1회)
    public bool TryReportDeath(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!IsPlaying || !Contains(session) || string.IsNullOrWhiteSpace(session.LoginId))
        {
            return false;
        }
        return _reportedDeaths.Add(session.LoginId);
    }
    //경기 초기화하고 다음 게임 준비
    public void FinishMatch()
    {
        IsPlaying=false;
        MatchId=null;
        _reportedDeaths.Clear();
    }
}
