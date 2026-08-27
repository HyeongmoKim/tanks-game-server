using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

//플레이어 한 명의 네트워크 연결과 상태를 보관하는 클래스
//프로젝트 내부에서만 사용하고 상속이 불가능하게 생성
//네트워크 자원을 비동기로 정리하기 위해 IAsyncDisposable 구현
internal sealed class ClientSession : IAsyncDisposable
{
    //문자열 읽기 쓰기 객체
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    //동시에 전송하는거 막음
    private readonly SemaphoreSlim _sendLock = new(1,1);
    
    //사용중일때 0 1일때 정리
    //Interlocked.Exchange를 쓰기위해 bool이 아닌 int 사용
    private int _disposed;

    public ClientSession(TcpClient client)
    {
        //client null인지 확인
        ArgumentNullException.ThrowIfNull(client);
        Client = client;

        //스트림 하나를 리더와 라이터가 사용
        NetworkStream stream = client.GetStream();
        //tcp에 필요없는 BOM 삭제
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier:false);

        _reader = new StreamReader(
            stream,
            utf8,
            detectEncodingFromByteOrderMarks:false,
            bufferSize:4096,
            leaveOpen : true
        );
        _writer = new StreamWriter(
            stream,
            utf8,
            bufferSize : 4096,
            leaveOpen:true
        )
        {
            //쓸때마다 출력
            AutoFlush = true
        };
    }
    //연결마다 생성되는 식별자 DB의 ID와는 다름
    public Guid Id {get;} = Guid.NewGuid();
    public TcpClient Client {get;}

    //접속 클라이언트의 IP와 포트
    public EndPoint? RemoteEndPoint=>Client.Client.RemoteEndPoint;

    //로그인과 방 입장시 값 설정
    public string? LoginId{get;set;}
    public string? RoomName{get;set;}

    //현재 문자열 값으로 상태 계산
    public bool IsLoggedIn=> !string.IsNullOrWhiteSpace(LoginId);
    public bool IsInRoom=> !string.IsNullOrWhiteSpace(RoomName);

    //줄바꿈까지 비동기 읽기 Json 형식
    public Task<string?> ReadLineAsync()
    {
        ThrowIfDisposed();
        return _reader.ReadLineAsync();
    }

    //Json으로 변환 후 한 줄로 전송
    public async Task SendAsync(ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfDisposed();
        string json = Protocol.Serialize(message);
        //다른 전송이 끝날 때까지 전송 잠금 대기
        await _sendLock.WaitAsync();
        try
        {
            //잠금을 기다리는 동안 세션이 종료됐을 수 있으므로 다시 확인
            ThrowIfDisposed();
            await _writer.WriteLineAsync(json);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    //전송 끝나면 리더 라이터 연결 비동기 정리
    public async ValueTask DisposeAsync()
    {
        //여러 작업이 동시에 요청해도 처음 작업만 정리
        if(Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        //SendAsync와 자원 정리가 동시에 실행되지 않도록 전송 잠금 획득
        await _sendLock.WaitAsync();
        try
        {
            try
            {
                await _writer.DisposeAsync();
            }
            finally
            {
                _reader.Dispose();
                Client.Dispose();
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
    
    //이미 정리된 세션을 다시 쓰려하면 예외처리
    private void ThrowIfDisposed()
    {
        //다른 스레드가 변경한 최신 종료상태 읽기
        if(Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(
                nameof(ClientSession)
            );
        }
    }
}
