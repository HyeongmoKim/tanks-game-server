using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tanks.Complete;
using UnityEngine;

public sealed class TanksMultiplayerMatch : MonoBehaviour
{
    private const float StateSendInterval = 0.05f;
    private const float PositionLerpSpeed = 12f;
    private const float RotationLerpSpeed = 15f;

    private readonly Dictionary<string, RemoteTankState>
        remoteTanks =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int>
        lastRemoteFireSequences =
            new(StringComparer.OrdinalIgnoreCase);

    private TanksNetworkClient networkClient;
    private GameManager gameManager;
    private TankManager localTank;
    private TankShooting localShooting;
    private TankHealth localHealth;

    private string matchId;
    private string localLoginId;

    private int sequence;
    private int fireSequence;
    private float nextStateSendTime;
    private bool initialized;
    private bool sendingState;
    private bool deathReportQueued;

    public void Initialize(
        TanksNetworkClient client,
        GameManager manager,
        string currentMatchId,
        string loginId,
        string[] players)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (manager == null)
        {
            throw new ArgumentNullException(nameof(manager));
        }

        if (string.IsNullOrWhiteSpace(currentMatchId))
        {
            throw new ArgumentException(
                "MatchId가 필요합니다.",
                nameof(currentMatchId));
        }

        if (string.IsNullOrWhiteSpace(loginId))
        {
            throw new ArgumentException(
                "로그인 ID가 필요합니다.",
                nameof(loginId));
        }

        if (players == null)
        {
            throw new ArgumentNullException(nameof(players));
        }

        if (players.Length < 2 ||
            players.Length > manager.m_SpawnPoints.Length)
        {
            throw new ArgumentException(
                "플레이어 수와 스폰 지점 수가 맞지 않습니다.",
                nameof(players));
        }

        Unsubscribe();

        networkClient = client;
        gameManager = manager;
        matchId = currentMatchId;
        localLoginId = loginId;

        localTank = null;
        localShooting = null;
        localHealth = null;

        remoteTanks.Clear();
        lastRemoteFireSequences.Clear();

        sequence = 0;
        fireSequence = 0;
        nextStateSendTime = 0f;
        initialized = false;
        sendingState = false;
        deathReportQueued = false;

        for (int index = 0;
             index < players.Length;
             index++)
        {
            TankManager tank =
                manager.m_SpawnPoints[index];

            if (tank.m_Instance == null)
            {
                throw new InvalidOperationException(
                    $"플레이어 {players[index]}의 탱크가 생성되지 않았습니다.");
            }

            bool isLocal =
                string.Equals(
                    players[index],
                    localLoginId,
                    StringComparison.OrdinalIgnoreCase);

            if (isLocal)
            {
                localTank = tank;
                continue;
            }

            ConfigureRemoteTank(tank);

            remoteTanks.Add(
                players[index],
                new RemoteTankState(
                    tank.m_Instance));
        }

        if (localTank == null)
        {
            throw new InvalidOperationException(
                "로컬 플레이어의 탱크를 찾지 못했습니다.");
        }

        localShooting =
            localTank.m_Instance.GetComponent<TankShooting>();

        localHealth =
            localTank.m_Instance.GetComponent<TankHealth>();

        if (localShooting == null)
        {
            throw new InvalidOperationException(
                "Local tank does not have TankShooting.");
        }

        if (localHealth == null)
        {
            throw new InvalidOperationException(
                "Local tank does not have TankHealth.");
        }

        localHealth.SetDamageAuthority(true);

        networkClient.MessageReceived +=
            OnServerMessage;

        localShooting.LocalShellFired +=
            OnLocalShellFired;

        localHealth.HealthChanged +=
            OnLocalHealthChanged;

        initialized = true;

        OnLocalHealthChanged(
            localHealth.CurrentHealth,
            !localHealth.IsDead);
    }

    public void CancelMatch()
    {
        GameManager manager =
            gameManager;

        ResetMatchState();
        manager?.CancelNetworkGame();
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        ApplyRemoteTankStates();
        TrySendLocalTankState();
    }

    private void TrySendLocalTankState()
    {
        if (sendingState ||
            networkClient == null ||
            !networkClient.IsConnected ||
            localTank?.m_Instance == null ||
            !localTank.m_Instance.activeInHierarchy ||
            Time.unscaledTime < nextStateSendTime)
        {
            return;
        }

        nextStateSendTime =
            Time.unscaledTime + StateSendInterval;

        Transform tankTransform =
            localTank.m_Instance.transform;

        int packetSequence = sequence++;
        Vector3 position = tankTransform.position;
        Quaternion rotation = tankTransform.rotation;

        sendingState = true;

        _ = SendLocalTankStateAsync(
            packetSequence,
            position,
            rotation);
    }

    private async Task SendLocalTankStateAsync(
        int packetSequence,
        Vector3 position,
        Quaternion rotation)
    {
        try
        {
            await networkClient.SendTankStateAsync(
                matchId,
                packetSequence,
                position,
                rotation);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[Tanks] Tank state send failed: " +
                $"{exception.Message}");
        }
        finally
        {
            sendingState = false;
        }
    }

    private void OnLocalShellFired(
        TankFireData fireData)
    {
        if (!initialized ||
            networkClient == null ||
            !networkClient.IsConnected)
        {
            return;
        }

        TanksNetworkClient client =
            networkClient;

        string currentMatchId =
            matchId;

        int packetSequence =
            fireSequence++;

        _ = SendLocalFireAsync(
            client,
            currentMatchId,
            packetSequence,
            fireData);
    }

    private async Task SendLocalFireAsync(
        TanksNetworkClient client,
        string currentMatchId,
        int packetSequence,
        TankFireData fireData)
    {
        try
        {
            await client.SendFireAsync(
                currentMatchId,
                packetSequence,
                fireData.Position,
                fireData.Velocity,
                fireData.MaxDamage,
                fireData.ExplosionForce,
                fireData.ExplosionRadius);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[Tanks] Fire send failed: " +
                $"{exception.Message}");
        }
    }

    private void OnLocalHealthChanged(
        float health,
        bool alive)
    {
        if (!initialized ||
            networkClient == null ||
            !networkClient.IsConnected)
        {
            return;
        }

        bool shouldReportDeath =
            !alive &&
            !deathReportQueued;

        if (shouldReportDeath)
        {
            deathReportQueued = true;
        }

        TanksNetworkClient client =
            networkClient;

        string currentMatchId =
            matchId;

        _ = SendLocalHealthAsync(
            client,
            currentMatchId,
            health,
            alive,
            shouldReportDeath);
    }

    private async Task SendLocalHealthAsync(
        TanksNetworkClient client,
        string currentMatchId,
        float health,
        bool alive,
        bool shouldReportDeath)
    {
        try
        {
            await client.SendTankHealthAsync(
                currentMatchId,
                health,
                alive);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[Tanks] Health send failed: " +
                $"{exception.Message}");
        }

        if (!shouldReportDeath)
        {
            return;
        }

        try
        {
            await client.SendPlayerDeadAsync(
                currentMatchId);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[Tanks] Death send failed: " +
                $"{exception.Message}");
        }
    }

    private void OnServerMessage(
        TanksServerMessage message)
    {
        if (!initialized ||
            !string.Equals(
                message.matchId,
                matchId,
                StringComparison.Ordinal))
        {
            return;
        }

        switch (message.type)
        {
            case "tank_state":
                ReceiveTankState(message);
                break;

            case "fire":
                ReceiveFire(message);
                break;

            case "tank_health":
                ReceiveTankHealth(message);
                break;

            case "player_dead":
                ReceivePlayerDead(message);
                break;

            case "match_ended":
            {
                GameManager manager =
                    gameManager;

                ResetMatchState();
                manager?.EndNetworkGame(
                    message.winner);
                break;
            }
        }
    }

    private void ReceiveTankState(
        TanksServerMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.loginId) ||
            !remoteTanks.TryGetValue(
                message.loginId,
                out RemoteTankState state))
        {
            return;
        }

        Quaternion rotation =
            new(
                message.rx,
                message.ry,
                message.rz,
                message.rw);

        if (Quaternion.Dot(rotation, rotation) < 0.0001f)
        {
            return;
        }

        state.TargetPosition =
            new Vector3(
                message.px,
                message.py,
                message.pz);

        state.TargetRotation =
            Quaternion.Normalize(rotation);

        state.HasReceivedState = true;
    }

    private void ReceiveFire(
        TanksServerMessage message)
    {
        if (message.sequence < 0 ||
            string.IsNullOrWhiteSpace(
                message.loginId) ||
            string.Equals(
                message.loginId,
                localLoginId,
                StringComparison.OrdinalIgnoreCase) ||
            !remoteTanks.TryGetValue(
                message.loginId,
                out RemoteTankState state) ||
            state.IsDead)
        {
            return;
        }

        if (lastRemoteFireSequences.TryGetValue(
                message.loginId,
                out int lastSequence) &&
            message.sequence <= lastSequence)
        {
            return;
        }

        lastRemoteFireSequences[message.loginId] =
            message.sequence;

        state.Shooting.ReplayNetworkFire(
            new Vector3(
                message.px,
                message.py,
                message.pz),
            new Vector3(
                message.vx,
                message.vy,
                message.vz),
            message.maxDamage,
            message.explosionForce,
            message.explosionRadius);
    }

    private void ReceiveTankHealth(
        TanksServerMessage message)
    {
        if (string.IsNullOrWhiteSpace(
                message.loginId) ||
            !remoteTanks.TryGetValue(
                message.loginId,
                out RemoteTankState state) ||
            state.IsDead)
        {
            return;
        }

        state.Health.ApplyNetworkState(
            message.health,
            message.alive);

        if (!message.alive ||
            state.Health.IsDead)
        {
            state.IsDead = true;
        }
    }

    private void ReceivePlayerDead(
        TanksServerMessage message)
    {
        if (string.IsNullOrWhiteSpace(
                message.loginId) ||
            !remoteTanks.TryGetValue(
                message.loginId,
                out RemoteTankState state) ||
            state.IsDead)
        {
            return;
        }

        state.IsDead = true;
        state.Health.ApplyNetworkDeath();
    }

    private void ApplyRemoteTankStates()
    {
        float positionAmount =
            1f - Mathf.Exp(
                -PositionLerpSpeed * Time.deltaTime);

        float rotationAmount =
            1f - Mathf.Exp(
                -RotationLerpSpeed * Time.deltaTime);

        foreach (RemoteTankState state
                 in remoteTanks.Values)
        {
            if (!state.HasReceivedState ||
                state.IsDead ||
                state.Transform == null ||
                !state.Transform.gameObject.activeInHierarchy)
            {
                continue;
            }

            state.Transform.position =
                Vector3.Lerp(
                    state.Transform.position,
                    state.TargetPosition,
                    positionAmount);

            state.Transform.rotation =
                Quaternion.Slerp(
                    state.Transform.rotation,
                    state.TargetRotation,
                    rotationAmount);
        }
    }

    private static void ConfigureRemoteTank(
        TankManager tank)
    {
        TankMovement movement =
            tank.m_Instance.GetComponent<TankMovement>();

        TankShooting shooting =
            tank.m_Instance.GetComponent<TankShooting>();

        TankHealth health =
            tank.m_Instance.GetComponent<TankHealth>();

        TankAI ai =
            tank.m_Instance.GetComponent<TankAI>();

        PowerUpDetector powerUpDetector =
            tank.m_Instance.GetComponent<PowerUpDetector>();

        Rigidbody rigidbody =
            tank.m_Instance.GetComponent<Rigidbody>();

        if (movement != null)
        {
            movement.enabled = false;
        }

        if (shooting != null)
        {
            shooting.enabled = false;
        }

        if (health != null)
        {
            health.SetDamageAuthority(false);
        }

        if (ai != null)
        {
            ai.enabled = false;
        }

        if (powerUpDetector != null)
        {
            powerUpDetector.enabled = false;
        }

        if (rigidbody != null)
        {
            rigidbody.isKinematic = true;
        }
    }

    private void Unsubscribe()
    {
        if (networkClient != null)
        {
            networkClient.MessageReceived -=
                OnServerMessage;
        }

        if (localShooting != null)
        {
            localShooting.LocalShellFired -=
                OnLocalShellFired;
        }

        if (localHealth != null)
        {
            localHealth.HealthChanged -=
                OnLocalHealthChanged;
        }
    }

    private void ResetMatchState()
    {
        initialized = false;
        sendingState = false;
        deathReportQueued = false;

        Unsubscribe();

        remoteTanks.Clear();
        lastRemoteFireSequences.Clear();

        localTank = null;
        localShooting = null;
        localHealth = null;

        matchId = null;
        localLoginId = null;

        networkClient = null;
        gameManager = null;
    }

    private void OnDestroy()
    {
        ResetMatchState();
    }

    private sealed class RemoteTankState
    {
        public RemoteTankState(
            GameObject tankInstance)
        {
            if (tankInstance == null)
            {
                throw new ArgumentNullException(
                    nameof(tankInstance));
            }

            Transform =
                tankInstance.transform;

            Shooting =
                tankInstance.GetComponent<TankShooting>();

            Health =
                tankInstance.GetComponent<TankHealth>();

            if (Shooting == null)
            {
                throw new InvalidOperationException(
                    "Remote tank does not have TankShooting.");
            }

            if (Health == null)
            {
                throw new InvalidOperationException(
                    "Remote tank does not have TankHealth.");
            }

            TargetPosition =
                Transform.position;

            TargetRotation =
                Transform.rotation;
        }

        public Transform Transform { get; }
        public TankShooting Shooting { get; }
        public TankHealth Health { get; }

        public Vector3 TargetPosition { get; set; }
        public Quaternion TargetRotation { get; set; }

        public bool HasReceivedState { get; set; }
        public bool IsDead { get; set; }
    }
}
