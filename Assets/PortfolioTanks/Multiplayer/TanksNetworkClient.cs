using UnityEngine;
using System;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;



public sealed class TanksNetworkClient : MonoBehaviour
{
    [SerializeField]
    private string serverAddress = "127.0.0.1";
    [SerializeField]
    private int serverPort = 7777;

    private TcpClient client;
    private StreamReader reader;

    private StreamWriter writer;

    public event Action<string> MessageReceived;
    public bool IsConnected {get; private set;}
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1,1);
    public async Task ConnectAsync(
        string nickname,
        string roomName
    ){
        if(IsConnected){
            return;
        }
    if (string.IsNullOrWhiteSpace(nickname))
    {
        throw new ArgumentException(
            "닉네임을 입력해야합니다.",
            nameof(nickname));
        
    }
        if (string.IsNullOrWhiteSpace(roomName))
        {
            roomName="로비";
        }
        client = new TcpClient();
        try
        {
            await client.ConnectAsync(
                serverAddress,
                serverPort
            );
            NetworkStream stream = client.GetStream();
            UTF8Encoding utf8 = new(false);

            reader = new StreamReader(
                stream,
                utf8,
                false,
                1024,
                true
            );

            writer = new StreamWriter(
                stream,
                utf8,
                1024,
                true
            );
            writer.AutoFlush = true;
            await writer.WriteLineAsync(nickname.Trim());
            await writer.WriteLineAsync(roomName.Trim());
            IsConnected = true;
            Debug.Log($"서버 접속 완료 : {nickname}/{roomName}");
            _ =ReceiveLoopAsync();

        }
        catch
        {
            client.Dispose();
            client=null;
            throw;
        }

    }
    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (IsConnected)
            {
                string message = await reader.ReadLineAsync();
                if (message == null)
                {
                    break;
                }
                Debug.Log($"서버 메시지 : {message}");
                MessageReceived?.Invoke(message);
            }
        }
        catch(ObjectDisposedException){}
        catch(Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            IsConnected=false;
            writer?.Dispose();
            reader?.Dispose();
            client?.Dispose();
            writer = null;
            reader = null;
            client = null;
            Debug.Log("서버 연결 종료");
        }
    }
    public async Task SendChatAsync(string message)
    {
        if(!IsConnected || writer == null)
        {
            throw new InvalidOperationException("서버에 연결되어있지 않습니다.");
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        await sendLock.WaitAsync();
        try
        {
            await writer.WriteLineAsync(message.Trim());
        }
        finally
        {
            sendLock.Release();
        }
    }
    public void Disconnect()
    {
        if(!IsConnected && client == null)
        {
            return;
        }
        IsConnected = false;
        client?.Dispose();
    }
    private void OnDestroy()
    {
        Disconnect();
    }
}
