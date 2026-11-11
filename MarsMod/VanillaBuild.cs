using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Mars machines as real game buildings: they get their own building kinds right after the game's 192, a card in the
    /// normal build menu (hammer → Production / Logistics) and our Blender model as the prefab, so the game's own tool
    /// does the ghost, footprint checks, payment, removal, refunds and co-op sync.
    ///
    /// The game's save file never sees these kinds: when the game writes a Mars save, our buildings are taken out of the
    /// structure list and written next to it (mars-structures.json), and put back in when the world loads. A Mars world
    /// therefore still opens without the mod (just without our machines), and kind numbers are only a runtime detail:
    /// the side file stores string ids ("marsmod.building.drill").
    /// </summary>
    internal static class VanillaBuild
    {
        public const int FirstKind = 193;
        const int DonorKind = 9;            // safety barrier: plain prefab, no scripts, no ports
        const string SideFile = "mars-structures.json";

        public sealed class Entry
        {
            public string Key;
            public MarsCatalog.Def Def;
            public int Kind;
            public MoonDepotBuildDefinition Definition;
            public GameObject Prefab;
            public Sprite Card;
        }

        public static readonly List<Entry> Entries = new List<Entry>();
        public static bool Failed { get; private set; }
        public static string FailReason;
        public static bool Ready => installedWorld != IntPtr.Zero && !Failed;

        static IntPtr installedWorld;
        static MoonDepotBuildDefinition originalDepotDefinition;
        static GameObject prefabHolder;
        static Il2CppReferenceArray<MoonDepotBuilder.CatalogEntry> originalCatalog;
        static bool catalogPublished;

        static VanillaBuild()
        {
            int k = FirstKind;
            foreach (var d in MarsCatalog.All) Entries.Add(new Entry { Key = "marsmod.building." + d.Id, Def = d, Kind = k++ });
        }

        public static int LastKind => FirstKind + Entries.Count - 1;
        public static bool IsOurs(int kind) => kind >= FirstKind && kind <= LastKind;
        public static Entry ByKind(int kind) => IsOurs(kind) ? Entries[kind - FirstKind] : null;
        public static Entry ByKey(string key) => Entries.Find(e => e.Key == key);
        public static Entry ByType(string id) => Entries.Find(e => e.Def.Id == id);

        // ------------------------------------------------------------------ world install

        /// <summary>Make our kinds known to this world (idempotent; safe to call from hooks).</summary>
        public static bool Install(MoonWorld world)
        {
            if (Failed || world == null) return false;
            if (installedWorld == world.Pointer) return true;
            if (!MarsState.CurrentIsMars) return false;
            try
            {
                var baseDef = world.depotDefinition;
                if (baseDef == null) return false;
                var old = baseDef.additionalStructures;
                if (old == null || old.Length < DonorKind) throw new Exception("no structure list");
                if (old.Length != FirstKind - 1)
                {
                    // Another mod already extended the list: only go on if our slots are free.
                    for (int k = FirstKind; k <= Math.Min(LastKind, old.Length); k++)
                        if (old[k - 1] != null) throw new Exception($"building kind {k} is already used by another mod");
                }
                var donor = old[DonorKind - 1];
                if (donor == null) throw new Exception("donor definition missing");

                if (prefabHolder == null)
                {
                    prefabHolder = new GameObject(); prefabHolder.name = "Mars building prefabs";
                    prefabHolder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(prefabHolder);
                }
                foreach (var e in Entries) Build(e, donor);

                int length = Math.Max(old.Length, LastKind);
                var arr = new Il2CppReferenceArray<MoonDepotBuildDefinition>(length);
                for (int i = 0; i < old.Length; i++) arr[i] = old[i];
                foreach (var e in Entries) arr[e.Kind - 1] = e.Definition;

                var clone = UnityEngine.Object.Instantiate(baseDef).Cast<MoonDepotBuildDefinition>();
                clone.name = baseDef.name;
                clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
                clone.additionalStructures = arr;
                originalDepotDefinition = baseDef;
                world.depotDefinition = clone;
                GrowKindArrays(world, length);

                installedWorld = world.Pointer;
                Plugin.Log.LogMessage($"[vbuild] {Entries.Count} Mars buildings registered as kinds {FirstKind}-{LastKind} ({string.Join(", ", Entries.Select(x => x.Key))})");
                return true;
            }
            catch (Exception e)
            {
                Failed = true; FailReason = e.Message;
                Plugin.Log.LogError("[vbuild] could not add Mars buildings to the build menu, falling back to the N menu: " + e);
                return false;
            }
        }

        static void Build(Entry e, MoonDepotBuildDefinition donor)
        {
            if (e.Prefab == null)
            {
                var t = Models.Get(e.Def.Model);
                var root = new GameObject(); root.name = e.Key;
                root.transform.SetParent(prefabHolder.transform, false);
                var model = UnityEngine.Object.Instantiate(t.Root).Cast<GameObject>();
                model.name = "MarsModel:" + e.Def.Id;
                model.transform.SetParent(root.transform, false);
                model.SetActive(true);
                int layer = donor.prefab != null ? donor.prefab.layer : 0;
                SetLayer(root.transform, layer);
                e.Prefab = root;
            }
            if (e.Definition == null)
            {
                var t = Models.Get(e.Def.Model);
                var def = UnityEngine.Object.Instantiate(donor).Cast<MoonDepotBuildDefinition>();
                def.name = e.Key;
                def.hideFlags = HideFlags.DontUnloadUnusedAsset;
                def.prefab = e.Prefab;
                def.displayName = e.Def.Name;
                def.priceDollars = Buildings.FreeBuild ? 0 : e.Def.Price;
                var half = default(Vector2);
                half.x = Mathf.Max(0.6f, t.Bounds.extents.x * 0.92f);
                half.y = Mathf.Max(0.6f, t.Bounds.extents.z * 0.92f);
                def.halfFootprint = half;
                def.height = Mathf.Max(0.5f, t.Bounds.max.y);
                def.scale = 1f;
                def.floorOffset = 0f;
                e.Definition = def;
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>Per-kind counters in the world are sized for 192 kinds; give them room for ours.</summary>
        static void GrowKindArrays(MoonWorld world, int maxKind)
        {
            try
            {
                var a = world.landmarkKindCount;
                if (a != null && a.Length <= maxKind) { var b = new Il2CppStructArray<int>(maxKind + 1); for (int i = 0; i < a.Length; i++) b[i] = a[i]; world.landmarkKindCount = b; }
                var c = world.raisedKindCount;
                if (c != null && c.Length <= maxKind) { var d = new Il2CppStructArray<int>(maxKind + 1); for (int i = 0; i < c.Length; i++) d[i] = c[i]; world.raisedKindCount = d; }
                var p = world.landmarkPlaced;
                if (p != null && p.Length <= maxKind) { var q = new Il2CppStructArray<bool>(maxKind + 1); for (int i = 0; i < p.Length; i++) q[i] = p[i]; world.landmarkPlaced = q; }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[vbuild] kind counters: " + e.Message); }
        }

        public static void SetPrices()
        {
            foreach (var e in Entries) if (e.Definition != null) e.Definition.priceDollars = Buildings.FreeBuild ? 0 : e.Def.Price;
        }

        /// <summary>Leaving the world: everything here belonged to it.</summary>
        public static void Reset()
        {
            Unpublish();
            foreach (var e in Entries)
            {
                if (e.Prefab != null) UnityEngine.Object.Destroy(e.Prefab);
                if (e.Definition != null) UnityEngine.Object.Destroy(e.Definition);
                e.Prefab = null; e.Definition = null;
            }
            installedWorld = IntPtr.Zero;
            originalDepotDefinition = null;
        }

        // ------------------------------------------------------------------ build menu cards

        public static void Frame()
        {
            bool want = Ready && MarsWorld.InMarsWorld;
            if (want && !catalogPublished) Publish();
            else if (!want && catalogPublished) Unpublish();
            if (!MarsWorld.InMarsWorld) PurgeRecent();
        }

        static void Publish()
        {
            try
            {
                var old = MoonDepotBuilder.baseStructures;
                if (old == null) return;
                originalCatalog = old;
                var arr = new Il2CppReferenceArray<MoonDepotBuilder.CatalogEntry>(old.Length + Entries.Count);
                for (int i = 0; i < old.Length; i++) arr[i] = old[i];
                int at = old.Length;
                foreach (var e in Entries) arr[at++] = new MoonDepotBuilder.CatalogEntry(e.Kind, e.Def.Name, e.Def.Category, e.Key);
                MoonDepotBuilder.baseStructures = arr;
                catalogPublished = true;
                RefreshBuilders();
                Plugin.Log.LogMessage($"[vbuild] build menu: {Entries.Count} Mars cards added");
            }
            catch (Exception e) { Failed = true; FailReason = e.Message; Plugin.Log.LogError("[vbuild] build menu cards failed: " + e); }
        }

        static void Unpublish()
        {
            if (!catalogPublished) return;
            catalogPublished = false;
            try { if (originalCatalog != null) MoonDepotBuilder.baseStructures = originalCatalog; } catch { }
            originalCatalog = null;
            PurgeRecent();
            RefreshBuilders();
        }

        static void RefreshBuilders()
        {
            foreach (var b in Look.All<MoonDepotBuilder>())
            {
                if (!Look.InScene(b.gameObject)) continue;
                try { b.structureEntries = null; } catch { }
                try { b.RefreshCatalog(); } catch { }
            }
        }

        static float nextPurge;
        /// <summary>The game remembers recently built kinds; ours must not show up in Moon worlds.</summary>
        static void PurgeRecent()
        {
            if (Time.realtimeSinceStartup < nextPurge) return;
            nextPurge = Time.realtimeSinceStartup + 3f;
            try
            {
                foreach (var b in Look.All<MoonDepotBuilder>())
                {
                    var r = b.recent;
                    if (r == null) continue;
                    bool changed = false;
                    for (int i = r.Count - 1; i >= 0; i--) if (IsOurs(r[i])) { r.RemoveAt(i); changed = true; }
                    if (changed) b.recentChanged = true;
                }
            }
            catch { }
        }

        public static Sprite CardArt(string image)
        {
            var e = ByKey(image);
            if (e == null) return null;
            if (e.Card != null) return e.Card;
            try
            {
                using (var s = typeof(VanillaBuild).Assembly.GetManifestResourceStream("card_" + e.Def.Id + ".png"))
                {
                    if (s == null) return null;
                    var ms = new MemoryStream(); s.CopyTo(ms);
                    var tex = Png.Load(ms.ToArray(), e.Key);
                    var rect = default(Rect); rect.width = tex.width; rect.height = tex.height;
                    var pivot = default(Vector2); pivot.x = 0.5f; pivot.y = 0.5f;
                    e.Card = Sprite.Create(tex, rect, pivot, 100f);
                    e.Card.name = e.Key;
                    UnityEngine.Object.DontDestroyOnLoad(e.Card);
                    e.Card.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
            }
            catch (Exception ex) { Log.Once("card-" + e.Key, "[vbuild] card art failed: " + ex.Message); }
            return e.Card;
        }

        // ------------------------------------------------------------------ placement rules on top of the game's

        /// <summary>Reason the current ghost may not be built (null = fine). Updated every frame from the local builder.</summary>
        public static string Block;
        public static int GhostKind;
        public static Vector3 GhostPos;

        public static void PlacementFrame()
        {
            Block = null; GhostKind = 0;
            if (!MarsWorld.InMarsWorld) return;
            try
            {
                foreach (var b in Look.All<MoonDepotBuilder>())
                {
                    if (!Look.InScene(b.gameObject) || b.ghostKind == 0) continue;
                    if (!b.PlacementPose(out int kind, out Vector3 pos, out Quaternion rot)) continue;
                    GhostKind = kind; GhostPos = pos;
                    Block = Rule(kind, pos);
                    break;
                }
            }
            catch { }
        }

        static string Rule(int kind, Vector3 pos)
        {
            var e = ByKind(kind);
            if (Buildings.Data.List.Any(o => o.Owner == Aliens.Owner && (o.Pos - pos).sqrMagnitude < 8f * 8f)) return "Too close to the alien outpost";
            string missing = e != null ? Buildings.MissingItems(e.Def) : null;
            if (missing != null) return missing;
            if (e != null && e.Def.NeedsMine)
            {
                var m = Mines.Nearest(pos, 6f);
                if (m == null)
                {
                    var near = Mines.Nearest(pos, 100000f);
                    return near != null ? $"Build the drill on a mine (nearest: {Vector3.Distance(near.Position, pos):0} m)" : "Build the drill on a mine";
                }
                if (Buildings.Data.List.Any(b => b.Mine == m.Id)) return "This mine already has a drill";
                return null;
            }
            // Everything else keeps mines free for drills.
            if (Mines.Nearest(pos, 7f) != null) return "Keep mines free for drills";
            return null;
        }

        // ------------------------------------------------------------------ save side file

        public sealed class Saved
        {
            public string Key, Owner;
            public int Id, Support, Vehicles, Style;
            public float X, Y, Z, Yaw, Qx, Qy, Qz, Qw;
            public bool PlacedPose;
            public double Stored;
        }

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

        static string SidePath()
        {
            int slot = MarsState.ActiveSlot;
            return slot < 0 ? null : Path.Combine(MarsState.SlotDir(slot), SideFile);
        }

        public sealed class Taken { public List<MoonDepotSnapshot> Depots = new List<MoonDepotSnapshot>(), Anchors = new List<MoonDepotSnapshot>(); }

        /// <summary>Before the game writes a save: take our buildings out and write them to the side file.</summary>
        public static Taken BeforeWrite(MoonLooseDustSnapshot state)
        {
            if (state == null) return null;
            var taken = new Taken();
            Pull(state.depots, taken.Depots);
            Pull(state.anchors, taken.Anchors);
            if (taken.Depots.Count == 0 && taken.Anchors.Count == 0 && !MarsState.CurrentIsMars) return null;
            if (MarsState.CurrentIsMars)
            {
                try
                {
                    string path = SidePath();
                    if (path != null)
                    {
                        var list = taken.Depots.Select(ToSaved).Where(s => s != null).ToList();
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        string tmp = path + ".tmp";
                        File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
                        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                        else File.Move(tmp, path);
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("[vbuild] could not save Mars buildings: " + e.Message); }
            }
            return taken;
        }

        public static void AfterWrite(MoonLooseDustSnapshot state, Taken taken)
        {
            if (state == null || taken == null) return;
            foreach (var d in taken.Depots) state.depots.Add(d);
            foreach (var d in taken.Anchors) state.anchors.Add(d);
        }

        static void Pull(Il2CppSystem.Collections.Generic.List<MoonDepotSnapshot> list, List<MoonDepotSnapshot> into)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var d = list[i];
                if (d != null && IsOurs(d.kind)) { into.Add(d); list.RemoveAt(i); }
            }
        }

        static Saved ToSaved(MoonDepotSnapshot d)
        {
            var e = ByKind(d.kind);
            if (e == null) return null;
            return new Saved
            {
                Key = e.Key, Owner = d.owner, Id = d.id, Support = d.support, Vehicles = d.vehicles, Style = d.style,
                X = d.x, Y = d.y, Z = d.z, Yaw = d.yaw, PlacedPose = d.placedPose, Qx = d.qx, Qy = d.qy, Qz = d.qz, Qw = d.qw, Stored = d.stored,
            };
        }

        /// <summary>Before the game builds the world's structures from a loaded save: add ours back.</summary>
        public static void BeforeRestore(MoonWorld world, MoonLooseDustSnapshot state)
        {
            if (state == null || !MarsState.CurrentIsMars) return;
            if (!Install(world)) return;
            string path = SidePath();
            if (path == null || !File.Exists(path)) return;
            try
            {
                var list = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(path), Json) ?? new List<Saved>();
                var have = new HashSet<int>();
                for (int i = 0; i < state.depots.Count; i++) have.Add(state.depots[i].id);
                int added = 0, unknown = 0;
                foreach (var s in list)
                {
                    var e = ByKey(s.Key);
                    if (e == null) { unknown++; continue; }
                    if (have.Contains(s.Id)) continue;
                    var d = new MoonDepotSnapshot();
                    d.id = s.Id; d.kind = e.Kind; d.owner = s.Owner;
                    d.x = s.X; d.y = s.Y; d.z = s.Z; d.yaw = s.Yaw; d.placedPose = s.PlacedPose;
                    d.qx = s.Qx; d.qy = s.Qy; d.qz = s.Qz; d.qw = s.Qw;
                    d.support = s.Support; d.stored = s.Stored; d.vehicles = s.Vehicles; d.style = s.Style;
                    state.depots.Add(d);
                    if (state.nextDepotId <= s.Id) state.nextDepotId = s.Id + 1;
                    added++;
                }
                Plugin.Log.LogMessage($"[vbuild] {added} Mars buildings loaded" + (unknown > 0 ? $", {unknown} of an unknown type skipped" : ""));
            }
            catch (Exception e) { Plugin.Log.LogError("[vbuild] could not load Mars buildings: " + e.Message); }
        }

        // ------------------------------------------------------------------ the world's current buildings

        public sealed class Placed { public int Id, Kind; public string Owner; public Vector3 Pos; public Quaternion Rot; }

        public static List<Placed> Current(MoonWorld world)
        {
            var list = new List<Placed>();
            if (world == null) return list;
            try
            {
                var dict = world.depots;
                if (dict == null) return list;
                foreach (var kv in dict)
                {
                    var d = kv.Value;
                    if (d == null || !IsOurs(d.kind)) continue;
                    var p = new Vector3(d.x, d.y, d.z);
                    var q = d.placedPose ? new Quaternion(d.qx, d.qy, d.qz, d.qw) : Quaternion.AngleAxis(d.yaw, p.normalized);
                    list.Add(new Placed { Id = d.id, Kind = d.kind, Owner = d.owner, Pos = p, Rot = q });
                }
            }
            catch (Exception e) { Log.Once("vbuild-current", "[vbuild] reading the world's buildings failed: " + e.Message); }
            return list;
        }

        /// <summary>Find the scene objects the game made from our prefabs (for animation), by their model child.</summary>
        public static List<(string type, GameObject go)> Instances()
        {
            var list = new List<(string, GameObject)>();
            foreach (var t in Look.All<Transform>())
            {
                if (t == null || !t.name.StartsWith("MarsModel:")) continue;
                var parent = t.parent;
                if (parent == null || !Look.InScene(t.gameObject)) continue;
                if (prefabHolder != null && parent.parent == prefabHolder.transform) continue;
                list.Add((t.name.Substring(10), parent.gameObject));
            }
            return list;
        }

        public static IEnumerable<string> Status()
        {
            yield return $"vanilla build: {(Failed ? "FAILED (" + FailReason + ")" : Ready ? "ready" : "not installed")}, cards {(catalogPublished ? "in the menu" : "hidden")}, kinds {FirstKind}-{LastKind}";
            foreach (var e in Entries)
                yield return $"  {e.Kind}  {e.Key}  ${e.Def.Price}  def {(e.Definition != null ? "ok" : "-")}  prefab {(e.Prefab != null ? "ok" : "-")}  card {(e.Card != null ? "loaded" : "-")}";
            if (GhostKind != 0) yield return $"  ghost kind {GhostKind}" + (Block != null ? $" blocked: {Block}" : "");
        }
    }
}
