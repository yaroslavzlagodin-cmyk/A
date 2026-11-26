using System;
using UnityEngine;

namespace BrokenHeart.Systems
{
    public enum SanityStage { Calm, Unease, Watched, Pursued, Manifestation, Breach }

    public sealed class SanitySystem : MonoBehaviour
    {
        [SerializeField] private float uneaseAt = 70, watchedAt = 50, pursuedAt = 30, manifestationAt = 15;
        [SerializeField, Range(2, 3)] private float recoveryMargin = 2.5f;
        public float Value { get; private set; } = 100;
        public float AwakeSeconds { get; private set; }
        public bool HasReachedZero { get; private set; }
        public SanityStage Stage { get; private set; }
        public bool CanManifest => Stage >= SanityStage.Manifestation && Value <= manifestationAt;
        public event Action<float> Changed;
        public event Action<SanityStage> StageChanged;
        public event Action Restored;
        public event Action FirstCollapse;

        private float Threshold(SanityStage stage) => stage switch
        {
            SanityStage.Unease => uneaseAt,
            SanityStage.Watched => watchedAt,
            SanityStage.Pursued => pursuedAt,
            SanityStage.Manifestation => manifestationAt,
            _ => 0
        };

        private void SetStage(SanityStage start)
        {
            var next = start;
            while (next < SanityStage.Breach && Value <= Threshold(next + 1)) next++;
            while (next > SanityStage.Calm && Value >= Threshold(next) + recoveryMargin) next--;
            if (next == Stage) return;
            Stage = next;
            StageChanged?.Invoke(next);
        }

        public void Advance(float seconds, bool sleeping, bool protectedMind)
        {
            if (seconds <= 0) return;
            if (sleeping)
            {
                AwakeSeconds = Mathf.Max(0, AwakeSeconds - seconds * 10);
                Adjust(seconds * 6);
                return;
            }
            float before = Mathf.Max(0, AwakeSeconds - 90);
            AwakeSeconds += seconds;
            float overdue = Mathf.Max(0, AwakeSeconds - 90) - before;
            if (overdue > 0) Adjust(-overdue * 0.12f * (protectedMind ? 0.25f : 1));
        }

        public void Adjust(float amount)
        {
            float next = Mathf.Clamp(Value + amount, 0, 100);
            if (Value == next) return;
            Value = next;
            SetStage(Stage);
            Changed?.Invoke(Value);
            if (Value == 0 && !HasReachedZero)
            {
                HasReachedZero = true;
                FirstCollapse?.Invoke();
            }
        }

        public void RestCompleted() => AwakeSeconds = 0;

        // A missing stage in an older save is derived from the hero's sanity.
        public void Restore(float value, float awake, bool collapsed, int savedStage = 0)
        {
            Value = Mathf.Clamp(value, 0, 100);
            AwakeSeconds = Mathf.Max(0, awake);
            HasReachedZero = collapsed || Value == 0;
            SetStage(savedStage >= 1 && savedStage <= 6 ? (SanityStage)(savedStage - 1) : SanityStage.Calm);
            Restored?.Invoke();
            Changed?.Invoke(Value);
        }
    }
}
