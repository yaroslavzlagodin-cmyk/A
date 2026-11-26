using UnityEngine;

namespace BrokenHeart.Combat
{
    public enum EnemyState { Idle, Patrol, Chase, Windup, Attack, Recovery, Dead }

    public sealed class EnemyActor : MonoBehaviour
    {
        public EnemyData Data { get; private set; }
        public EnemyState State { get; private set; }
        private PlayerVitals target;
        private EnemySystem owner;
        private Rigidbody2D body;
        private SpriteRenderer sprite;
        private Color normalColor;
        private float health, timer, homeX, patrolDirection = 1, strikeDirection;

        public void Initialize(EnemyData data, PlayerVitals player, EnemySystem system)
        {
            Data = data;
            target = player;
            owner = system;
            health = data.health;
            body = GetComponent<Rigidbody2D>();
            sprite = GetComponent<SpriteRenderer>();
            normalColor = sprite.color;
            homeX = body.position.x;
            Enter(EnemyState.Idle, 0.6f);
        }

        private void FixedUpdate()
        {
            if (target == null || State == EnemyState.Dead) return;
            Vector2 position = body.position;
            Vector2 hero = target.transform.position;
            Vector2 offset = hero - position;
            float distance = offset.magnitude;
            if (distance > 60 || position.y < owner.FallBoundary) { owner.Remove(this, false); return; }
            timer -= Time.fixedDeltaTime;
            if (!Data.underground && owner.FindStrongLight(position, Data.lightRadius, out float lightX))
            {
                Enter(EnemyState.Patrol);
                Walk(Mathf.Sign(position.x - lightX) * Data.speed);
                return;
            }
            bool visible = distance <= Data.sightRange && owner.HasSight(position, hero);
            switch (State)
            {
                case EnemyState.Windup:
                    if (timer <= 0) Enter(EnemyState.Attack, Data.underground ? Data.dashDuration : 0);
                    return;
                case EnemyState.Attack:
                    // Commit to the warned direction. A dodge or a newly placed wall can cancel the hit.
                    if (visible && distance <= Data.hitReach && offset.x * strikeDirection >= -0.1f)
                    {
                        float scale = owner.DamageMultiplier;
                        target.TryDamage(new DamageHit(Data.damageKind, gameObject, Data.damage * scale, Data.sanityDamage * scale));
                        Enter(EnemyState.Recovery, Data.recovery);
                    }
                    else if (!Data.underground || !visible || timer <= 0) Enter(EnemyState.Recovery, Data.recovery);
                    else Walk(strikeDirection * Data.dashSpeed);
                    return;
                case EnemyState.Recovery:
                    if (timer <= 0) Enter(EnemyState.Idle);
                    return;
            }
            if (visible && distance <= (Data.underground ? Data.ambushRange : Data.hitReach))
            {
                strikeDirection = Mathf.Sign(offset.x);
                Enter(EnemyState.Windup, Data.windup);
            }
            else if (Data.underground) Enter(EnemyState.Idle);
            else if (visible)
            {
                State = EnemyState.Chase;
                Walk(Mathf.Sign(offset.x) * Data.speed);
            }
            else if (State != EnemyState.Idle || timer <= 0)
            {
                if (Mathf.Abs(position.x - homeX) >= Data.patrolRadius && (position.x - homeX) * patrolDirection > 0)
                {
                    patrolDirection *= -1;
                    Enter(EnemyState.Idle, 0.65f);
                }
                else
                {
                    State = EnemyState.Patrol;
                    Walk(patrolDirection * Data.speed * 0.45f);
                }
            }
        }

        private void Enter(EnemyState next, float seconds = 0)
        {
            State = next;
            timer = seconds;
            sprite.color = next == EnemyState.Windup ? Data.warningColor : normalColor;
            Walk(0);
        }

        private void Walk(float speed)
        {
            body.linearVelocity = new Vector2(speed, body.linearVelocity.y);
            if (speed != 0) sprite.flipX = speed < 0;
        }

        public void Damage(float amount)
        {
            if (State == EnemyState.Dead || amount <= 0) return;
            health -= amount;
            if (health > 0) return;
            Enter(EnemyState.Dead);
            owner.Remove(this, true);
        }
    }
}
