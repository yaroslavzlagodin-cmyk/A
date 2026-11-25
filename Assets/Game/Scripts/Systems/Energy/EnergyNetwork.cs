using System;
using System.Collections.Generic;
using BrokenHeart.Inventory;
using BrokenHeart.World;
using UnityEngine;
namespace BrokenHeart.Systems.Energy
{
    [Serializable] public struct SavedDevice { public string itemId; public Vector3 position; public float stored; }
    public sealed class EnergyNetwork : MonoBehaviour
    {
        [SerializeField] private BlockWorld world;
        [SerializeField] private Material material;
        [SerializeField] private Material wireMaterial;
        [SerializeField] private ItemData starterGenerator, starterLamp;
        private readonly List<EnergyDevice> devices = new();
        private readonly List<LineRenderer> wires = new();
        public IReadOnlyList<EnergyDevice> Devices => devices;
        private float supportTimer;
        private void Start()
        {
            if (starterGenerator != null) Spawn(starterGenerator, new Vector3(-6.5f,1.5f,0));
            if (starterLamp != null) Spawn(starterLamp, new Vector3(-10.5f,1.5f,0));
        }
        public EnergyDevice At(Vector2Int cell) => devices.Find(d => world.CellAt(d.transform.position) == cell);
        public bool CanPlace(Vector2Int cell) => devices.Count < 64 && At(cell) == null && world.Get(cell) == null &&
            world.Get(cell + Vector2Int.down) is BlockData support && support.Solid;
        public EnergyDevice Spawn(ItemData item, Vector3 position, float stored = 0)
        {
            var go = new GameObject(item.DisplayName, typeof(SpriteRenderer), typeof(BoxCollider2D));
            go.transform.position = position;
            var visual = go.GetComponent<SpriteRenderer>(); visual.sprite = item.Icon; visual.sharedMaterial = material; visual.sortingOrder = 5;
            go.GetComponent<BoxCollider2D>().size = Vector2.one * 0.9f;
            var device = go.AddComponent<EnergyDevice>(); device.Initialize(item); device.Stored = Mathf.Clamp(stored, 0, item.Device.capacity);
            devices.Add(device); RebuildWires(); return device;
        }
        public void Remove(EnergyDevice device, bool dropItem)
        {
            if (!devices.Remove(device)) return;
            if (dropItem) world.GetComponent<DropSpawner>().Spawn(device.Item, 1, device.transform.position);
            device.gameObject.SetActive(false); Destroy(device.gameObject); RebuildWires();
        }
        public void Clear() { foreach (var device in devices.ToArray()) Remove(device, false); }
        private bool Linked(EnergyDevice a, EnergyDevice b) => Vector2.Distance(a.transform.position,b.transform.position) <= Mathf.Min(a.Data.connectionRange,b.Data.connectionRange);
        private void Update()
        {
            Tick(Time.deltaTime);
            supportTimer += Time.deltaTime;
            if (supportTimer < 1) return;
            supportTimer = 0;
            foreach (var device in devices.ToArray())
            {
                var support = world.Get(world.CellAt(device.transform.position) + Vector2Int.down);
                if (support == null || !support.Solid) Remove(device, true);
            }
        }
        public void Tick(float delta)
        {
            if (delta <= 0) return;
            var remaining = new HashSet<EnergyDevice>(devices);
            foreach (var root in devices)
            {
                if (!remaining.Remove(root)) continue;
                var component = new List<EnergyDevice> { root };
                for (int i = 0; i < component.Count; i++)
                    foreach (var other in devices)
                        if (remaining.Contains(other) && Linked(component[i],other)) { remaining.Remove(other); component.Add(other); }
                float total = 0, capacity = 0;
                foreach (var device in component)
                { total += device.Stored + device.Data.generationPerSecond * delta; capacity += device.Data.capacity; device.Powered = false; }
                total = Mathf.Min(total, capacity);
                foreach (var device in component)
                {
                    float cost = device.Data.consumptionPerSecond * delta;
                    if (cost > 0 && total >= cost) { total -= cost; device.Powered = true; }
                }
                foreach (var device in component) device.Stored = total * device.Data.capacity / capacity;
            }
        }
        private void RebuildWires()
        {
            foreach (var wire in wires) if (wire != null) { wire.gameObject.SetActive(false); Destroy(wire.gameObject); }
            wires.Clear();
            for (int i = 0; i < devices.Count; i++) for (int j = i + 1; j < devices.Count; j++)
            {
                if (!Linked(devices[i], devices[j])) continue;
                var line = new GameObject("Arcane link", typeof(LineRenderer)).GetComponent<LineRenderer>();
                line.transform.SetParent(transform); line.sharedMaterial = wireMaterial; line.positionCount = 2;
                line.SetPosition(0,devices[i].transform.position); line.SetPosition(1,devices[j].transform.position);
                line.widthMultiplier = 0.035f; line.startColor = line.endColor = new Color(0.4f,0.85f,0.95f,0.55f); line.sortingOrder = 8;
                wires.Add(line);
            }
        }
    }
}
