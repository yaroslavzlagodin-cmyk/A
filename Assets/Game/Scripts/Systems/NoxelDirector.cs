using UnityEngine;
namespace BrokenHeart.Systems
{
    public sealed class NoxelDirector : MonoBehaviour
    {
        [SerializeField] private SanitySystem sanity;
        [SerializeField] private MemorySystem memory;
        [SerializeField] private NoxelDialogueData dialogue;
        private System.Random random=new System.Random(1977);
        private float nextLine=25,visibleUntil;
        public string CurrentLine { get; private set; }
        public string VisibleLine=>Time.time<visibleUntil?CurrentLine:"";
        public event System.Action<string> Spoken;
        private void OnEnable(){memory.Remembered+=OnMemory;sanity.FirstCollapse+=OnCollapse;}
        private void OnDisable(){memory.Remembered-=OnMemory;sanity.FirstCollapse-=OnCollapse;}
        private void OnMemory(string message){Speak("A memory? Are you quite sure it belongs to you?");}
        private void OnCollapse(){Speak("There. Now you can finally see the company you keep.",true);}
        public void Speak(string line,bool force=false)
        {
            if(!force && Time.time<visibleUntil)return;
            CurrentLine=line;visibleUntil=Time.time+7;Spoken?.Invoke(line);
        }
        private void Update()
        {
            nextLine-=Time.deltaTime;if(nextLine>0)return;
            nextLine=30+random.Next(25);
            var lines=sanity.Value<45?dialogue.distressedLines:dialogue.calmLines;
            if(lines.Length>0)Speak(lines[random.Next(lines.Length)]);
        }
    }
}
