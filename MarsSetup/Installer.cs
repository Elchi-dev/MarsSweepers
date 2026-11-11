using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MarsSetup
{
    /// <summary>
    /// The player installer: one window, no terminal. It finds the game, installs BepInEx and the Mars mod
    /// (without the debug probe and without BepInEx's console window), walks the player through the one file
    /// that has to come from Nexus, and starts the game. Testers start MarsSetup.exe --dev for the old menu.
    /// </summary>
    internal sealed class Installer : Form
    {
        static readonly Color Bg = Color.FromArgb(28, 20, 18), Panel = Color.FromArgb(44, 31, 27), Text1 = Color.FromArgb(240, 228, 216),
                              Dim = Color.FromArgb(170, 150, 135), Rust = Color.FromArgb(224, 106, 52), Good = Color.FromArgb(124, 220, 150),
                              Bad = Color.FromArgb(255, 120, 100), Amber = Color.FromArgb(255, 194, 102), Cyan = Color.FromArgb(120, 210, 240);

        readonly Label gameLabel = new Label(), bepLabel = new Label(), patcherLabel = new Label(), modLabel = new Label(), hint = new Label();
        readonly Button install = new Button(), play = new Button(), patcherBtn = new Button(), cancel = new Button(), more = new Button();
        readonly RichTextBox log = new RichTextBox();
        readonly ProgressBar busy = new ProgressBar();
        Thread worker;

        public static int Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var f = new Installer();
            Program.PlayerMode = true;
            Program.Gui = f.Write;
            Program.GuiAsk = f.AskUser;
            string hintPath = args.FirstOrDefault(a => Directory.Exists(a) || File.Exists(a));
            f.Shown += (s, e) => f.Start(hintPath);
            Application.Run(f);
            return 0;
        }

        Installer()
        {
            Text = "Mars Sweepers";
            BackColor = Bg; ForeColor = Text1;
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            ClientSize = new Size(640, 560);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label { Text = "MARS SWEEPERS", Font = new Font("Segoe UI Black", 26f, FontStyle.Bold), ForeColor = Rust, AutoSize = true, Location = new Point(24, 18) };
            var sub = new Label { Text = "A Mars mod for Moon Sweepers. Made by Elchi.", ForeColor = Dim, AutoSize = true, Location = new Point(28, 66) };
            Controls.Add(title); Controls.Add(sub);

            var box = new System.Windows.Forms.Panel { BackColor = Panel, Location = new Point(24, 100), Size = new Size(592, 150) };
            Controls.Add(box);
            int y = 12;
            foreach (var l in new[] { gameLabel, bepLabel, patcherLabel, modLabel })
            {
                l.AutoSize = false; l.AutoEllipsis = true; l.Size = new Size(568, 26); l.Location = new Point(12, y); l.ForeColor = Dim; y += 28;
                box.Controls.Add(l);
            }
            hint.AutoSize = false; hint.Size = new Size(568, 24); hint.Location = new Point(12, y + 2); hint.ForeColor = Amber;
            box.Controls.Add(hint);

            Style(install, "Install", Rust, new Point(24, 266), new Size(180, 48));
            Style(play, "Play", Color.FromArgb(60, 140, 90), new Point(214, 266), new Size(180, 48));
            Style(patcherBtn, "I downloaded it…", Color.FromArgb(80, 64, 58), new Point(404, 266), new Size(212, 48));
            Style(cancel, "Cancel", Color.FromArgb(80, 64, 58), new Point(404, 266), new Size(212, 48));
            patcherBtn.Visible = false; cancel.Visible = false;
            install.Click += (s, e) => RunJob(() => { Program.GuiInstall(); });
            play.Click += (s, e) => RunJob(() => Program.GuiPlay());
            patcherBtn.Click += (s, e) => PickPatcher();
            cancel.Click += (s, e) => { Program.Cancel = true; };

            busy.Style = ProgressBarStyle.Marquee; busy.Location = new Point(24, 324); busy.Size = new Size(592, 6); busy.Visible = false;
            Controls.Add(busy);

            log.Location = new Point(24, 338); log.Size = new Size(592, 168);
            log.BackColor = Panel; log.ForeColor = Text1; log.BorderStyle = BorderStyle.None; log.ReadOnly = true; log.Font = new Font("Consolas", 9f);
            Controls.Add(log);

            Style(more, "More…", Color.FromArgb(60, 46, 42), new Point(24, 516), new Size(110, 30));
            more.Font = new Font("Segoe UI", 9f);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open the game folder", null, (s, e) => Program.GuiOpenGame());
            menu.Items.Add("Open the Mars saves folder", null, (s, e) => Program.GuiOpenSaves());
            menu.Items.Add("Uninstall the Mars mod (saves stay)", null, (s, e) => RunJob(() => Program.GuiRemoveMars()));
            menu.Items.Add("Remove all modding from the game", null, (s, e) => RunJob(() => Program.GuiRemoveAll()));
            menu.Items.Add("-");
            menu.Items.Add("Tester tools (console)", null, (s, e) => { try { Process.Start(Application.ExecutablePath, "--dev"); } catch { } });
            more.Click += (s, e) => menu.Show(more, new Point(0, more.Height));
            var ver = new Label { Text = "v" + Application.ProductVersion.Split('+')[0], ForeColor = Dim, AutoSize = true, Location = new Point(560, 522) };
            Controls.Add(ver);
        }

        void Style(Button b, string text, Color c, Point at, Size size)
        {
            b.Text = text; b.BackColor = c; b.ForeColor = Color.White; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold); b.Location = at; b.Size = size; b.Cursor = Cursors.Hand;
            Controls.Add(b);
        }

        // ------------------------------------------------------------------ start

        void Start(string hintPath)
        {
            if (!Program.GuiFindGame(hintPath))
            {
                using (var d = new OpenFileDialog { Title = "Where is MoonSweepers.exe? (Steam: right-click the game > Manage > Browse local files)", Filter = "MoonSweepers.exe|MoonSweepers.exe" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK || !Program.GuiFindGame(d.FileName))
                    {
                        Write(4, "Moon Sweepers was not found. Start this again once the game is installed.");
                        install.Enabled = play.Enabled = false;
                        Refresh2();
                        return;
                    }
                }
            }
            if (hintPath != null && Program.GuiIsPatcher(hintPath)) RunJob(() => Program.GuiPatcherFrom(hintPath));
            Refresh2();
        }

        /// <summary>The four status lines and which button is the next step.</summary>
        void Refresh2()
        {
            var st = Program.GuiState();
            gameLabel.Text = st.Game != null ? "Game:  " + st.Game : "Game:  not found";
            Line(bepLabel, "BepInEx (the mod loader)", st.Bep);
            Line(patcherLabel, "Moon Sweepers patcher (from Nexus Mods)", st.Patcher);
            Line(modLabel, "Mars mod", st.Mod, st.ModOld ? "update ready" : null);
            bool ready = st.Bep && st.Patcher && st.Mod && !st.ModOld;
            install.Text = ready ? "Repair" : st.Mod ? "Update" : "Install";
            play.Enabled = ready && worker == null;
            install.Enabled = st.Game != null && worker == null;
            hint.Text = st.Game == null ? "" : !st.Patcher && st.Bep ? "One file is missing: the patcher. Press Install, it walks you through it."
                      : ready ? (st.FirstStart ? "Ready. The first start takes a minute or two longer (it prepares the mod loader once)." : "Ready. Press Play.")
                      : "Press Install. Your saves are backed up first.";
        }

        void Line(Label l, string what, bool ok, string extra = null)
        {
            l.Text = (ok ? "✓  " : "•  ") + what + (extra != null ? "  (" + extra + ")" : ok ? "" : "  (missing)");
            l.ForeColor = extra != null ? Amber : ok ? Good : Dim;
        }

        // ------------------------------------------------------------------ jobs on a worker thread

        void RunJob(Action job)
        {
            if (worker != null) return;
            Program.Cancel = false;
            install.Enabled = play.Enabled = false; busy.Visible = true;
            worker = new Thread(() =>
            {
                try { job(); }
                catch (UnauthorizedAccessException e) { Write(4, "Windows refused to write a file: " + e.Message + ". Close the game and try again; if it keeps happening, right-click MarsSetup.exe > Run as administrator."); }
                catch (Exception e) { Write(4, "Something went wrong: " + e.Message); Program.GuiLogError(e); }
                finally
                {
                    BeginInvoke((Action)(() => { worker = null; busy.Visible = false; ShowPatcherWait(false); Refresh2(); }));
                }
            }) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        public void ShowPatcherWait(bool on)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => ShowPatcherWait(on))); return; }
            patcherBtn.Visible = on;
            cancel.Visible = on;
            if (on) { patcherBtn.Location = new Point(404, 266); patcherBtn.Size = new Size(212, 22); patcherBtn.Font = cancel.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold); cancel.Location = new Point(404, 292); cancel.Size = new Size(212, 22); }
        }

        public void ShowCancel(bool on)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => ShowCancel(on))); return; }
            cancel.Visible = on;
            if (on) { patcherBtn.Visible = false; cancel.Location = new Point(404, 266); cancel.Size = new Size(212, 48); cancel.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold); }
        }

        void PickPatcher()
        {
            using (var d = new OpenFileDialog { Title = "The patcher you downloaded from Nexus (the .zip, or MoonSweepers.ModEngine.dll)", Filter = "Patcher download|*.zip;MoonSweepers.ModEngine.dll|All files|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) Program.GuiPicked = d.FileName;
        }

        // ------------------------------------------------------------------ messages from the install code (any thread)

        void Write(int kind, string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Write(kind, text))); } catch { } return; }
            Program.GuiLog(kind, text);
            var c = kind == 0 ? Cyan : kind == 2 ? Good : kind == 3 ? Amber : kind == 4 ? Bad : Text1;
            string prefix = kind == 2 ? "✓ " : kind == 3 ? "! " : kind == 4 ? "✗ " : kind == 0 ? "" : "  ";
            log.SelectionStart = log.TextLength; log.SelectionLength = 0;
            log.SelectionColor = c;
            log.AppendText(prefix + text + Environment.NewLine);
            log.ScrollToCaret();
        }

        bool AskUser(string q, bool def)
        {
            if (InvokeRequired) return (bool)Invoke((Func<bool>)(() => AskUser(q, def)));
            return MessageBox.Show(this, q, "Mars Sweepers", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                   def ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public static Installer Current => Application.OpenForms.OfType<Installer>().FirstOrDefault();
    }
}
