using UnityEngine;
using System.Collections.Generic;

namespace Tanks.Complete
{
    public class ShellExplosion : MonoBehaviour
    {
        //하나의 폭탄은 한번만 폭발
        private bool m_Exploded;
        public LayerMask m_TankMask;                        // Used to filter what the explosion affects, this should be set to "Players".
        public ParticleSystem m_ExplosionParticles;         // Reference to the particles that will play on explosion.
        public AudioSource m_ExplosionAudio;                // Reference to the audio that will play on explosion.
        [HideInInspector] public float m_MaxLifeTime = 2f;  // The time in seconds before the shell is removed.

        // All those are hidden in inspector as they will actually come from the TankShooting scripts
        [HideInInspector] public float m_MaxDamage = 100f;                    // The amount of damage done if the explosion is centred on a tank.
        [HideInInspector] public float m_ExplosionForce = 50f;                // The amount of force added to a tank at the centre of the explosion.
        [HideInInspector] public float m_ExplosionRadius = 5f;                // The maximum distance away from the explosion tanks can be and are still affected.


        private void Start ()
        {
            // If it isn't destroyed by then, destroy the shell after its lifetime.
            Destroy (gameObject, m_MaxLifeTime);
        }

        //포탄이 충돌하면 폭발 범위 안의 탱크를 찾아 피해와 폭발 힘을 적용한다.
        private void OnTriggerEnter(Collider other)
        {
            if (m_Exploded)
            {
                return;
            }

            m_Exploded = true;
            // 처음 충돌한 대상 뿐만 아니라 반경 안의 모든 탱크를 찾음
            Collider[] colliders =
                Physics.OverlapSphere(
                    transform.position,
                    m_ExplosionRadius,
                    m_TankMask);

            //같은 포탄의 피해는 한번만
            HashSet<TankHealth> damagedTanks = new();

            foreach (Collider hit in colliders)
            {
                Rigidbody targetRigidbody =
                    hit.attachedRigidbody; //자식 Collider에서 탱크 본체에 연결된 rigidbody 가져옴

                if (targetRigidbody == null)
                {
                    continue;
                }

                TankHealth targetHealth =
                    targetRigidbody.GetComponent<TankHealth>();

                //피해 판정 권한이 있는 탱크만 처리
                if (targetHealth == null ||
                    !targetHealth.HasDamageAuthority ||
                    !damagedTanks.Add(targetHealth))
                {
                    continue;
                }

                //폭발힘도 로컬 소유 탱크에만 적용하여 위치충돌 막음
                TankMovement targetMovement =
                    targetRigidbody.GetComponent<TankMovement>();

                if (targetMovement != null)
                {
                    targetMovement.AddExplosionForce(
                        m_ExplosionForce,
                        transform.position,
                        m_ExplosionRadius);
                }

                float damage =
                    CalculateDamage(
                        targetRigidbody.position);

                targetHealth.TakeDamage(damage);
            }

            //파괴된 뒤에도 폭발 입자와 소리가 끝까지 재생되게 분리 
            m_ExplosionParticles.transform.parent = null;
            m_ExplosionParticles.Play();
            m_ExplosionAudio.Play();

            ParticleSystem.MainModule mainModule =
                m_ExplosionParticles.main;

            Destroy(
                m_ExplosionParticles.gameObject,
                mainModule.duration);

            Destroy(gameObject);
        }

        private float CalculateDamage (Vector3 targetPosition)
        {
            // Create a vector from the shell to the target.
            Vector3 explosionToTarget = targetPosition - transform.position;

            // Calculate the distance from the shell to the target.
            float explosionDistance = explosionToTarget.magnitude;

            // Calculate the proportion of the maximum distance (the explosionRadius) the target is away.
            float relativeDistance = (m_ExplosionRadius - explosionDistance) / m_ExplosionRadius;

            // Calculate damage as this proportion of the maximum possible damage.
            float damage = relativeDistance * m_MaxDamage;

            // Make sure that the minimum damage is always 0.
            damage = Mathf.Max (0f, damage);

            return damage;
        }
    }
}
