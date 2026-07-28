using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.IO;

// 7777포트로 서버 열기
TcpListener listener = new(IPAddress.Loopback, 7777);
// 클라이언트의 접속 받기
listener.Start();
Console.WriteLine("서버가 127.0.0.1:7777에 열림");
//여러 클라이언트 처리를 동시에 접근할 수 있는 접속자 목록
ConcurrentDictionary<TcpClient, ClientSession> clients = new();

// 서버 종료시까지 클라이언트 받음
while (true)
{
    //클라이언트가 접속할 떄 까지 비동기로 기다림
    TcpClient client = await listener.AcceptTcpClientAsync();
    //새로 접속한 사람의 세션 객체 생성
    ClientSession session = new(client);
    // 접속자 목록에 등록
    clients.TryAdd(client,session);
    Console.WriteLine($"클라이언트 접속 현재 {clients.Count}명");
    // 이 작업을 기다리지 않고 다음 클라이언트 접속을 받으러 돌아감
    _ = HandleClientAsync(session,clients);
}

// 메시지 처리 메소드
static async Task HandleClientAsync(ClientSession session,ConcurrentDictionary<TcpClient,ClientSession>clients)
{
    TcpClient client = session.Client;
    // 메서드가 끝나면 연결을 자동으로 정리
    using (client)
    {
        // 클라이언트와 주고받는 통로
        NetworkStream stream = client.GetStream();
        // TCP 바이트를 UTF8로 인코딩하고 줄바꿈단위로 구분지음
        using StreamReader reader = new(stream,Encoding.UTF8);

        string? nickname = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            clients.TryRemove(client,out _);
            return;
        }
        nickname = nickname.Trim();
        session.Nickname = nickname;
        Console.WriteLine($"닉네임 등록 {nickname}");

        //닉네임 다음줄 방이름으로 읽음
        string? roomName = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(roomName))
        {
            session.RoomName="로비";
        }
        else
        {
            session.RoomName=roomName.Trim();
        }
        Console.WriteLine($"{session.Nickname}님이 {session.RoomName}에 입장하였습니다.");

        // 클라이언트가 연결 종료할떄 까지 메시지 읽기
        while (true)
        {
            string? message = await reader.ReadLineAsync();
            if(message is null)
            {
                break;
            }
            Console.WriteLine($"받은 메시지 {message}");

            // 메시지 끝에 줄바꿈 추가
            byte[] messageBytes = Encoding.UTF8.GetBytes($"{session.Nickname} : {message}\n");


            //각 플레이어에게 채팅 전달
            foreach(ClientSession connectedSession in clients.Values)
            {
                if (connectedSession.RoomName != session.RoomName)
                {
                    continue;
                }
                await connectedSession.SendLock.WaitAsync();
                try
                {
                    NetworkStream connectedStream = connectedSession.Client.GetStream();
                    await connectedStream.WriteAsync(messageBytes);
                }
                finally
                {
                    connectedSession.SendLock.Release();
                }
            }
            
        }
    }
    //접속 종료
    clients.TryRemove(client,out _);
    Console.WriteLine($"클라이언트 종료 현재 {clients.Count}명");
}