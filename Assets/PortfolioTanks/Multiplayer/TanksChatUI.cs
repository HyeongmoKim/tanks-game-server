using UnityEngine;
using System;
using TMPro;
using UnityEngine.UI;
public sealed class TanksChatUI : MonoBehaviour
{
    [SerializeField]
    private TanksNetworkClient networkClient;
    [SerializeField]
    private TMP_InputField nicknameInput;
    [SerializeField]
    private TMP_InputField roomInput;
    [SerializeField]
    private TMP_InputField messageInput;
    [SerializeField]
    private TMP_Text chatOutput;
    [SerializeField]
    private Button connectButton;
    [SerializeField]
    private Button sendButton;
    private void OnEnable()
    {
        if (networkClient != null)
        {
            networkClient.MessageReceived += OnMessageReceived;

        }
    }
    private void OnDisable()
    {
        if (networkClient != null)
        {
            networkClient.MessageReceived -= OnMessageReceived;
        }   
    }
    private void OnMessageReceived(string message)
    {
        if (chatOutput == null)
        {
            return;
        }
        chatOutput.text +=message + "\n";
    }
    public async void Connect()
    {
        if(networkClient==null||
        nicknameInput==null||
        roomInput == null)
        {
            Debug.LogError("채팅 UI 인스펙터 연결 필요");
            return;
        }
        if (connectButton != null)
        {
            connectButton.interactable=false;
        }
        try
        {
            await networkClient.ConnectAsync(
                nicknameInput.text,
                roomInput.text
            );
            nicknameInput.interactable=false;
            roomInput.interactable=false;

            if(sendButton != null)
            {
                sendButton.interactable=true;
            }
            string connectedRoom = roomInput.text.Trim();

            if (string.IsNullOrWhiteSpace(connectedRoom))
            {
                connectedRoom="로비";
            }
            OnMessageReceived($"[시스템]{connectedRoom}방에 접속했습니다.");
        }
        catch (Exception exception)
        {
            OnMessageReceived($"연결 실패 : {exception.Message}");
            Debug.LogException(exception);
            if(connectButton != null)
            {
                connectButton.interactable = true;
            }
        }
    }
    public async void SendChat()
    {
        if (networkClient == null || messageInput == null)
        {
            Debug.LogError("네트워크와 메시지 입력창을 연결해야함");
            return;
        }
        string message = messageInput.text;
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        try
        {
            await networkClient.SendChatAsync(message);
            messageInput.text = string.Empty;
            messageInput.ActivateInputField();
        }
        catch(Exception exception)
        {
            OnMessageReceived($"전송 실패 : {exception.Message}");
            Debug.LogException(exception);
        }
    }
}
