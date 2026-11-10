using System;
using System.Collections.Generic;
using System.IO;

namespace MarsMod
{
    /// <summary>
    /// The Mars employment agreement. The game's contract text is loaded from its resources and then parsed;
    /// the Mars mod rewrites that raw text (clause by clause where we wrote a Mars version, the generic
    /// Moon → Mars wording for everything else) before the game ever sees it.
    /// </summary>
    internal static class Contract
    {
        // Original sentence → Mars sentence. Matching is on plain text, so markup around it stays intact.
        static readonly (string from, string to)[] Clauses =
        {
            ("LUNAR SURFACE DIVISION", "MARTIAN SURFACE DIVISION"),
            ("Congratulations. Your application was the cheapest.",
             "Congratulations on cleaning the Moon. As a reward, your application for Mars was approved. It was the only one."),
            ("Your shift ends when the Moon is clean.", "Your shift ends when Mars is clean."),
            ("“The Moon” includes all visible surfaces, concealed surfaces, and surfaces management discovers after you finish.",
             "“Mars” includes all visible surfaces, concealed surfaces, both moons, and any dust the wind puts back after you finish."),
            ("Lost or damaged equipment will be deducted from your pay at its replacement cost, including delivery from Earth.",
             "Lost or damaged equipment will be deducted from your pay at its replacement cost, including delivery from Earth (seven months, no tracking number)."),
            ("You may earn money by collecting lunar dust.",
             "You may earn money by collecting Martian dust. Red dust is worth exactly as much as grey dust. Please stop asking."),
            ("Staring at Earth counts as unauthorized remote working.",
             "Staring at Earth counts as unauthorized remote working. Earth is the small blue dot. Stop looking for it."),
            ("Keep your helmet on.",
             "Keep your helmet on. Dust storms are not a reason to stop working. They are a reason to work faster."),
            ("Leaving the Moon during your shift is prohibited.",
             "Leaving Mars during your shift is prohibited. Leaving Mars at all is currently not technically possible."),
            ("Complaints must be submitted in person to Human Resources on Earth.",
             "Complaints must be submitted in person to Human Resources on Earth. The next launch window is in 26 months."),
            ("I accept that the Moon’s size was publicly available information.",
             "I accept that Mars has nearly four times the Moon’s surface area, and that this was publicly available information."),
            ("I have read and understood this agreement, or have elected to become useful instead.",
             "I have read and understood this agreement, or have elected to become useful instead. Again."),
        };

        public static string ForMars(string raw)
        {
            Save("PlayerContract.raw.txt", raw);
            string s = raw;
            int hits = 0;
            foreach (var (from, to) in Clauses)
                if (s.Contains(from)) { s = s.Replace(from, to); hits++; }
            // Anything we didn't write a Mars clause for still gets the Moon → Mars wording.
            s = Text.MarsifyText(s);
            Save("PlayerContract.mars.txt", s);
            Plugin.Log.LogInfo($"[contract] {hits}/{Clauses.Length} Mars clauses placed");
            return s;
        }

        /// <summary>Mars sentences must not be rewritten again by the text pass (they mention the Moon on purpose).</summary>
        public static IEnumerable<string> OwnLines()
        {
            foreach (var (_, to) in Clauses) yield return to;
        }

        static void Save(string name, string text)
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "originals");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name), text ?? "");
            }
            catch { }
        }
    }
}
