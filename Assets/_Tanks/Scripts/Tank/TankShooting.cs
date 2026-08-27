using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System;
namespace Tanks.Complete
{
    public readonly struct TankFireData
    {
        // 다른 클라이언트에서 재현하기 위해 발사의 값을 묶어 전달
        public TankFireData(
            Vector3 position,
            Vector3 velocity,
            float maxDamage,
            float explosionForce,
            float explosionRadius)
        {
            Position = position;
            Velocity = velocity;
            MaxDamage = maxDamage;
            ExplosionForce = explosionForce;
            ExplosionRadius = explosionRadius;
        }

        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float MaxDamage { get; }
        public float ExplosionForce { get; }
        public float ExplosionRadius { get; }
    }

    public class TankShooting : MonoBehaviour
    {
        //로컬 발사했을때 네트워크에 발사정보를 알린다.
        public event Action<TankFireData> LocalShellFired;
        public Rigidbody m_Shell;                   // Prefab of the shell.
        public Transform m_FireTransform;           // A child of the tank where the shells are spawned.
        public Slider m_AimSlider;                  // A child of the tank that displays the current launch force.
        public AudioSource m_ShootingAudio;         // Reference to the audio source used to play the shooting audio. NB: different to the movement audio source.
        public AudioClip m_ChargingClip;            // Audio that plays when each shot is charging up.
        public AudioClip m_FireClip;                // Audio that plays when each shot is fired.
        [Tooltip("The speed in unit/second the shell have when fired at minimum charge")]
        public float m_MinLaunchForce = 5f;        // The force given to the shell if the fire button is not held.
        [Tooltip("The speed in unit/second the shell have when fired at max charge")]
        public float m_MaxLaunchForce = 20f;        // The force given to the shell if the fire button is held for the max charge time.
        [Tooltip("The maximum time spent charging. When charging reach that time, the shell is fired at MaxLaunchForce")]
        public float m_MaxChargeTime = 0.75f;       // How long the shell can charge for before it is fired at max force.
        [Tooltip("The time that must pass before being able to shoot again after a shot")]
        public float m_ShotCooldown = 1.0f;         // The time required between 2 shots
        [Header("Shell Properties")]
        [Tooltip("The amount of health removed to a tank if they are exactly on the landing spot of a shell")]
        public float m_MaxDamage = 100f;                    // The amount of damage done if the explosion is centred on a tank.
        [Tooltip("The force of the explosion at the shell position. Keep it 50 and below")]
        public float m_ExplosionForce = 50f;              // The amount of force added to a tank at the centre of the explosion.
        [Tooltip("The radius of the explosion in Unity unit. Force decrease with distance to the center, and an tank further than this from the shell explosion won't be impacted by the explosion")]
        public float m_ExplosionRadius = 5f;                // The maximum distance away from the explosion tanks can be and are still affected.

        [HideInInspector]
        public TankInputUser m_InputUser;           // The Input User component for that tanks. Contains the Input Actions. 
        
        public float CurrentChargeRatio =>
            (m_CurrentLaunchForce - m_MinLaunchForce) / (m_MaxLaunchForce - m_MinLaunchForce); //The charging amount between 0-1
        public bool IsCharging => m_IsCharging;
        
        public bool m_IsComputerControlled { get; set; } = false;

        private string m_FireButton;                // The input axis that is used for launching shells.
        private float m_CurrentLaunchForce;         // The force that will be given to the shell when the fire button is released.
        private float m_ChargeSpeed;                // How fast the launch force increases, based on the max charge time.
        private bool m_Fired;                       // Whether or not the shell has been launched with this button press.
        private bool m_HasSpecialShell;             // has the tank a shell that makes extra damage?
        private float m_SpecialShellMultiplier;     // The amount that the special shell will multiply the damage.
        private InputAction fireAction;             // The Input Action for shooting, retrieve from TankInputUser
        private bool m_IsCharging = false;          // Are we currently charging the shot
        private float m_BaseMinLaunchForce;         // The initial value of m_MinLaunchForce
        private float m_ShotCooldownTimer;          // The timer counting down before a shot is allowed again
        
        private void OnEnable()
        {
            // When the tank is turned on, reset the launch force, the UI and the power ups
            m_CurrentLaunchForce = m_MinLaunchForce;
            m_BaseMinLaunchForce = m_MinLaunchForce;
            m_AimSlider.value = m_BaseMinLaunchForce;
            m_HasSpecialShell = false;
            m_SpecialShellMultiplier = 1.0f;

            m_AimSlider.minValue = m_MinLaunchForce;
            m_AimSlider.maxValue = m_MaxLaunchForce;
        }

        private void Awake()
        {
            m_InputUser = GetComponent<TankInputUser>();
            if (m_InputUser == null)
                m_InputUser = gameObject.AddComponent<TankInputUser>();
        }

        private void Start ()
        {
            // The fire axis is based on the player number.
            m_FireButton = "Fire";
            fireAction = m_InputUser.ActionAsset.FindAction(m_FireButton);
            
            fireAction.Enable();

            // The rate that the launch force charges up is the range of possible forces by the max charge time.
            m_ChargeSpeed = (m_MaxLaunchForce - m_MinLaunchForce) / m_MaxChargeTime;
        }


        private void Update ()
        {
            // Computer and Human control Tank use 2 different update functions 
            if (!m_IsComputerControlled)
            {
                HumanUpdate();
            }
            else
            {
                ComputerUpdate();
            }
        }

        /// <summary>
        /// Used by AI to start charging
        /// </summary>
        public void StartCharging()
        {
            m_IsCharging = true;
            // ... reset the fired flag and reset the launch force.
            m_Fired = false;
            m_CurrentLaunchForce = m_MinLaunchForce;

            // Change the clip to the charging clip and start it playing.
            m_ShootingAudio.clip = m_ChargingClip;
            m_ShootingAudio.Play ();
        }

        public void StopCharging()
        {
            if (m_IsCharging)
            {
                Fire();
                m_IsCharging = false;
            }
        }

        void ComputerUpdate()
        {
            // The slider should have a default value of the minimum launch force.
            m_AimSlider.value = m_BaseMinLaunchForce;

            // If the max force has been exceeded and the shell hasn't yet been launched...
            if (m_CurrentLaunchForce >= m_MaxLaunchForce && !m_Fired)
            {
                // ... use the max force and launch the shell.
                m_CurrentLaunchForce = m_MaxLaunchForce;
                Fire ();
            }
            // Otherwise, if the fire button is being held and the shell hasn't been launched yet...
            else if (m_IsCharging && !m_Fired)
            {
                // Increment the launch force and update the slider.
                m_CurrentLaunchForce += m_ChargeSpeed * Time.deltaTime;

                m_AimSlider.value = m_CurrentLaunchForce;
            }
            // Otherwise, if the fire button is released and the shell hasn't been launched yet...
            else if (fireAction.WasReleasedThisFrame() && !m_Fired)
            {
                // ... launch the shell.
                Fire ();
                m_IsCharging = false;
            }
        }
        
        void HumanUpdate()
        {
            // if there is a cooldown timer, decrement it
            if (m_ShotCooldownTimer > 0.0f)
            {
                m_ShotCooldownTimer -= Time.deltaTime;
            }
            
            // The slider should have a default value of the minimum launch force.
            m_AimSlider.value = m_BaseMinLaunchForce;

            // If the max force has been exceeded and the shell hasn't yet been launched...
            if (m_CurrentLaunchForce >= m_MaxLaunchForce && !m_Fired)
            {
                // ... use the max force and launch the shell.
                m_CurrentLaunchForce = m_MaxLaunchForce;
                Fire ();
            }
            // Otherwise, if the fire button has just started being pressed...
            else if (m_ShotCooldownTimer <= 0 && fireAction.WasPressedThisFrame())
            {
                // ... reset the fired flag and reset the launch force.
                m_Fired = false;
                m_CurrentLaunchForce = m_MinLaunchForce;

                // Change the clip to the charging clip and start it playing.
                m_ShootingAudio.clip = m_ChargingClip;
                m_ShootingAudio.Play ();
            }
            // Otherwise, if the fire button is being held and the shell hasn't been launched yet...
            else if (fireAction.IsPressed() && !m_Fired)
            {
                // Increment the launch force and update the slider.
                m_CurrentLaunchForce += m_ChargeSpeed * Time.deltaTime;

                m_AimSlider.value = m_CurrentLaunchForce;
            }
            // Otherwise, if the fire button is released and the shell hasn't been launched yet...
            else if (fireAction.WasReleasedThisFrame() && !m_Fired)
            {
                // ... launch the shell.
                Fire ();
            }
        }


        //로컬 포탄 생성 후 발사값을 네트워크 전송용 이벤트로
        private void Fire()
        {
            m_Fired = true;
            //특수 포탄효과 반영한 포탄의 피해량
            float shellDamage = m_MaxDamage;

            if (m_HasSpecialShell)
            {
                shellDamage *= m_SpecialShellMultiplier;

                m_HasSpecialShell = false;
                m_SpecialShellMultiplier = 1f;

                PowerUpDetector powerUpDetector =
                    GetComponent<PowerUpDetector>();

                if (powerUpDetector != null)
                {
                    powerUpDetector.m_HasActivePowerUp = false;
                }

                PowerUpHUD powerUpHUD =
                    GetComponentInChildren<PowerUpHUD>();

                if (powerUpHUD != null)
                {
                    powerUpHUD.DisableActiveHUD();
                }
            }

            Vector3 shellPosition =
                m_FireTransform.position;

            Quaternion shellRotation =
                m_FireTransform.rotation;

            Vector3 shellVelocity =
                m_CurrentLaunchForce *
                m_FireTransform.forward;

            SpawnShell(
                shellPosition,
                shellRotation,
                shellVelocity,
                shellDamage,
                m_ExplosionForce,
                m_ExplosionRadius);

            // 로컬에서만 이벤트를 발생시켜 원격재생이 다시 서버로 전송되는 거 막음
            LocalShellFired?.Invoke(
                new TankFireData(
                    shellPosition,
                    shellVelocity,
                    shellDamage,
                    m_ExplosionForce,
                    m_ExplosionRadius));

            m_ShootingAudio.clip = m_FireClip;
            m_ShootingAudio.Play();

            m_CurrentLaunchForce = m_MinLaunchForce;
            m_ShotCooldownTimer = m_ShotCooldown;
        }

        //로컬 발사와 원격 발사가 동일방식으로 포탄을 생성
        private void SpawnShell(
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            float maxDamage,
            float explosionForce,
            float explosionRadius)
        {
            Rigidbody shellInstance =
                Instantiate(
                    m_Shell,
                    position,
                    rotation);

            shellInstance.linearVelocity = velocity;

            ShellExplosion explosionData =
                shellInstance.GetComponent<ShellExplosion>();

            explosionData.m_MaxDamage = maxDamage;
            explosionData.m_ExplosionForce = explosionForce;
            explosionData.m_ExplosionRadius = explosionRadius;
        }

        //서버에서 받은 상대의 발사를 재현하여 로컬 발사 이벤트는 발생안시킴
        public void ReplayNetworkFire(
            Vector3 position,
            Vector3 velocity,
            float maxDamage,
            float explosionForce,
            float explosionRadius)
        {
            // 속도가 작으면 방향계산이 힘들어 회전을 대신 사용
            Quaternion rotation =
                velocity.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(
                        velocity.normalized)
                    : m_FireTransform.rotation;

            SpawnShell(
                position,
                rotation,
                velocity,
                maxDamage,
                explosionForce,
                explosionRadius);

            m_ShootingAudio.clip = m_FireClip;
            m_ShootingAudio.Play();
        }


        public void EquipSpecialShell(float damageMultiplier)
        {
            m_HasSpecialShell = true;
            m_SpecialShellMultiplier = damageMultiplier;
        }

        /// <summary>
        /// Return the estyimated position the projectile will have with the charging level (between 0 & 1)
        /// </summary>
        /// <param name="chargingLevel">The fire charging level between 0 - 1</param>
        /// <returns>The position at which the projectile will be (ignore obstacle)</returns>
        public Vector3 GetProjectilePosition(float chargingLevel)
        {
            float chargeLevel = Mathf.Lerp (m_MinLaunchForce, m_MaxLaunchForce, chargingLevel);
            Vector3 velocity = chargeLevel * m_FireTransform.forward; 
            
            float a = 0.5f * Physics.gravity.y;
            float b = velocity.y;
            float c = m_FireTransform.position.y;
            
            float sqrtContent = b * b - 4 * a * c;
            //no solution
            if (sqrtContent <= 0)
            {
                return m_FireTransform.position;
            }

            float answer1 = (-b + Mathf.Sqrt(sqrtContent)) / (2 * a);
            float answer2 = (-b - Mathf.Sqrt(sqrtContent)) / (2 * a);

            float answer = answer1 > 0 ? answer1 : answer2;
            
            Vector3 position = m_FireTransform.position +
                               new Vector3(velocity.x, 0, velocity.z) *
                               answer;
            position.y = 0;

            return position;
        }
    }
}
