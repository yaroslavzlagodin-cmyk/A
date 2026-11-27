using System.Collections.Generic;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.Systems;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.InputSystem;
namespace BrokenHeart.Combat
{
    public sealed class EnemySystem : MonoBehaviour
    {
        [SerializeField] private BlockWorld world;
        [SerializeField] private PlayerVitals player;
        [SerializeField] private DayNightSystem clock;
        [SerializeField] private EnemyData[] types;
        [SerializeField] private Material material;
        // Keep the enemy data in the project while encounters are paused.
        [SerializeField] private bool encountersEnabled = false;
        private readonly List<EnemyActor> actors=new();
        private System.Random random;
        private PlayerInventory inventory;
        private PlayerMotor motor;
        private BrokenHeart.Systems.Energy.EnergyNetwork energy;
        private int solidMask;
        private SpriteRenderer playerSprite;
        private SpriteRenderer weaponSwing;
        private Vector2 swingDirection;
        private float swingTime;
        private float spawnIn=18,attackIn;
        public IReadOnlyList<EnemyActor> Actors=>actors;
        public event System.Action<EnemyActor> Defeated;
        public EnemyData[] Types=>types;
        public bool Peaceful { get; set; }
        public float DamageMultiplier { get; set; } = 1;
        public float FallBoundary=>world.Bounds.yMin-16;
        private bool EncountersActive => encountersEnabled && isActiveAndEnabled;
        private void Awake()
        {
            random = new System.Random(world.Seed ^ 8221);
            inventory = player.GetComponent<PlayerInventory>();
            motor = player.GetComponent<PlayerMotor>();
            energy = GetComponent<BrokenHeart.Systems.Energy.EnergyNetwork>();
            solidMask = 1 << world.Map.gameObject.layer;
            playerSprite = player.GetComponent<SpriteRenderer>();
            var visual = new GameObject("Weapon Swing", typeof(SpriteRenderer));
            visual.transform.SetParent(player.transform, false);
            weaponSwing = visual.GetComponent<SpriteRenderer>();
            weaponSwing.sharedMaterial = material;
            weaponSwing.sortingOrder = 14;
            weaponSwing.enabled = false;
        }

        public bool Eligible(EnemyData data, Vector2 position)
        {
            if (!EncountersActive || Peaceful) return false;
            var cell = world.CellAt(position);
            int depth = world.DepthAt(cell);
            return data.underground ? depth > 6 :
                depth <= 1 && clock.IsNight && world.Generator.Biome(cell.x) == data.biome;
        }

        private void Update()
        {
            if (Time.timeScale == 0) return;
            attackIn = Mathf.Max(0, attackIn - Time.deltaTime);
            AnimateSwing();
            if (!EncountersActive && actors.Count == 0) return;
            if (inventory.CanUseAttackKey && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) TryAttack(Vector2.zero);
            if (!EncountersActive) return;
            spawnIn -= Time.deltaTime;
            if(spawnIn>0)return;
            spawnIn=12+random.Next(8);
            if (!clock.IsNight)
                for (int i = actors.Count - 1; i >= 0; i--)
                    if (!actors[i].Data.underground) Remove(actors[i], false);
            if(actors.Count>=6 || Peaceful)return;

            var origin = world.CellAt(player.transform.position);
            bool underground = world.DepthAt(origin) > 6;
            foreach (var type in types)
            {
                if (type.underground != underground || (!underground && !clock.IsNight)) continue;
                for (int attempt = 0; attempt < 9; attempt++)
                {
                    int x = origin.x + (random.Next(2) == 0 ? -1 : 1) *
                        (underground ? random.Next(8, 18) : random.Next(14, 23));
                    int y = underground ? origin.y + random.Next(-5, 6) : world.Generator.Surface(x) + 1;
                    for (int offset = 0; offset < (underground ? 4 : 1); offset++)
                    {
                        var cell = new Vector2Int(x, y - offset);
                        var position = (Vector2)world.Center(cell) + Vector2.up * 0.12f;
                        if (!world.Bounds.Contains(cell) || !Eligible(type, position) ||
                            world.Get(cell + Vector2Int.down)?.Solid != true ||
                            Physics2D.OverlapBox(position, new Vector2(0.85f, 1.1f), 0, 1)) continue;
                        Spawn(type, position);
                        return;
                    }
                }
            }
        }

        public bool TryAttack(Vector2 aim)
        {
            if ((!EncountersActive && actors.Count == 0) || Time.timeScale == 0 || attackIn > 0 || motor.InputSuppressed ||
                inventory.Mode != PlayerInputMode.Gameplay)
                return false;
            var item = inventory.Selected.Item;
            bool armed = item != null && item.AttackDamage > 0;
            float damage = armed ? item.AttackDamage : 15;
            float reach = armed ? item.AttackReach : 2.5f;
            Vector2 origin = player.transform.position;
            aim.Normalize();
            EnemyActor target = null;
            Vector2 hitDirection = aim;
            float closest = reach * reach;
            foreach (var actor in actors)
            {
                Vector2 delta = (Vector2)actor.transform.position - origin;
                float distance = delta.sqrMagnitude;
                if (distance > closest || (aim != Vector2.zero && Vector2.Dot(aim, delta.normalized) < 0.5f) ||
                    !HasSight(origin, origin + delta)) continue;
                target = actor;
                closest = distance;
                hitDirection = delta.normalized;
            }
            attackIn = armed ? item.SwingInterval : 0.45f;
            if (armed && item.Icon != null)
            {
                swingDirection = hitDirection != Vector2.zero ? hitDirection :
                    playerSprite != null && playerSprite.flipX ? Vector2.left : Vector2.right;
                swingTime = 0.18f;
                weaponSwing.sprite = item.Icon;
                weaponSwing.enabled = true;
            }
            if (target != null) target.Damage(damage);
            return true;
        }

        internal bool HasSight(Vector2 from, Vector2 to) => !Physics2D.Linecast(from, to, solidMask);

        internal bool FindStrongLight(Vector2 position, float radius, out float lightX)
        {
            Vector2 hero = player.transform.position;
            lightX = hero.x;
            if (clock.SurfaceIntensity >= 0.7f && world.DepthAt(world.CellAt(position)) <= 1) return true;
            float nearest = radius * radius;
            bool found = !inventory.Equipped((int)EquipmentSlot.Lamp - 1).Empty &&
                (hero - position).sqrMagnitude < nearest && HasSight(position, hero);
            if (found) nearest = (hero - position).sqrMagnitude;
            if (energy == null) return found;
            foreach (var device in energy.Devices)
            {
                if (!device.Powered || device.Data.wardRadius > 0 || device.IsDimmed || device.Data.consumptionPerSecond <= 0) continue;
                Vector2 source = device.transform.position;
                float distance = (source - position).sqrMagnitude;
                if (distance >= nearest || !HasSight(position, source)) continue;
                nearest = distance;
                lightX = source.x;
                found = true;
            }
            return found;
        }

        private void AnimateSwing()
        {
            if (!weaponSwing.enabled) return;
            swingTime -= Time.deltaTime;
            if (swingTime <= 0) { weaponSwing.enabled = false; return; }
            float progress = 1 - swingTime / 0.18f;
            float angle = Mathf.Atan2(swingDirection.y, swingDirection.x) * Mathf.Rad2Deg;
            angle += Mathf.Lerp(-65, 65, progress) * (swingDirection.x >= 0 ? 1 : -1);
            weaponSwing.transform.localPosition = Quaternion.Euler(0, 0, angle) * Vector3.right * 0.82f;
            weaponSwing.transform.localRotation = Quaternion.Euler(0, 0, angle - 45);
        }
        public EnemyActor Spawn(EnemyData data,Vector2 position)
        {
            if (!EncountersActive) return null;
            return CreateActor(data, position);
        }

        internal EnemyActor Manifest(EnemyData data, Vector2 position, SanitySystem sanity)
        {
            if (!isActiveAndEnabled || Peaceful || !sanity.CanManifest) return null;
            return CreateActor(data, position);
        }

        private EnemyActor CreateActor(EnemyData data, Vector2 position)
        {
            if (data == null || actors.Count >= 6) return null;
            var go=new GameObject(data.name,typeof(SpriteRenderer),typeof(Rigidbody2D),typeof(BoxCollider2D)); go.layer=2; go.transform.position=position;
            var visual=go.GetComponent<SpriteRenderer>(); visual.sprite=data.sprite; visual.sharedMaterial=material; visual.sortingOrder=9;
            var body=go.GetComponent<Rigidbody2D>(); body.gravityScale=2; body.constraints=RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode=CollisionDetectionMode2D.Continuous; go.GetComponent<BoxCollider2D>().size=new Vector2(0.85f,0.85f);
            var actor=go.AddComponent<EnemyActor>(); actor.Initialize(data,player,this); actors.Add(actor); return actor;
        }
        public void Remove(EnemyActor actor,bool loot)
        {
            if(!actors.Remove(actor))return;
            if (loot)
            {
                world.GetComponent<DropSpawner>().Spawn(actor.Data.drop, 1, actor.transform.position, player.GetComponent<Collider2D>());
                inventory.Progress.Gain(20);
                Defeated?.Invoke(actor);
            }
            actor.gameObject.SetActive(false); Destroy(actor.gameObject);
        }
        public void Clear()
        {
            for (int i = actors.Count - 1; i >= 0; i--) Remove(actors[i], false);
            attackIn = 0;
            if (weaponSwing != null) weaponSwing.enabled = false;
        }
        private void OnDisable() => Clear();

        public bool Nearby(float radius)
        { foreach(var actor in actors)if(Vector2.Distance(actor.transform.position,player.transform.position)<radius)return true; return false; }
    }
}
