using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TanksRoomRowView : MonoBehaviour
{
    [SerializeField]
    private Button joinButton;

    [SerializeField]
    private TMP_Text roomNameLabel;

    [SerializeField]
    private TMP_Text roomDetailsLabel;

    private string roomName;
    private Action<string> joinRequested;

    private void Awake()
    {
        joinButton.onClick.AddListener(HandleJoinRequested);
    }

    private void OnDestroy()
    {
        joinButton.onClick.RemoveListener(HandleJoinRequested);
    }

    public void Show(
        TanksRoomSummary room,
        Action<string> onJoinRequested)
    {
        if (room == null)
        {
            throw new ArgumentNullException(nameof(room));
        }

        if (onJoinRequested == null)
        {
            throw new ArgumentNullException(
                nameof(onJoinRequested));
        }

        roomName = room.name;
        joinRequested = onJoinRequested;

        roomNameLabel.text = room.name;
        roomDetailsLabel.text =
            $"{room.playerCount}/4 PLAYERS  •  " +
            (room.isPlaying ? "IN MATCH" : "OPEN");

        joinButton.interactable =
            !room.isPlaying &&
            room.playerCount < 4;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        roomName = null;
        joinRequested = null;
        gameObject.SetActive(false);
    }

    private void HandleJoinRequested()
    {
        if (!string.IsNullOrWhiteSpace(roomName))
        {
            joinRequested?.Invoke(roomName);
        }
    }
}
