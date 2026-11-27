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
        private const double StepSeconds = 0.1;
        private const double StepTolerance = 0.000001;

        [SerializeField] private BlockWorld world;
        [SerializeField] private Material material;
        [SerializeField] private Material wireMaterial;
        [SerializeField] private ItemData starterGenerator, starterLamp;
        private readonly List<EnergyDevice> devices = new();
        private readonly List<LineRenderer> wires = new();
        private readonly List<EnergyDevice> componentDevices = new();
        private readonly List<int> componentEnds = new();
        private readonly HashSet<EnergyDevice> unvisited = new();
        private bool topologyDirty;
        private double accumulatedTime;
        private float supportTimer;
        public IReadOnlyList<EnergyDevice> Devices => devices;

        private void Start()
        {
            if (starterGenerator != null) Spawn(starterGenerator, new Vector3(-6.5f, 1.5f, 0));
            if (starterLamp != null) Spawn(starterLamp, new Vector3(-10.5f, 1.5f, 0));
        }

        public EnergyDevice At(Vector2Int cell)
        {
            foreach (var device in devices)
                if (world.CellAt(device.transform.position) == cell) return device;
            return null;
        }

        public bool WithinPoweredWard(Vector2 position)
        {
            foreach (var device in devices)
            {
                float radius = device.Data.wardRadius;
                if (radius > 0 && device.Powered && ((Vector2)device.transform.position - position).sqrMagnitude <= radius * radius)
                    return true;
            }
            return false;
        }

        public bool CanPlace(Vector2Int cell) =>
            devices.Count < 64 && At(cell) == null && world.Peek(cell) == null &&
            world.IsSolidAt(cell + Vector2Int.down);

        public EnergyDevice Spawn(ItemData item, Vector3 position, float stored = 0)
        {
            var go = new GameObject(item.DisplayName, typeof(SpriteRenderer), typeof(BoxCollider2D));
            go.transform.position = position;
            var visual = go.GetComponent<SpriteRenderer>();
            visual.sprite = item.Icon;
            visual.sharedMaterial = material;
            visual.sortingOrder = 5;
            go.GetComponent<BoxCollider2D>().size = Vector2.one * 0.9f;
            var device = go.AddComponent<EnergyDevice>();
            device.Initialize(item);
            device.Stored = Mathf.Clamp(stored, 0, item.Device.capacity);
            devices.Add(device);
            topologyDirty = true;
            return device;
        }

        public void Remove(EnergyDevice device, bool dropItem)
        {
            if (!devices.Remove(device)) return;
            topologyDirty = true;
            device.gameObject.SetActive(false);
            Destroy(device.gameObject);
            if (dropItem) world.GetComponent<DropSpawner>().Spawn(device.Item, 1, device.transform.position);
        }

        public void Clear()
        {
            for (int index = devices.Count - 1; index >= 0; index--) Remove(devices[index], false);
            accumulatedTime = 0;
            supportTimer = 0;
        }

        private static bool Linked(EnergyDevice first, EnergyDevice second) =>
            Vector2.Distance(first.transform.position, second.transform.position) <=
            Mathf.Min(first.Data.connectionRange, second.Data.connectionRange);

        private void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0) return;
            Tick(delta);
            supportTimer += delta;
            if (supportTimer < 1) return;
            supportTimer -= 1;
            for (int index = 0; index < devices.Count;)
            {
                var device = devices[index];
                var supportCell = world.CellAt(device.transform.position) + Vector2Int.down;
                if (world.IsSolidAt(supportCell)) index++;
                else Remove(device, true);
            }
        }

        private void LateUpdate() => RefreshTopology();

        public void Tick(float delta)
        {
            if (!float.IsFinite(delta) || delta <= 0) return;
            accumulatedTime += delta;
            if (accumulatedTime + StepTolerance < StepSeconds) return;

            RefreshTopology();
            // Use the same integration step at every frame rate and keep leftover time for the next frame.
            int steps = (int)Math.Floor((accumulatedTime + StepTolerance) / StepSeconds);
            accumulatedTime -= steps * StepSeconds;
            for (int step = 0; step < steps; step++) SolveStep((float)StepSeconds);
        }

        private void RefreshTopology()
        {
            if (!topologyDirty) return;
            componentDevices.Clear();
            componentEnds.Clear();
            unvisited.Clear();
            foreach (var device in devices) unvisited.Add(device);

            // Keep breadth-first discovery in placement order so consumers retain their priority.
            foreach (var root in devices)
            {
                if (!unvisited.Remove(root)) continue;
                int start = componentDevices.Count;
                componentDevices.Add(root);
                for (int index = start; index < componentDevices.Count; index++)
                {
                    var current = componentDevices[index];
                    foreach (var candidate in devices)
                    {
                        if (unvisited.Contains(candidate) && Linked(current, candidate))
                        {
                            unvisited.Remove(candidate);
                            componentDevices.Add(candidate);
                        }
                    }
                }
                componentEnds.Add(componentDevices.Count);
            }
            RebuildWires();
            topologyDirty = false;
        }

        private void SolveStep(float delta)
        {
            int first = 0;
            foreach (int end in componentEnds)
            {
                float total = 0, capacity = 0;
                for (int index = first; index < end; index++)
                {
                    var device = componentDevices[index];
                    total += device.Stored + device.Data.generationPerSecond * delta;
                    capacity += device.Data.capacity;
                    device.Powered = false;
                }
                total = Mathf.Min(total, capacity);
                for (int index = first; index < end; index++)
                {
                    var device = componentDevices[index];
                    float cost = device.Data.consumptionPerSecond * delta;
                    if (cost > 0 && total >= cost)
                    {
                        total -= cost;
                        device.Powered = true;
                    }
                }
                for (int index = first; index < end; index++)
                {
                    var device = componentDevices[index];
                    device.Stored = capacity > 0 ? total * device.Data.capacity / capacity : 0;
                }
                first = end;
            }
        }

        private void RebuildWires()
        {
            int count = 0;
            for (int first = 0; first < devices.Count; first++)
                for (int second = first + 1; second < devices.Count; second++)
                {
                    var a = devices[first];
                    var b = devices[second];
                    if (!Linked(a, b)) continue;

                    if (count == wires.Count)
                    {
                        var line = new GameObject("Arcane link", typeof(LineRenderer)).GetComponent<LineRenderer>();
                        line.transform.SetParent(transform, false);
                        line.sharedMaterial = wireMaterial;
                        line.positionCount = 2;
                        line.widthMultiplier = 0.035f;
                        line.startColor = line.endColor = new Color(0.4f, 0.85f, 0.95f, 0.55f);
                        line.sortingOrder = 8;
                        wires.Add(line);
                    }
                    var wire = wires[count++];
                    wire.SetPosition(0, a.transform.position);
                    wire.SetPosition(1, b.transform.position);
                }

            for (int index = wires.Count - 1; index >= count; index--)
            {
                var wire = wires[index];
                wire.gameObject.SetActive(false);
                Destroy(wire.gameObject);
                wires.RemoveAt(index);
            }
        }
    }
}
