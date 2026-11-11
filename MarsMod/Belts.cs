using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// EXPERIMENTAL, off by default (`beltmode on`, host): Mars items on the game's belts, as docs/BELTS.md plans it.
    /// Out: a Mars machine whose port has a belt starting there puts its products on it, one at a time, as tiny
    /// canisters (0.01 kg of dust, so a vanilla machine pays nothing for them); the host remembers which canister
    /// carries which Mars item. In: when one of those canisters reaches a Mars machine (the game asks
    /// EnterMachine), the machine takes it if it needs that item and has room; the container port takes every
    /// Mars item into the crew storage. Built before the belt research came back, so it may not work yet: the
    /// game has to know the ports of a Mars building for belts to reach it at all.
    /// </summary>
    internal static class Belts
    {
        public sealed class State
        {
            public bool On;
            public Dictionary<int, string> Carried = new Dictionary<int, string>();   // canister id → Mars item
        }

        const string Key = "belts";
        public static State Data = new State();
        static bool dirty;
        static float nextOut;
        const float CanisterKg = 0.01f;

        static Belts()
        {
            MarsData.Changed += key =>
            {
                if (key != "" && key != Key) return;
                bool was = Data.On;
                Data = MarsData.Get<State>(Key);
                if (Data.On && !was) PortsNow();
            };
        }

        public static void Init() { }

        public static string Toggle(bool on)
        {
            bool was = Data.On;
            Data.On = on; dirty = true; Save();
            if (on && !was) PortsNow();
            return on ? "Mars belts ON (experimental): Mars buildings now have belt ports. If belts do not snap to them, save and load the world once." : "Mars belts off (load the world again to drop the ports).";
        }

        static void Save() { if (dirty && MarsNet.IsHost) { MarsData.Set(Key, Data); dirty = false; } }

        /// <summary>Where a Mars building's ports are in the world (from its model's port_N markers).</summary>
        public static IEnumerable<Vector3> Ports(Buildings.Rec b, MarsCatalog.Def def)
        {
            foreach (var (pos, _) in LocalPorts(def)) yield return b.Pos + b.Rot * pos;
        }

        /// <summary>The ports in the building's own space: position and outward direction (from the model's markers).</summary>
        public static List<(Vector3 pos, Vector3 dir)> LocalPorts(MarsCatalog.Def def)
        {
            var list = new List<(Vector3, Vector3)>();
            try
            {
                var t = Models.Get(def.Model);
                foreach (var m in t.Markers.Where(m => m.Key.StartsWith("port_")).OrderBy(m => m.Key))
                {
                    var p = t.Root.transform.InverseTransformPoint(m.Value.position);
                    var flat = new Vector3(p.x, 0f, p.z);
                    var dir = Mathf.Abs(flat.x) > Mathf.Abs(flat.z) ? new Vector3(Mathf.Sign(flat.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(flat.z));
                    list.Add((p, dir));
                }
            }
            catch (Exception e) { Log.Once("belt-ports-" + def.Id, "[belts] ports of " + def.Id + ": " + e.Message); }
            return list;
        }

        /// <summary>Give a Mars building definition its own conveyor ports, so the game's belts snap to them.
        /// Port 0 is an output, the rest are inputs (extractors: all outputs; the container port: all inputs).</summary>
        public static void SetPorts(MoonDepotBuildDefinition d, MarsCatalog.Def def)
        {
            var ports = LocalPorts(def);
            if (ports.Count == 0) return;
            try
            {
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<MoonDepotBuildDefinition.ConveyorPort>(ports.Count);
                for (int i = 0; i < ports.Count; i++)
                {
                    var cp = new MoonDepotBuildDefinition.ConveyorPort();
                    cp.name = "mars_port_" + i;
                    cp.position = ports[i].pos;
                    cp.direction = ports[i].dir;
                    cp.output = def.Role == MarsCatalog.Role.Extractor || def.Role != MarsCatalog.Role.Storage && i == 0;
                    arr[i] = cp;
                }
                d.conveyorPorts = arr;
                Plugin.Log.LogMessage($"[belts] {def.Id}: {ports.Count} conveyor ports set");
            }
            catch (Exception e) { Log.Once("belt-setports-" + def.Id, "[belts] could not set ports of " + def.Id + ": " + e.Message); }
        }

        // ------------------------------------------------------------------ out (host, every 2 s)

        public static void Tick(float now)
        {
            if (!Data.On || !MarsNet.IsHost || !MarsData.Loaded || now < nextOut) return;
            nextOut = now + 2f;
            var w = Commands.FindWorld();
            if (w == null) return;
            try
            {
                foreach (var b in Buildings.Data.List.ToList())
                {
                    var def = MarsCatalog.Get(b.Type);
                    if (def == null || Buildings.Jammed(b)) continue;
                    string item = null;
                    if (def.Role == MarsCatalog.Role.Extractor) item = b.Items.Keys.FirstOrDefault(k => b.Items[k] > 0);
                    else if (def.Role == MarsCatalog.Role.Recipe) item = def.Out.Keys.FirstOrDefault(k => Buildings.Count(b.Items, k) > 0);
                    if (item == null) continue;
                    foreach (var port in Ports(b, def))
                    {
                        var belt = w.BeltStartingAt(port);
                        if (belt == null) continue;
                        int before = w.nextCanisterId;
                        if (!w.EmitCargo(belt, "mars", MoonCargo.Dust, CanisterKg, out string reason)) { Log.Once("belt-emit-" + b.Id, $"[belts] {def.Name} #{b.Id} could not emit: {reason}"); continue; }
                        int id = w.nextCanisterId != before ? w.nextCanisterId - 1 : before;
                        Data.Carried[id] = item;
                        Buildings.AddTo(b.Items, item, -1);
                        Buildings.Changed();
                        dirty = true;
                        break;
                    }
                }
                // Forget canisters that no longer exist (sold, vacuumed, picked up).
                if (Data.Carried.Count > 0 && (int)now % 20 == 0)
                    foreach (var id in Data.Carried.Keys.ToList())
                        if (!w.TryGetCanister(id, out MoonDroppedCanister _)) { Data.Carried.Remove(id); dirty = true; }
            }
            catch (Exception e) { Log.Once("belt-tick", "[belts] " + e.Message); }
            Save();
        }

        // ------------------------------------------------------------------ in (prefix on EnterMachine)

        /// <summary>True when this call was ours (result set, the game's own method skipped).</summary>
        public static bool Enter(MoonWorld w, MoonDepotSnapshot machine, int canister, ref bool result)
        {
            if (!Data.On || machine == null) return false;
            var entry = VanillaBuild.ByKind(machine.kind);
            if (entry == null) return false;
            try
            {
                var rec = Buildings.Data.List.Find(b => b.Id == Buildings.VanillaIds + machine.id);
                if (rec == null || !Data.Carried.TryGetValue(canister, out var item)) { result = false; return true; }   // not a Mars item: refuse
                var def = entry.Def;
                bool take = def.Role == MarsCatalog.Role.Storage ||
                            def.In.TryGetValue(item, out _) && Buildings.Count(rec.Items, item) < def.Cap;
                if (!take) { result = false; return true; }
                if (def.Role == MarsCatalog.Role.Storage) Buildings.AddTo(Buildings.Items.Storage, item, 1);
                else Buildings.AddTo(rec.Items, item, 1);
                Data.Carried.Remove(canister);
                w.RemoveCanister(canister);
                Buildings.Changed();
                dirty = true;
                result = true;
                return true;
            }
            catch (Exception e) { Log.Once("belt-enter", "[belts] enter failed: " + e.Message); return false; }
        }

        /// <summary>Ports for the definitions that already exist (the Mars data may arrive after the build menu was set up).</summary>
        static void PortsNow()
        {
            foreach (var e in VanillaBuild.Entries) if (e.Definition != null) SetPorts(e.Definition, e.Def);
        }

        public static void Reset() { Data = new State(); dirty = false; }
    }
}
