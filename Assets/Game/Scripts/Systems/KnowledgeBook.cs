using System;
using UnityEngine;

namespace BrokenHeart.Systems
{
    public enum Discovery { Observe, Examine, EndureDarkness, SurviveHit, Defeat }
    public enum KnowledgeKind { Creature, Recipe, Story }

    [Serializable]
    public sealed class KnowledgeEntry
    {
        public string id;
        public KnowledgeKind kind;
        public Discovery condition;
        public string prerequisite;
        public int experience;
        public string title, description;
    }

    [CreateAssetMenu(menuName = "Broken Heart/Knowledge Book")]
    public sealed class KnowledgeBook : ScriptableObject
    {
        public KnowledgeEntry[] entries;
    }
}
