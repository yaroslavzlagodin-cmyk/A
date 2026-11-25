using System;

namespace BrokenHeart.Player
{
    public sealed class PlayerProgress
    {
        public const int ExperiencePerLevel = 100;
        public int TotalExperience { get; private set; }
        public int Level => 1 + TotalExperience / ExperiencePerLevel;
        public int ExperienceInLevel => TotalExperience % ExperiencePerLevel;

        internal void Gain(int amount)
        {
            TotalExperience = (int)Math.Min(int.MaxValue, (long)TotalExperience + amount);
        }

        internal void Restore(int experience) => TotalExperience = experience;
    }
}
