using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The Mars contract goals: a short path from the first drill to the planet cannon, one goal at a time, each
    /// with a cash bonus for the team. The host counts what the crew makes and checks the goals; the state rides
    /// in the Mars data, so everyone sees the same goal and gets the same notice.
    /// </summary>
    internal static class Goals
    {
        public sealed class State
        {
            public int Done;                                              // goals finished, in order
            public Dictionary<string, int> Made = new Dictionary<string, int>();   // items produced by machines
            public int Trades;
            public bool ShieldUp, Fired;
        }

        sealed class Goal { public string Text; public int Reward; public Func<State, bool> Met; public Func<State, string> Progress; }

        static int Made(State s, string id) => s.Made.TryGetValue(id, out var n) ? n : 0;
        static bool Has(string type) => Buildings.Data.List.Any(b => b.Type == type);

        static readonly Goal[] All =
        {
            new Goal { Text = "Build an ore drill on a mine", Reward = 150, Met = s => Has("drill") },
            new Goal { Text = "Smelt 20 iron plates", Reward = 300, Met = s => Made(s, "iron_plate") >= 20, Progress = s => $"{Made(s, "iron_plate")}/20" },
            new Goal { Text = "Power the base: a solar panel and a battery", Reward = 300, Met = s => Has("solar") && Has("battery") },
            new Goal { Text = "Make 10 machine parts (crusher, spinner, assembler)", Reward = 800, Met = s => Made(s, "machine_part") >= 10, Progress = s => $"{Made(s, "machine_part")}/10" },
            new Goal { Text = "Find water ice near a pole and make 5 rocket fuel", Reward = 1200, Met = s => Made(s, "rocket_fuel") >= 5, Progress = s => $"{Made(s, "rocket_fuel")}/5" },
            new Goal { Text = "Trade with the aliens", Reward = 600, Met = s => s.Trades >= 1 },
            new Goal { Text = "Keep a storm out with a shield dome", Reward = 2000, Met = s => s.ShieldUp },
            new Goal { Text = "Fire the planet cannon at the Moon", Reward = 0, Met = s => s.Fired },
        };

        const string Key = "goals";
        public static State Data = new State();
        static bool dirty;
        static int shownDone = -1;
        static float nextCheck;

        static Goals()
        {
            MarsData.Changed += key =>
            {
                if (key != "" && key != Key) return;
                Data = MarsData.Get<State>(Key);
                if (shownDone >= 0 && Data.Done > shownDone)
                    for (int i = shownDone; i < Data.Done && i < All.Length; i++) Announce(i);
                shownDone = Data.Done;
            };
        }

        public static void Init() { }

        // ------------------------------------------------------------------ host counts

        public static void Produced(string item, int n) { if (!MarsNet.IsHost) return; Data.Made[item] = (Data.Made.TryGetValue(item, out var v) ? v : 0) + n; dirty = true; }
        public static void Traded() { if (!MarsNet.IsHost) return; Data.Trades++; dirty = true; }
        public static void ShieldRaised() { if (!MarsNet.IsHost || Data.ShieldUp) return; Data.ShieldUp = true; dirty = true; }
        public static void CannonFired() { if (!MarsNet.IsHost) return; Data.Fired = true; dirty = true; }

        public static void Tick(float now)
        {
            if (!MarsData.Loaded || !MarsNet.IsHost || now < nextCheck) return;
            nextCheck = now + 2f;
            bool changed = dirty;
            while (Data.Done < All.Length && All[Data.Done].Met(Data))
            {
                var g = All[Data.Done];
                if (g.Reward > 0) Buildings.Earn(Commands.FindWorld(), Buildings.Me, g.Reward);
                Plugin.Log.LogMessage($"[goals] done: {g.Text} (+${g.Reward})");
                Data.Done++;
                changed = true;
            }
            if (!changed) return;
            dirty = false;
            MarsData.Set(Key, Data);   // the Changed event shows the notice here too
        }

        static void Announce(int i)
        {
            var g = All[i];
            MarsWorld.Urgent($"GOAL DONE: {g.Text}" + (g.Reward > 0 ? $"  +${g.Reward} for the crew" : ""));
            if (i + 1 < All.Length) MarsWorld.Notice("Next goal: " + All[i + 1].Text);
        }

        public static string Current()
        {
            if (Data.Done >= All.Length) return "All Mars goals done. The Moon had it coming.";
            var g = All[Data.Done];
            return $"Goal {Data.Done + 1}/{All.Length}: {g.Text}" + (g.Progress != null ? $" ({g.Progress(Data)})" : "") + (g.Reward > 0 ? $"  · bonus ${g.Reward}" : "");
        }

        public static IEnumerable<string> Report()
        {
            for (int i = 0; i < All.Length; i++)
                yield return $"  {(i < Data.Done ? "[x]" : i == Data.Done ? "[>]" : "[ ]")} {All[i].Text}" + (i == Data.Done && All[i].Progress != null ? $" ({All[i].Progress(Data)})" : "") + (All[i].Reward > 0 ? $"  ${All[i].Reward}" : "");
        }

        /// <summary>Shown once when a Mars world opens.</summary>
        public static void Opened() { shownDone = Data.Done; MarsWorld.Notice(Current()); }

        public static void Reset() { Data = new State(); shownDone = -1; dirty = false; }
    }
}
