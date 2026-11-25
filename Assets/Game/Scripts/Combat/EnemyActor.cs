using UnityEngine;
namespace BrokenHeart.Combat
{
    public sealed class EnemyActor : MonoBehaviour
    {
        public EnemyData Data { get; private set; }
        private PlayerVitals target;
        private EnemySystem owner;
        private Rigidbody2D body;
        private float health,attackIn,jumpIn;
        public void Initialize(EnemyData data,PlayerVitals target,EnemySystem owner)
        { Data=data; this.target=target; this.owner=owner; health=data.health; body=GetComponent<Rigidbody2D>(); }
        private void FixedUpdate()
        {
            if(target==null)return;
            float dx=target.transform.position.x-transform.position.x;
            body.linearVelocity=new Vector2(Mathf.Abs(dx)<20?Mathf.Sign(dx)*Data.speed:0,body.linearVelocity.y);
            jumpIn-=Time.fixedDeltaTime; attackIn-=Time.fixedDeltaTime;
            if(jumpIn<=0 && Physics2D.Raycast(transform.position,Vector2.down,0.65f,1))
            { body.linearVelocity=new Vector2(body.linearVelocity.x,6); jumpIn=1.6f; }
            if(Vector2.Distance(transform.position,target.transform.position)<1.3f && attackIn<=0)
            { target.Damage(Data.damage*owner.DamageMultiplier); attackIn=1.2f; }
            if(Vector2.Distance(transform.position,target.transform.position)>60 || transform.position.y < owner.FallBoundary) owner.Remove(this,false);
        }
        public void Damage(float amount) { health-=Mathf.Max(0,amount); if(health<=0)owner.Remove(this,true); }
    }
}
