using UnityEngine;
namespace BrokenHeart.Combat
{
    public sealed class PlayerVitals : MonoBehaviour
    {
        public float Health { get; private set; } = 100;
        public event System.Action<float> Damaged;
        public event System.Action<Vector3> Died;
        public float DamageMultiplier { get; set; } = 1;
        public void Damage(float amount)
        {
            if(amount<=0)return;
            amount *= DamageMultiplier * (GetComponent<BrokenHeart.Inventory.PlayerInventory>()?.DamageTakenFactor ?? 1);
            Health=Mathf.Max(0,Health-amount); Damaged?.Invoke(amount);
            if(Health<=0)
            {
                Vector3 deathPosition=transform.position;
                transform.position=new Vector3(0.5f,4,0); GetComponent<Rigidbody2D>().position=transform.position;
                GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero; Health=100;
                Died?.Invoke(deathPosition);
            }
        }
        public void Restore(float health) { Health=Mathf.Clamp(health,1,100); }
        public void Heal(float amount) { Health=Mathf.Min(100,Health+Mathf.Max(0,amount)); }
    }
}
