using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MarsProbe
{
    /// <summary>
    /// Everything the probe writes lives in &lt;game&gt;/MarsProbe/.
    ///   Report/  - this run's findings (cleared at every start)
    ///   state/   - crumbs and the skip list, kept between runs
    /// Crumbs: before a risky unit of work we write "B key", after it "E key".
    /// If the game dies natively inside that unit, the next start finds a B without an E
    /// and adds the key to the skip list, so the same crash never happens twice.
    /// </summary>
    internal static class Out
    {
        public static string Root;
        public static string ReportDir;
        public static string StateDir;
        static string crumbPath, skipPath, reportPath;
        static readonly HashSet<string> skip = new HashSet<string>();
        public static readonly List<string> SkippedThisRun = new List<string>();
        public static readonly List<string> NewlySkipped = new List<string>();
        static FileStream crumbStream;
        static readonly object gate = new object();

        public static void Init(string gameRoot)
        {
            Root = Path.Combine(gameRoot, "MarsProbe");
            ReportDir = Path.Combine(Root, "Report");
            StateDir = Path.Combine(Root, "state");
            Directory.CreateDirectory(StateDir);
            if (Directory.Exists(ReportDir))
            {
                try { Directory.Delete(ReportDir, true); } catch { }
            }
            Directory.CreateDirectory(ReportDir);

            crumbPath = Path.Combine(StateDir, "crumbs.txt");
            skipPath = Path.Combine(StateDir, "skip.txt");
            reportPath = Path.Combine(ReportDir, "report.txt");

            if (File.Exists(skipPath))
                foreach (var l in File.ReadAllLines(skipPath))
                    if (l.Trim().Length > 0 && !l.StartsWith("#")) skip.Add(l.Trim());

            // Find units that started last run but never ended: those crashed the game.
            if (File.Exists(crumbPath))
            {
                var open = new List<string>();
                foreach (var l in File.ReadAllLines(crumbPath))
                {
                    if (l.StartsWith("B ")) open.Add(l.Substring(2));
                    else if (l.StartsWith("E ")) open.Remove(l.Substring(2));
                }
                foreach (var k in open)
                {
                    if (skip.Add(k)) NewlySkipped.Add(k);
                }
                if (NewlySkipped.Count > 0)
                    File.AppendAllLines(skipPath, NewlySkipped);
            }
            crumbStream = new FileStream(crumbPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        public static bool ShouldSkip(string key)
        {
            if (!skip.Contains(key)) return false;
            SkippedThisRun.Add(key);
            return true;
        }

        public static void Begin(string key) => Crumb("B " + key);
        public static void End(string key) => Crumb("E " + key);

        static void Crumb(string line)
        {
            lock (gate)
            {
                var b = Encoding.UTF8.GetBytes(line + "\n");
                crumbStream.Write(b, 0, b.Length);
                crumbStream.Flush(true);
            }
        }

        /// <summary>Append a line to the main report.txt.</summary>
        public static void Report(string line)
        {
            lock (gate)
            {
                File.AppendAllText(reportPath, line + Environment.NewLine);
            }
        }

        public static StreamWriter Writer(string relativePath)
        {
            string p = Path.Combine(ReportDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return new StreamWriter(p, false, new UTF8Encoding(false)) { NewLine = "\n" };
        }

        public static string Trunc(string s, int max)
        {
            if (s == null) return "null";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return s.Length <= max ? s : s.Substring(0, max) + "…(" + s.Length + ")";
        }
    }
}
