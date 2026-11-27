using BrokenHeart.Combat;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using UnityEngine;
using UnityEngine.InputSystem;
namespace BrokenHeart.Systems
{
    public sealed class SleepSystem : MonoBehaviour
    {
        [SerializeField] private SanitySystem sanity;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private PlayerVitals vitals;
        [SerializeField] private EnemySystem enemies;
        [SerializeField] private ItemData mindFocus;
        public bool Resting { get; private set; }
        public string Status { get; private set; } = "";
        public event System.Action RestCompleted;
        private float restSeconds;
        private void OnEnable(){vitals.Damaged+=OnDamaged;}
        private void OnDisable(){vitals.Damaged-=OnDamaged; CancelRest();}
        private void OnDamaged(float damage){CancelRest();}
        public bool TryRest()
        {
            if (inventory.Mode != PlayerInputMode.Gameplay) return false;
            if(!motor.Grounded || enemies.Nearby(8)) { Status="Cannot rest: find safe ground"; return false; }
            inventory.CaptureHeldActions();
            Resting=true; restSeconds=0; motor.InputSuppressed=true; Status="Resting... movement cancels"; return true;
        }
        public void CancelRest()
        {
            if (!Resting) return;
            inventory.CaptureHeldActions();
            Resting = false;
            motor.InputSuppressed = Time.timeScale == 0;
        }
        private void Update()
        {
            if(Time.timeScale==0)return;
            var keyboard=Keyboard.current;
            if(keyboard!=null)
            {
                if(keyboard.hKey.wasPressedThisFrame) { if(Resting)CancelRest(); else TryRest(); }
                if(Debug.isDebugBuild && keyboard.f8Key.wasPressedThisFrame)sanity.Adjust(-20);
                if(Resting && (keyboard.aKey.isPressed || keyboard.dKey.isPressed || keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed || keyboard.spaceKey.isPressed))CancelRest();
            }
            if(Resting && (!motor.Grounded || enemies.Nearby(8))) { CancelRest(); Status="Sleep interrupted"; }
            sanity.Advance(Time.deltaTime,Resting,inventory.Selected.Item==mindFocus);
            if(Resting)
            {
                restSeconds+=Time.deltaTime; vitals.Heal(Time.deltaTime*2);
                if (restSeconds >= 8)
                {
                    CancelRest();
                    sanity.RestCompleted();
                    Status = "Rested. H to rest again";
                    RestCompleted?.Invoke();
                }
            }
        }
    }
}
