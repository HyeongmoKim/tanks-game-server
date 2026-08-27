using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tanks.Complete;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TanksChatUI : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField]
    private TanksNetworkClient networkClient;

    [SerializeField]
    private GameManager gameManager;

    [SerializeField]
    private GameObject titleScreen;

    [Header("Screens")]
    [SerializeField]
    private Image rootBackground;

    [SerializeField]
    private GameObject startPanel;

    [SerializeField]
    private GameObject lobbyPanel;

    [SerializeField]
    private GameObject roomPanel;

    [SerializeField]
    private GameObject gameHud;

    [Header("Inputs")]
    [SerializeField]
    private TMP_InputField loginInput;

    [SerializeField]
    private TMP_InputField createRoomInput;

    [SerializeField]
    private TMP_InputField chatInput;

    [Header("Labels")]
    [SerializeField]
    private TMP_Text endpointText;

    [SerializeField]
    private TMP_Text startStatus;

    [SerializeField]
    private TMP_Text playerSummary;

    [SerializeField]
    private TMP_Text roomCountLabel;

    [SerializeField]
    private TMP_Text lobbyStatus;

    [SerializeField]
    private TMP_Text roomTitle;

    [SerializeField]
    private TMP_Text playerList;

    [SerializeField]
    private TMP_Text chatOutput;

    [SerializeField]
    private TMP_Text roomStatus;

    [SerializeField]
    private TMP_Text gameHudText;

    [SerializeField]
    private TMP_Text startGameButtonLabel;

    [Header("Buttons")]
    [SerializeField]
    private Button loginButton;

    [SerializeField]
    private Button disconnectButton;

    [SerializeField]
    private Button refreshButton;

    [SerializeField]
    private Button createRoomButton;

    [SerializeField]
    private Button leaveRoomButton;

    [SerializeField]
    private Button startGameButton;

    [SerializeField]
    private Button sendButton;

    [Header("Room List")]
    [SerializeField]
    private TanksRoomRowView[] roomRows =
        Array.Empty<TanksRoomRowView>();

    [SerializeField]
    private GameObject emptyRoomsView;

    [Header("Runtime Colors")]
    [SerializeField]
    private Color normalStatusColor;

    [SerializeField]
    private Color errorStatusColor;

    [SerializeField]
    private Color matchResultColor;

    [SerializeField]
    private Color gameHudColor;

    [SerializeField]
    private Color playerIndexColor;

    [SerializeField]
    private Color hostLabelColor;

    [SerializeField]
    private Color emptyPlayerColor;

    [SerializeField]
    private Color systemSenderColor;

    [SerializeField]
    private Color playerSenderColor;

    [Header("Network Tank Slots")]
    [SerializeField]
    private Color[] networkTankColors =
        Array.Empty<Color>();

    private TanksMultiplayerMatch multiplayerMatch;

    private readonly List<string> chatLines = new();

    private string loginId;
    private string currentRoom;
    private string currentMatchId;
    private bool isHost;
    private string[] currentPlayers = Array.Empty<string>();

    private void Awake()
    {
        if (networkClient == null)
        {
            networkClient =
                FindAnyObjectByType<TanksNetworkClient>();
        }

        if (gameManager == null)
        {
            gameManager =
                FindAnyObjectByType<GameManager>(
                    FindObjectsInactive.Include);
        }

        if (titleScreen == null)
        {
            titleScreen = GameObject.Find("TitleScreen");
        }

        if (!HasRequiredReferences())
        {
            Debug.LogError(
                "[Tanks] TanksChatUI hierarchy references are incomplete.",
                this);
            enabled = false;
            return;
        }

        endpointText.text = networkClient == null
            ? "Server component missing"
            : $"LOCAL SERVER  •  {networkClient.Endpoint}";

        loginButton.onClick.AddListener(Login);
        disconnectButton.onClick.AddListener(Disconnect);
        refreshButton.onClick.AddListener(RefreshRooms);
        createRoomButton.onClick.AddListener(CreateRoom);
        leaveRoomButton.onClick.AddListener(LeaveRoom);
        startGameButton.onClick.AddListener(StartGame);
        sendButton.onClick.AddListener(SendChat);
        chatInput.onSubmit.AddListener(HandleChatSubmitted);

        RenderRooms(Array.Empty<TanksRoomSummary>());
        ShowStart("Enter a Login ID to connect.");
    }

    private void OnEnable()
    {
        if (networkClient == null)
        {
            return;
        }

        networkClient.MessageReceived += OnServerMessage;
        networkClient.Disconnected += OnDisconnected;
    }

    private void OnDisable()
    {
        if (networkClient == null)
        {
            return;
        }

        networkClient.MessageReceived -= OnServerMessage;
        networkClient.Disconnected -= OnDisconnected;
    }

    private void OnDestroy()
    {
        if (loginButton == null)
        {
            return;
        }

        loginButton.onClick.RemoveListener(Login);
        disconnectButton.onClick.RemoveListener(Disconnect);
        refreshButton.onClick.RemoveListener(RefreshRooms);
        createRoomButton.onClick.RemoveListener(CreateRoom);
        leaveRoomButton.onClick.RemoveListener(LeaveRoom);
        startGameButton.onClick.RemoveListener(StartGame);
        sendButton.onClick.RemoveListener(SendChat);
        chatInput.onSubmit.RemoveListener(HandleChatSubmitted);
    }

    private bool HasRequiredReferences()
    {
        return networkClient != null &&
               rootBackground != null &&
               startPanel != null &&
               lobbyPanel != null &&
               roomPanel != null &&
               gameHud != null &&
               loginInput != null &&
               createRoomInput != null &&
               chatInput != null &&
               endpointText != null &&
               startStatus != null &&
               playerSummary != null &&
               roomCountLabel != null &&
               lobbyStatus != null &&
               roomTitle != null &&
               playerList != null &&
               chatOutput != null &&
               roomStatus != null &&
               gameHudText != null &&
               startGameButtonLabel != null &&
               loginButton != null &&
               disconnectButton != null &&
               refreshButton != null &&
               createRoomButton != null &&
               leaveRoomButton != null &&
               startGameButton != null &&
               sendButton != null &&
               emptyRoomsView != null &&
               roomRows is { Length: 7 } &&
               roomRows.All(row => row != null) &&
               networkTankColors is { Length: 4 };
    }

    private async void Login()
    {
        if (networkClient == null)
        {
            ShowStart(
                "Network component is missing.",
                true);
            return;
        }

        string requestedLoginId =
            loginInput.text.Trim();

        if (requestedLoginId.Length < 3)
        {
            ShowStart(
                "Login ID must contain at least 3 characters.",
                true);
            return;
        }

        loginButton.interactable = false;
        loginInput.interactable = false;
        ShowStart(
            $"Connecting to {networkClient.Endpoint}...");

        try
        {
            await networkClient.ConnectAsync();
            await networkClient.LoginAsync(
                requestedLoginId);
            ShowStart("Checking player profile...");
        }
        catch (Exception exception)
        {
            loginButton.interactable = true;
            loginInput.interactable = true;
            ShowStart(
                $"Connection failed: {exception.Message}",
                true);
            Debug.LogException(exception);
        }
    }

    private async void RefreshRooms()
    {
        await RunNetworkAction(
            () => networkClient.RequestRoomListAsync(),
            message =>
            {
                lobbyStatus.text = message;
                lobbyStatus.color = errorStatusColor;
            });
    }

    private async void CreateRoom()
    {
        string roomName =
            createRoomInput.text.Trim();

        if (roomName.Length == 0)
        {
            lobbyStatus.text =
                "Enter a room name first.";
            lobbyStatus.color =
                errorStatusColor;
            return;
        }

        createRoomButton.interactable = false;
        lobbyStatus.text = "Creating room...";
        lobbyStatus.color = normalStatusColor;

        await RunNetworkAction(
            () => networkClient.CreateRoomAsync(roomName),
            message =>
            {
                createRoomButton.interactable = true;
                lobbyStatus.text = message;
                lobbyStatus.color = errorStatusColor;
            });
    }

    private async void JoinRoom(string roomName)
    {
        lobbyStatus.text =
            $"Joining {roomName}...";
        lobbyStatus.color = normalStatusColor;

        await RunNetworkAction(
            () => networkClient.JoinRoomAsync(roomName),
            message =>
            {
                lobbyStatus.text = message;
                lobbyStatus.color = errorStatusColor;
            });
    }

    private async void LeaveRoom()
    {
        await RunNetworkAction(
            () => networkClient.LeaveRoomAsync(),
            message =>
            {
                roomStatus.text = message;
                roomStatus.color = errorStatusColor;
            });
    }

    private async void SendChat()
    {
        string message = chatInput.text.Trim();

        if (message.Length == 0)
        {
            return;
        }

        chatInput.text = string.Empty;
        chatInput.ActivateInputField();

        await RunNetworkAction(
            () => networkClient.SendChatAsync(message),
            error => AppendChat("SYSTEM", error));
    }

    private void HandleChatSubmitted(string _)
    {
        SendChat();
    }

    private async void StartGame()
    {
        startGameButton.interactable = false;
        roomStatus.text =
            "Starting match for every player...";
        roomStatus.color = normalStatusColor;

        await RunNetworkAction(
            () => networkClient.StartGameAsync(),
            message =>
            {
                startGameButton.interactable = isHost;
                roomStatus.text = message;
                roomStatus.color = errorStatusColor;
            });
    }

    private void Disconnect()
    {
        networkClient?.Disconnect();
        ResetSession();
        ShowStart(
            "Disconnected. Enter a Login ID to reconnect.");
    }

    private async Task RunNetworkAction(
        Func<Task> action,
        Action<string> showError)
    {
        if (networkClient == null ||
            !networkClient.IsConnected)
        {
            showError("Not connected to the server.");
            return;
        }

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            showError(exception.Message);
            Debug.LogException(exception);
        }
    }

    private void OnServerMessage(
        TanksServerMessage message)
    {
        switch (message.type)
        {
            case "login_result":
                loginId = message.loginId;
                playerSummary.text =
                    $"{message.loginId}  •  " +
                    $"{message.wins}W / {message.losses}L";
                loginButton.interactable = true;
                loginInput.interactable = true;
                lobbyStatus.text =
                    "Connected. Choose a room.";
                lobbyStatus.color =
                    normalStatusColor;
                ShowOnly(lobbyPanel);
                _ = networkClient.RequestRoomListAsync();
                break;

            case "room_list":
                RenderRooms(
                    message.rooms ??
                    Array.Empty<TanksRoomSummary>());
                break;

            case "room_joined":
                currentRoom = message.roomName;
                isHost = message.isHost;
                currentPlayers =
                    message.players ??
                    Array.Empty<string>();
                createRoomButton.interactable = true;
                chatLines.Clear();
                AppendChat(
                    "SYSTEM",
                    $"Joined {currentRoom}.");
                UpdateRoomView();
                ShowOnly(roomPanel);
                break;

            case "room_state":
                currentRoom = message.roomName;
                isHost = message.isHost;
                currentPlayers =
                    message.players ??
                    Array.Empty<string>();
                UpdateRoomView();
                break;

            case "chat":
                AppendChat(
                    message.sender,
                    message.message);
                break;

            case "left_room":
                CancelActiveMatch();
                currentRoom = null;
                isHost = false;
                currentMatchId = null;
                currentPlayers =
                    Array.Empty<string>();
                lobbyStatus.text =
                    "You left the room.";
                lobbyStatus.color =
                    normalStatusColor;
                ShowOnly(lobbyPanel);
                _ = networkClient.RequestRoomListAsync();
                break;

            case "game_started":
                if (string.IsNullOrWhiteSpace(
                        message.matchId))
                {
                    startGameButton.interactable =
                        isHost;
                    ShowContextError(
                        "서버가 MatchId를 보내지 않음.");
                    break;
                }

                currentMatchId = message.matchId;
                currentPlayers =
                    message.players ??
                    currentPlayers;
                BeginTanksMatch();
                break;

            case "match_ended":
            {
                if (!string.Equals(
                        message.matchId,
                        currentMatchId,
                        StringComparison.Ordinal))
                {
                    break;
                }

                string result;

                if (string.IsNullOrWhiteSpace(
                        message.winner))
                {
                    result =
                        "The match ended without a winner.";
                }
                else if (string.Equals(
                             message.winner,
                             loginId,
                             StringComparison.OrdinalIgnoreCase))
                {
                    result = "You won the match.";
                }
                else
                {
                    result =
                        $"{message.winner} won the match.";
                }

                if (!string.IsNullOrWhiteSpace(
                        message.winner) &&
                    !message.statsRecorded)
                {
                    result += " Stats were not saved.";
                }

                currentMatchId = null;
                multiplayerMatch = null;

                AppendChat("SYSTEM", result);
                UpdateRoomView();

                roomStatus.text =
                    result +
                    (isHost
                        ? " Start another match when ready."
                        : " Waiting for the host.");
                roomStatus.color = matchResultColor;
                ShowOnly(roomPanel);
                break;
            }

            case "error":
                loginButton.interactable = true;
                loginInput.interactable = true;
                createRoomButton.interactable = true;
                startGameButton.interactable = isHost;
                ShowContextError(
                    message.error ??
                    "The server rejected the request.");
                break;
        }
    }

    private void OnDisconnected(string reason)
    {
        ResetSession();
        ShowStart(reason, true);
    }

    private void RenderRooms(
        TanksRoomSummary[] rooms)
    {
        roomCountLabel.text =
            $"{rooms.Length} ROOM" +
            (rooms.Length == 1 ? string.Empty : "S");

        emptyRoomsView.SetActive(rooms.Length == 0);

        int visibleCount =
            Mathf.Min(rooms.Length, roomRows.Length);

        for (int index = 0;
             index < roomRows.Length;
             index++)
        {
            if (index < visibleCount)
            {
                roomRows[index].Show(
                    rooms[index],
                    JoinRoom);
            }
            else
            {
                roomRows[index].Hide();
            }
        }
    }

    private void UpdateRoomView()
    {
        roomTitle.text =
            string.IsNullOrWhiteSpace(currentRoom)
                ? "ROOM"
                : currentRoom.ToUpperInvariant();

        string playerIndexHex =
            ColorUtility.ToHtmlStringRGB(
                playerIndexColor);
        string hostLabelHex =
            ColorUtility.ToHtmlStringRGB(
                hostLabelColor);
        string emptyPlayerHex =
            ColorUtility.ToHtmlStringRGB(
                emptyPlayerColor);

        List<string> lines = new();

        for (int index = 0;
             index < currentPlayers.Length;
             index++)
        {
            string hostMark = index == 0
                ? $"  <color=#{hostLabelHex}>HOST</color>"
                : string.Empty;

            lines.Add(
                $"<color=#{playerIndexHex}>" +
                $"0{index + 1}</color>   " +
                $"{currentPlayers[index]}{hostMark}");
        }

        for (int index = currentPlayers.Length;
             index < 4;
             index++)
        {
            lines.Add(
                $"<color=#{emptyPlayerHex}>" +
                $"0{index + 1}   Waiting...</color>");
        }

        playerList.text =
            string.Join("\n\n", lines);

        startGameButton.interactable = isHost;
        startGameButtonLabel.text = isHost
            ? "START GAME"
            : "WAITING FOR HOST";

        roomStatus.text = isHost
            ? "You are the host. Start when ready."
            : "The host will start the match.";
        roomStatus.color = normalStatusColor;
    }

    private void AppendChat(
        string sender,
        string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string safeSender =
            string.IsNullOrWhiteSpace(sender)
                ? "SYSTEM"
                : sender;

        Color senderColor =
            safeSender == "SYSTEM"
                ? systemSenderColor
                : playerSenderColor;

        string senderHex =
            ColorUtility.ToHtmlStringRGB(
                senderColor);

        chatLines.Add(
            $"<color=#{senderHex}><b>" +
            $"{safeSender}</b></color>  {message}");

        while (chatLines.Count > 12)
        {
            chatLines.RemoveAt(0);
        }

        chatOutput.text =
            string.Join("\n\n", chatLines);
    }

    private void BeginTanksMatch()
    {
        startPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        roomPanel.SetActive(false);
        gameHud.SetActive(true);
        rootBackground.enabled = false;

        gameHudText.color = gameHudColor;
        gameHudText.text =
            $"{currentRoom?.ToUpperInvariant()}  •  " +
            "ROOM CONNECTED  •  GAMEPLAY PROTOTYPE";

        if (titleScreen != null)
        {
            titleScreen.SetActive(false);
        }

        if (gameManager == null)
        {
            gameHudText.text =
                "GAME MANAGER NOT FOUND";
            gameHudText.color =
                errorStatusColor;
            return;
        }

        if (currentPlayers.Length < 2 ||
            currentPlayers.Length > 4)
        {
            gameHudText.text =
                "INVALID PLAYER COUNT";
            gameHudText.color =
                errorStatusColor;
            return;
        }

        if (!currentPlayers.Any(player =>
                string.Equals(
                    player,
                    loginId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            gameHudText.text =
                "LOCAL PLAYER NOT FOUND";
            gameHudText.color =
                errorStatusColor;
            return;
        }

        GameManager.PlayerData[] players =
            BuildNetworkMatchPlayers(gameManager);

        gameManager.StartNetworkGame(players);

        multiplayerMatch =
            gameManager.GetComponent<
                TanksMultiplayerMatch>();

        if (multiplayerMatch == null)
        {
            multiplayerMatch =
                gameManager.gameObject.AddComponent<
                    TanksMultiplayerMatch>();
        }

        multiplayerMatch.Initialize(
            networkClient,
            gameManager,
            currentMatchId,
            loginId,
            currentPlayers);
    }

    private GameManager.PlayerData[]
        BuildNetworkMatchPlayers(
            GameManager manager)
    {
        GameObject[] prefabs =
        {
            manager.m_Tank1Prefab,
            manager.m_Tank2Prefab,
            manager.m_Tank3Prefab,
            manager.m_Tank4Prefab
        };

        GameManager.PlayerData[] result =
            new GameManager.PlayerData[
                currentPlayers.Length];

        for (int index = 0;
             index < currentPlayers.Length;
             index++)
        {
            string playerLoginId =
                currentPlayers[index];

            bool isLocalPlayer =
                string.Equals(
                    playerLoginId,
                    loginId,
                    StringComparison.OrdinalIgnoreCase);

            result[index] =
                new GameManager.PlayerData
                {
                    LoginId = playerLoginId,
                    IsRemote = !isLocalPlayer,
                    IsComputer = false,
                    TankColor =
                        networkTankColors[index],
                    UsedPrefab = prefabs[index],
                    ControlIndex =
                        isLocalPlayer ? 0 : -1
                };
        }

        return result;
    }

    private void ShowContextError(string message)
    {
        if (roomPanel.activeSelf)
        {
            roomStatus.text = message;
            roomStatus.color = errorStatusColor;
            return;
        }

        if (lobbyPanel.activeSelf)
        {
            lobbyStatus.text = message;
            lobbyStatus.color = errorStatusColor;
            return;
        }

        ShowStart(message, true);
    }

    private void ShowStart(
        string message,
        bool isError = false)
    {
        ShowOnly(startPanel);
        startStatus.text = message;
        startStatus.color = isError
            ? errorStatusColor
            : normalStatusColor;
    }

    private void ShowOnly(GameObject panelToShow)
    {
        startPanel.SetActive(
            ReferenceEquals(
                panelToShow,
                startPanel));
        lobbyPanel.SetActive(
            ReferenceEquals(
                panelToShow,
                lobbyPanel));
        roomPanel.SetActive(
            ReferenceEquals(
                panelToShow,
                roomPanel));
        gameHud.SetActive(
            ReferenceEquals(
                panelToShow,
                gameHud));
        rootBackground.enabled = true;
    }

    private void ResetSession()
    {
        CancelActiveMatch();

        loginId = null;
        currentRoom = null;
        isHost = false;
        currentMatchId = null;
        currentPlayers = Array.Empty<string>();
        chatLines.Clear();
        chatOutput.text = string.Empty;

        loginButton.interactable = true;
        loginInput.interactable = true;
    }

    private void CancelActiveMatch()
    {
        if (multiplayerMatch == null)
        {
            return;
        }

        multiplayerMatch.CancelMatch();
        multiplayerMatch = null;
    }
}
