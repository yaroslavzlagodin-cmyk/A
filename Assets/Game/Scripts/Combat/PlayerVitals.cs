using System;
using BrokenHeart.Inventory;
using BrokenHeart.Systems;
using BrokenHeart.World;
using UnityEngine;

namespace BrokenHeart.Combat
{
    public enum DamageKind { Physical, Mental }

    public readonly struct DamageHit
    {
        public readonly DamageKind Kind;
        public readonly GameObject Source;
        public readonly float Health, Sanity;

        public DamageHit(DamageKind kind, GameObject source, float health, float sanity)
        {
            Kind = kind;
            Source = source;
            Health = Mathf.Max(0, health);
            Sanity = Mathf.Max(0, sanity);
        }
    }

    public sealed class PlayerVitals : MonoBehaviour
    {
        public const float HitGraceSeconds = 0.5f;
        public float Health { get; private set; } = 100;
        public event Action<float> Damaged;
        public event Action<Vector3> Died;
        public float DamageMultiplier { get; set; } = 1;
        private float vulnerableAt;
        private PlayerInventory inventory;
        private SanitySystem sanity;
        private bool resolvingHit;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            sanity = GetComponent<SanitySystem>();
        }

        public void Damage(float amount) =>
            TryDamage(new DamageHit(DamageKind.Physical, null, amount, amount * 0.5f));

        public bool TryDamage(DamageHit hit)
        {
            if (resolvingHit || Time.time < vulnerableAt || Time.timeScale == 0) return false;
            float protection = hit.Kind == DamageKind.Physical ? inventory?.DamageTakenFactor ?? 1 :
                inventory != null && inventory.Selected.Item?.Id == "crystal_focus" ? 0.25f : 1;
            float scale = Mathf.Max(0, DamageMultiplier) * protection;
            float injury = hit.Health * scale;
            float stress = hit.Sanity * scale;
            if (injury <= 0 && stress <= 0) return false;

            vulnerableAt = Time.time + HitGraceSeconds;
            resolvingHit = true;
            try
            {
                Health = Mathf.Max(0, Health - injury);
                sanity?.Adjust(-stress);
                Damaged?.Invoke(injury > 0 ? injury : stress);
                if (Health <= 0)
                {
                    Vector3 deathPosition = transform.position;
                    var spawn = new Vector3(0.5f, 4, 0);
                    FindAnyObjectByType<BlockWorld>()?.PrepareCollisionAt(spawn);
                    transform.position = spawn;
                    if (TryGetComponent<Rigidbody2D>(out var body))
                    {
                        body.position = spawn;
                        body.linearVelocity = Vector2.zero;
                    }
                    Health = 100;
                    Died?.Invoke(deathPosition);
                }
            }
            finally { resolvingHit = false; }
            return true;
        }

        public void Restore(float health)
        {
            Health = Mathf.Clamp(health, 1, 100);
            vulnerableAt = 0;
        }

        public void Heal(float amount) => Health = Mathf.Min(100, Health + Mathf.Max(0, amount));
    }
}
