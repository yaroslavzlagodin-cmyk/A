using BrokenHeart.Combat;
using BrokenHeart.Inventory;
using BrokenHeart.Systems.Energy;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BrokenHeart.Systems
{
    public sealed class SanityVisuals : MonoBehaviour
    {
        [SerializeField] private SanitySystem sanity;
        [SerializeField] private Transform observer;
        [SerializeField] private BlockWorld world;
        [SerializeField] private Sprite eyeSprite, shadowSprite;
        [SerializeField] private Material material;
        [SerializeField] private EnemyData manifestation;
        [SerializeField] private bool hallucinationsEnabled = true;

        private readonly SpriteRenderer[] eyes = new SpriteRenderer[4];
        private SpriteRenderer shadow, phantom;
        private DemonEventScheduler scheduler;
        private DayNightSystem clock;
        private EnemySystem enemies;
        private EnergyNetwork network;
        private PlayerInventory inventory;
        private PlayerVitals vitals;
        private MemorySystem memory;
        private bool inspectedLamp;
        private EnemyActor intruder;
        private EnergyDevice dimmed;
        private DemonEvent active;
        private Vector3 shadowOrigin;
        private float remaining, age;

        public bool EyesVisible => eyes[0] != null && eyes[0].gameObject.activeSelf;
        private void Awake()
        {
            clock = world.GetComponent<DayNightSystem>();
            enemies = world.GetComponent<EnemySystem>();
            network = world.GetComponent<EnergyNetwork>();
            inventory = observer.GetComponent<PlayerInventory>();
            vitals = observer.GetComponent<PlayerVitals>();
            memory = observer.GetComponent<MemorySystem>();
            scheduler = new DemonEventScheduler(world.Seed ^ 149);
            if (!hallucinationsEnabled) return;
            for (int i = 0; i < eyes.Length; i++) eyes[i] = Create("Unreal eyes", eyeSprite, Color.white);
            shadow = Create("Moving shadow", shadowSprite, new Color(0.66f, 0.53f, 0.72f, 0.75f));
            shadow.transform.localScale = new Vector3(1.2f, 2.2f, 1);
            phantom = Create("False stone", world.Palette[2].Sprite, new Color(0.82f, 0.78f, 0.86f, 0.85f));
        }

        private SpriteRenderer Create(string label, Sprite sprite, Color color)
        {
            var go = new GameObject(label, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            var visual = go.GetComponent<SpriteRenderer>();
            visual.sprite = sprite;
            visual.sharedMaterial = material;
            visual.color = color;
            visual.sortingOrder = 15;
            go.SetActive(false);
            return visual;
        }

        private void OnEnable()
        {
            sanity.Restored += ResetEvents;
            sanity.StageChanged += OnStage;
            vitals.Died += OnDeath;
            enemies.Defeated += OnDefeated;
            vitals.SurvivedHit += OnSurvivedHit;
        }
        private void OnDisable()
        {
            sanity.Restored -= ResetEvents;
            sanity.StageChanged -= OnStage;
            vitals.Died -= OnDeath;
            enemies.Defeated -= OnDefeated;
            vitals.SurvivedHit -= OnSurvivedHit;
            ResetEvents();
        }
        private void OnDefeated(EnemyActor actor)
        {
            if (actor == intruder && sanity.CanManifest)
                memory.Record(Discovery.Defeat, world.CellAt(actor.transform.position));
        }

        private void OnSurvivedHit(DamageHit hit)
        {
            if (intruder != null && hit.Source == intruder.gameObject)
                memory.Record(Discovery.SurviveHit, world.CellAt(observer.position));
        }

        private bool Near(Transform target, float reach) =>
            Vector2.Distance(observer.position, target.position) <= reach && enemies.HasSight(observer.position, target.position);

        public bool TryResearch()
        {
            if (inventory.Mode != PlayerInputMode.Gameplay) return false;
            var cell = world.CellAt(observer.position);
            if (active == DemonEvent.Glimpse && sanity.InResearchRange(SanityStage.Unease) &&
                shadow.gameObject.activeSelf && Near(shadow.transform, 10))
                memory.Record(Discovery.Observe, cell);
            else if (active == DemonEvent.FalseStone && sanity.InResearchRange(SanityStage.Watched) &&
                phantom.gameObject.activeSelf && Near(phantom.transform, 5))
                memory.Record(Discovery.Examine, cell);
            else if (active == DemonEvent.DimLight && sanity.InResearchRange(SanityStage.Pursued) &&
                dimmed != null && dimmed.Powered && dimmed.IsDimmed && Vector2.Distance(observer.position, dimmed.transform.position) <= 5)
                inspectedLamp = true;
            else return false;
            return true;
        }

        private DemonEvent ResearchOpportunity() => memory.TrailStep switch
        {
            0 when sanity.InResearchRange(SanityStage.Unease) => DemonEvent.Glimpse,
            1 when sanity.InResearchRange(SanityStage.Watched) => DemonEvent.FalseStone,
            2 when sanity.InResearchRange(SanityStage.Pursued) && !memory.HasKnowledge("lore.darkness") => DemonEvent.DimLight,
            3 when sanity.CanManifest => DemonEvent.Intruder,
            _ => DemonEvent.None
        };

        private void OnDeath(Vector3 _) => ResetEvents();
        private void OnStage(SanityStage stage)
        {
            if (stage < MinimumStage(active)) EndEvent();
        }

        private static SanityStage MinimumStage(DemonEvent effect) => effect switch
        {
            DemonEvent.Glimpse => SanityStage.Unease,
            DemonEvent.Watchers or DemonEvent.FalseStone => SanityStage.Watched,
            DemonEvent.Stalk or DemonEvent.DimLight => SanityStage.Pursued,
            DemonEvent.Intruder => SanityStage.Manifestation,
            DemonEvent.Rift => SanityStage.Breach,
            _ => SanityStage.Calm
        };

        private void ResetEvents()
        {
            EndEvent();
            scheduler.Reset();
        }

        private void EndEvent()
        {
            foreach (var eye in eyes) if (eye != null) eye.gameObject.SetActive(false);
            if (shadow != null) shadow.gameObject.SetActive(false);
            if (phantom != null) phantom.gameObject.SetActive(false);
            if (dimmed != null) dimmed.RestoreLight();
            if (intruder != null && enemies != null) enemies.Remove(intruder, false);
            intruder = null;
            dimmed = null;
            inspectedLamp = false;
            active = DemonEvent.None;
        }

        private EnergyDevice NearbyLamp()
        {
            EnergyDevice nearest = null;
            float limit = 9 * 9;
            foreach (var device in network.Devices)
            {
                float distance = (device.transform.position - observer.position).sqrMagnitude;
                if (!device.Powered || device.Data.wardRadius > 0 || device.Data.consumptionPerSecond <= 0 || distance > limit) continue;
                nearest = device;
                limit = distance;
            }
            return nearest;
        }

        // Find a visible free cell beside the hero; physical visitors also need a solid floor.
        private bool FindPosition(int offset, bool grounded, out Vector3 position)
        {
            var origin = world.CellAt(observer.position);
            for (int x = 0; x < 3; x++)
                for (int height = 0; height < 9; height++)
                {
                    int y = (height + 1) / 2 * (height % 2 == 1 ? -1 : 1);
                    var cell = origin + new Vector2Int(offset + x * System.Math.Sign(offset), y);
                    if (!world.Bounds.Contains(cell) || world.IsSolidAt(cell) ||
                        (grounded && !world.IsSolidAt(cell + Vector2Int.down))) continue;
                    position = world.Center(cell);
                    if (enemies.HasSight(observer.position, position) &&
                        (!grounded || !Physics2D.OverlapBox(position, new Vector2(0.85f, 0.85f), 0, 1))) return true;
                }
            position = default;
            return false;
        }

        private static void Reveal(SpriteRenderer visual, Vector3 position)
        {
            visual.transform.position = position;
            visual.gameObject.SetActive(true);
        }

        private void StartEvent(DemonEvent effect, EnergyDevice lamp)
        {
            if (effect == DemonEvent.None) return;
            int side = scheduler.Side();
            bool physical = effect == DemonEvent.Intruder;
            if (effect == DemonEvent.DimLight)
            {
                if (lamp == null) return;
                dimmed = lamp;
                lamp.DimFor(5);
            }
            else
            {
                if (!FindPosition(side * 6, physical, out var pos) &&
                    !FindPosition(-side * 6, physical, out pos)) return;
                switch (effect)
                {
                    case DemonEvent.Intruder:
                        intruder = enemies.Manifest(manifestation, pos, sanity);
                        if (intruder == null) return;
                        break;
                    case DemonEvent.FalseStone:
                        Reveal(phantom, pos);
                        break;
                    case DemonEvent.Glimpse:
                    case DemonEvent.Watchers:
                    case DemonEvent.Rift:
                        Reveal(eyes[0], pos);
                        int count = effect == DemonEvent.Rift ? 4 : effect == DemonEvent.Watchers ? 3 : 1;
                        for (int i = 1; i < count; i++)
                            if (FindPosition((i % 2 == 0 ? side : -side) * (4 + i), false, out var eyePos))
                                Reveal(eyes[i], eyePos + Vector3.up * (0.2f * i));
                        if (effect == DemonEvent.Watchers) break;
                        if (FindPosition(-side * 5, false, out var other)) pos = other;
                        goto case DemonEvent.Stalk;
                    case DemonEvent.Stalk:
                        shadowOrigin = pos;
                        Reveal(shadow, pos);
                        break;
                }
            }
            active = effect;
            age = 0;
            remaining = effect switch
            {
                DemonEvent.Intruder => 9,
                DemonEvent.DimLight => 5 * lamp.Data.dimDurationFactor,
                DemonEvent.Rift => 8,
                _ => 6
            };
        }

        private void RefreshEffects(float seconds)
        {
            foreach (var eye in eyes)
                if (eye.gameObject.activeSelf && Vector2.Distance(eye.transform.position, observer.position) < 3)
                    eye.gameObject.SetActive(false);
            if (phantom.gameObject.activeSelf && Vector2.Distance(phantom.transform.position, observer.position) < 2.5f)
                phantom.gameObject.SetActive(false);
            if (!shadow.gameObject.activeSelf) return;
            if (active == DemonEvent.Stalk)
            {
                shadow.transform.position = Vector3.MoveTowards(shadow.transform.position, observer.position, seconds * 2.4f);
                if (Vector2.Distance(shadow.transform.position, observer.position) < 1.7f) shadow.gameObject.SetActive(false);
            }
            else shadow.transform.position = shadowOrigin + new Vector3(Mathf.Sin(age * 1.3f), Mathf.Sin(age * 2) * 0.12f);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) TryResearch();
            Tick(Time.deltaTime);
        }

        public void Tick(float seconds)
        {
            if (!hallucinationsEnabled || Time.timeScale == 0 || seconds <= 0) return;
            if (inventory.Mode == PlayerInputMode.Sleep || sanity.Stage == SanityStage.Calm)
            {
                if (active != DemonEvent.None) EndEvent();
                return;
            }
            if (active != DemonEvent.None)
            {
                remaining -= seconds;
                if (inspectedLamp && (dimmed == null || !dimmed.Powered ||
                    Vector2.Distance(observer.position, dimmed.transform.position) > 5)) inspectedLamp = false;
                if (remaining <= 0 && inspectedLamp)
                    memory.Record(Discovery.EndureDarkness, world.CellAt(observer.position));
                age += seconds;
                if (remaining <= 0 || active == DemonEvent.Intruder && (!sanity.CanManifest || intruder == null) ||
                    active == DemonEvent.Glimpse && !clock.IsNight && sanity.Stage == SanityStage.Unease) EndEvent();
                else RefreshEffects(seconds);
                return;
            }
            if (inventory.Mode != PlayerInputMode.Gameplay || !scheduler.Due(seconds)) return;
            var lamp = NearbyLamp();
            bool cave = world.DepthAt(world.CellAt(observer.position)) >= 4;
            var effect = scheduler.Choose(sanity.Stage, clock.IsNight, cave, lamp != null,
                sanity.CanManifest && !enemies.Peaceful, inventory.Progress.Level, ResearchOpportunity(),
                network.WithinPoweredWard(observer.position));
            StartEvent(effect, lamp);
        }
    }
}
