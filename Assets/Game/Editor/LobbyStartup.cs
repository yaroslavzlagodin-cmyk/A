using UnityEditor;
using UnityEditor.SceneManagement;

namespace BrokenHeart.Editor
{
    [InitializeOnLoad]
    internal static class LobbyStartup
    {
        static LobbyStartup()
        {
            EditorApplication.delayCall += UseLobby;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) UseLobby();
            };
        }

        internal static void UseLobby()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var lobby = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuSetup.Path);
            if (lobby != null) EditorSceneManager.playModeStartScene = lobby;
        }
    }
}
