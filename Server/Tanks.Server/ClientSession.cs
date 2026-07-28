using System.Net.Sockets;
using System.Threading;

//플레이어 한 명의 네트워크 연결과 상태를 보관하는 클래스
//프로젝트 내부에서만 사용하고 상속이 불가능하게 생성
internal sealed class ClientSession
{
    //플레이어 tcp 연결
    public TcpClient Client{get;}
    
    //닉네임을 받기 전에는 닉네임 미등록을 기본값으로
    public string Nickname {get; set;}
        ="닉네임 미등록";

    //현재 들어가있는 방 이름
    //현재 들어가있는 방이 없으면 로비
    public string RoomName {get; set;}
        = "로비";
    
    //비동기 잠금 -> 여러 작업이 동시에 메시지를 보낼 수 없도록 막음
    public SemaphoreSlim SendLock {get;}
        = new(1,1);

    //객체 생성
    public ClientSession(TcpClient client)
    {
        Client = client;
    }
}