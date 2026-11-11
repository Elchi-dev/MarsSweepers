using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// What the game's belts do, measured before Mars items ride on them (docs/BELTS.md has the plan).
    /// Two passive hooks log the first calls of MoonWorld.EnterMachine (a canister reaches a machine input) and
    /// MoonWorld.EmitCargo (a machine puts cargo on a belt); `belts` in the terminal lists the belts near the
    /// player and what the game answers for the ports of the nearest Mars building. Nothing is changed.
    /// </summary>
    internal static class BeltResearch
    {
        const int MaxLog = 40;
        static int entered, emitted;

        public static void EnterMachine(MoonWorld w, MoonDepotSnapshot machine, int input, int id, bool result)
        {
            if (entered >= MaxLog) return;
            entered++;
            try
            {
                string cargo = "?";
                if (w.TryGetCanister(id, out MoonDroppedCanister c)) cargo = $"{c.cargo} {c.kilograms:0.###} kg, owner {c.owner}";
                Plugin.Log.LogMessage($"[belts] enter machine kind {machine?.kind} id {machine?.id} input {input} canister {id} ({cargo}) -> {result}" +
                                      (VanillaBuild.ByKind(machine?.kind ?? 0) != null ? "  (a MARS kind)" : ""));
            }
            catch (Exception e) { Log.Once("belt-enter", "[belts] enter log failed: " + e.Message); }
        }

        public static void EmitCargo(MoonWorld w, MoonConveyorSnapshot belt, string owner, MoonCargo cargo, double kg, bool result, string reason)
        {
            if (emitted >= MaxLog) return;
            emitted++;
            try { Plugin.Log.LogMessage($"[belts] emit {cargo} {kg:0.###} kg by {owner} onto belt {belt?.id} -> {result} {reason}  (next canister id {w.nextCanisterId})"); }
            catch (Exception e) { Log.Once("belt-emit", "[belts] emit log failed: " + e.Message); }
        }

        public static IEnumerable<string> Report(Vector3 me)
        {
            var w = Commands.FindWorld();
            if (w == null) { yield return "No world."; yield break; }
            var lines = new List<string>();
            try
            {
                foreach (var kv in w.conveyors)
                {
                    var b = kv.Value;
                    float d = Mathf.Min(Vector3.Distance(b.start, me), Vector3.Distance(b.end, me));
                    if (d < 40f) lines.Add($"  belt {b.id}: start {Vector3.Distance(b.start, me):0.0} m, end {Vector3.Distance(b.end, me):0.0} m away");
                }
                lines.Add($"{w.conveyors.Count} belts in the world, {w.canisters.Count} canisters, next canister id {w.nextCanisterId}");
            }
            catch (Exception e) { lines.Add("belt list failed: " + e.Message); }
            try
            {
                var rec = Buildings.Data.List.Where(b => !string.IsNullOrEmpty(b.Type)).OrderBy(b => Vector3.Distance(b.Pos, me)).FirstOrDefault();
                var def = rec != null ? MarsCatalog.Get(rec.Type) : null;
                if (rec != null && def != null && Vector3.Distance(rec.Pos, me) < 30f)
                {
                    lines.Add($"nearest Mars building: {def.Name} #{rec.Id}");
                    var t = Models.Get(def.Model);
                    foreach (var m in t.Markers.Where(m => m.Key.StartsWith("port_")))
                    {
                        var p = rec.Pos + rec.Rot * m.Value.localPosition;
                        bool isIn = w.MachineInputAt(p, out MoonDepotSnapshot mach, out int input);
                        var start = w.BeltStartingAt(p); var end = w.BeltEndingAt(p);
                        lines.Add($"  {m.Key}: input {(isIn ? $"yes (kind {mach?.kind}, input {input})" : "no")}, belt starting here {start?.id.ToString() ?? "none"}, ending here {end?.id.ToString() ?? "none"}");
                    }
                }
            }
            catch (Exception e) { lines.Add("port check failed: " + e.Message); }
            lines.Add($"logged so far: {entered} machine entries, {emitted} emits (first {MaxLog} of each go to the BepInEx log)");
            foreach (var l in lines) yield return l;
        }
    }
}
