using UnityEngine;
using UnityEngine.UI;
using System;

namespace Tanks.Complete
{
    public class TankHealth : MonoBehaviour
    {
        public float m_StartingHealth = 100f;               // The amount of health each tank starts with.
        public Slider m_Slider;                             // The slider to represent how much health the tank currently has.
        public Image m_FillImage;                           // The image component of the slider.
        public Color m_FullHealthColor = Color.green;    // The color the health bar will be when on full health.
        public Color m_ZeroHealthColor = Color.red;      // The color the health bar will be when on no health.
        public GameObject m_ExplosionPrefab;                // A prefab that will be instantiated in Awake, then used whenever the tank dies.
        [HideInInspector] public bool m_HasShield;          // Has the tank picked up a shield power up?
        // 체력 바뀌면 네트워크에 알림
        public event Action<float, bool> HealthChanged;
        //살아있는 탱크만 피해와 회복을 계산
        public bool HasDamageAuthority { get; private set; } = true;
        //네트워크 전송에 사용할 현재 체력과 사망 상태를 읽기전용
        public float CurrentHealth => m_CurrentHealth;
        public bool IsDead => m_Dead;                
        private AudioSource m_ExplosionAudio;               // The audio source to play when the tank explodes.
        private ParticleSystem m_ExplosionParticles;        // The particle system the will play when the tank is destroyed.
        private float m_CurrentHealth;                      // How much health the tank currently has.
        private bool m_Dead;                                // Has the tank been reduced beyond zero health yet?
        private float m_ShieldValue;                        // Percentage of reduced damage when the tank has a shield.
        private bool m_IsInvincible;                        // Is the tank invincible in this moment?

        private void Awake ()
        {
            // Instantiate the explosion prefab and get a reference to the particle system on it.
            m_ExplosionParticles = Instantiate (m_ExplosionPrefab).GetComponent<ParticleSystem> ();

            // Get a reference to the audio source on the instantiated prefab.
            m_ExplosionAudio = m_ExplosionParticles.GetComponent<AudioSource> ();

            // Disable the prefab so it can be activated when it's required.
            m_ExplosionParticles.gameObject.SetActive (false);
            
            // Set the slider max value to the max health the tank can have
            m_Slider.maxValue = m_StartingHealth;
        }

        private void OnDestroy()
        {
            if(m_ExplosionParticles != null)
                Destroy(m_ExplosionParticles.gameObject);
        }

        private void OnEnable()
        {
            // When the tank is enabled, reset the tank's health and whether or not it's dead.
            m_CurrentHealth = m_StartingHealth;
            m_Dead = false;
            m_HasShield = false;
            m_ShieldValue = 0;
            m_IsInvincible = false;

            // Update the health slider's value and color.
            SetHealthUI();
        }
        //로컬 탱크에만 피해 판정 권한
        public void SetDamageAuthority(
            bool hasAuthority)
        {
            HasDamageAuthority = hasAuthority;
        }

        //서버에 전달된 원격 탱크의 생존 상태를 화면에 반영
        public void ApplyNetworkState(
            float health,
            bool alive)
        {
            //로컬과 죽은 탱크는 적용하지 않음
            if (HasDamageAuthority ||
                m_Dead ||
                float.IsNaN(health) ||
                float.IsInfinity(health))
            {
                return;
            }

            m_CurrentHealth =
                alive
                    ? Mathf.Clamp(
                        health,
                        0f,
                        m_StartingHealth)
                    : 0f;

            SetHealthUI();

            if (!alive ||
                m_CurrentHealth <= 0f)
            {
                OnDeath();
            }
        }
        //player_dead 메시지도 사망처리와 같은 경로
        public void ApplyNetworkDeath()
        {
            ApplyNetworkState(0f, false);
        }

        //로컬 탱크만 피해를 계산하고 결과를 이벤트로
        public void TakeDamage(float amount)
        {
            if (!HasDamageAuthority ||
                m_Dead ||
                m_IsInvincible ||
                amount <= 0f ||
                float.IsNaN(amount) ||
                float.IsInfinity(amount))
            {
                return;
            }

            float previousHealth =
                m_CurrentHealth;

            m_CurrentHealth =
                Mathf.Max(
                    0f,
                    m_CurrentHealth -
                    amount * (1f - m_ShieldValue));

            if (Mathf.Approximately(
                    previousHealth,
                    m_CurrentHealth))
            {
                return;
            }

            SetHealthUI();
            //사망을 확정해야 죽음 값이 나옴
            if (m_CurrentHealth <= 0f)
            {
                OnDeath();
            }

            HealthChanged?.Invoke(
                m_CurrentHealth,
                !m_Dead);
        }

        //탱크를 소유한 클라이언트에서만 계산한 뒤 다른 클라에 동기화
        public void IncreaseHealth(float amount)
        {
            if (!HasDamageAuthority ||
                m_Dead ||
                amount <= 0f ||
                float.IsNaN(amount) ||
                float.IsInfinity(amount))
            {
                return;
            }

            float previousHealth =
                m_CurrentHealth;

            m_CurrentHealth =
                Mathf.Min(
                    m_StartingHealth,
                    m_CurrentHealth + amount);

            if (Mathf.Approximately(
                    previousHealth,
                    m_CurrentHealth))
            {
                return;
            }

            SetHealthUI();

            HealthChanged?.Invoke(
                m_CurrentHealth,
                true);
        }

        //원격의 방어 상태가 이 클라이언트의 파워업 충돌로 변경되는 것을 막음 
        public void ToggleShield (float shieldAmount)
        {
            if (!HasDamageAuthority || m_Dead)
            {
                return;
            }
            // Inverts the value of has shield.
            m_HasShield = !m_HasShield;

            // Stablish the amount of damage that will be reduced by the shield
            if (m_HasShield)
            {
                m_ShieldValue = shieldAmount;
            }
            else
            {
                m_ShieldValue = 0;
            }
        }

        //무적상태도 소유 클라이언트에서만
        public void ToggleInvincibility()
        {
            if (!HasDamageAuthority || m_Dead)
            {
                return;
            }
            m_IsInvincible = !m_IsInvincible;
        }


        private void SetHealthUI ()
        {
            // Set the slider's value appropriately.
            m_Slider.value = m_CurrentHealth;

            // Interpolate the color of the bar between the choosen colours based on the current percentage of the starting health.
            m_FillImage.color = Color.Lerp (m_ZeroHealthColor, m_FullHealthColor, m_CurrentHealth / m_StartingHealth);
        }

        //체력 패킷과 사망 패킷이 같이와도 사망연출은 한번만
        private void OnDeath()
        {
            if (m_Dead)
            {
                return;
            }

            m_Dead = true;

            m_ExplosionParticles.transform.position =
                transform.position;

            m_ExplosionParticles.gameObject.SetActive(true);
            m_ExplosionParticles.Play();
            m_ExplosionAudio.Play();

            gameObject.SetActive(false);
        }
    }
}
