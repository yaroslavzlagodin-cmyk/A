using UnityEngine;
namespace BrokenHeart.Systems
{
    [CreateAssetMenu(menuName="Broken Heart/Noxel Dialogue")]
    public sealed class NoxelDialogueData : ScriptableObject
    {
        [TextArea] public string[] calmLines;
        [TextArea] public string[] distressedLines;
    }
}
