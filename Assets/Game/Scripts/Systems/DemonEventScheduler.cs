using System;
using UnityEngine;

namespace BrokenHeart.Systems
{
    internal enum DemonEvent { None, Glimpse, Watchers, FalseStone, Stalk, DimLight, Intruder, Rift }

    // One event owns the scene at a time. Cooldowns use scaled gameplay seconds.
    internal sealed class DemonEventScheduler
    {
        private readonly System.Random random;
        private readonly float[] availableAt = new float[(int)DemonEvent.Rift + 1];
        private float elapsed, nextAt;

        internal DemonEventScheduler(int seed)
        {
            random = new System.Random(seed);
            Reset();
        }

        internal void Reset()
        {
            Array.Clear(availableAt, 0, availableAt.Length);
            elapsed = 0;
            nextAt = 15 + (float)random.NextDouble() * 15;
        }

        internal bool Due(float seconds)
        {
            elapsed += seconds;
            return elapsed >= nextAt;
        }

        internal DemonEvent Choose(SanityStage stage, bool night, bool cave, bool lamp, bool canManifest, int level)
        {
            bool dark = night || cave;
            DemonEvent chosen = DemonEvent.None;
            int total = 0;
            void Offer(DemonEvent option, int weight)
            {
                if (availableAt[(int)option] > elapsed) return;
                total += weight;
                if (random.Next(total) < weight) chosen = option;
            }
            if (stage >= SanityStage.Unease && (night || cave && stage >= SanityStage.Watched))
                Offer(DemonEvent.Glimpse, 3);
            if (stage >= SanityStage.Watched)
            {
                if (dark) Offer(DemonEvent.Watchers, 4);
                Offer(DemonEvent.FalseStone, cave ? 5 : 2);
            }
            if (stage >= SanityStage.Pursued)
            {
                Offer(DemonEvent.Stalk, dark ? 6 : 2);
                if (lamp) Offer(DemonEvent.DimLight, 6);
            }
            if (canManifest && dark) Offer(DemonEvent.Intruder, 6 + Mathf.Clamp(level, 1, 5));
            if (stage == SanityStage.Breach && availableAt[(int)DemonEvent.Rift] <= elapsed)
                chosen = DemonEvent.Rift;
            float intensity = (int)stage / 5f;
            nextAt = elapsed + Mathf.Lerp(15, 10, intensity) + (float)random.NextDouble() * 12;
            if (chosen != DemonEvent.None)
                availableAt[(int)chosen] = elapsed + (chosen switch
                {
                    DemonEvent.Intruder => 70,
                    DemonEvent.Rift => 55,
                    DemonEvent.DimLight => 32,
                    _ => 15
                });
            return chosen;
        }

        internal int Side() => random.Next(2) == 0 ? -1 : 1;
    }
}
