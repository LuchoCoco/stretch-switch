// Fenêtre principale + surveillance des jeux + raccourcis clavier.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Stretch Switch")]
[assembly: AssemblyProduct("Stretch Switch")]
[assembly: AssemblyVersion(StretchSwitch.Updater.Version + ".0")]
[assembly: AssemblyFileVersion(StretchSwitch.Updater.Version + ".0")]

namespace StretchSwitch
{
    class MainForm : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        const int HK_TOGGLE = 1, HK_UP = 2, HK_DOWN = 3, HK_RESET = 4;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;

        AppConfig cfg;
        ListView list = new ListView();
        Label lblScreen = new Label(), lblStatus = new Label(), lblKeys = new Label();
        CheckBox chkStartup = new CheckBox(), chkMinimized = new CheckBox(), chkNotif = new CheckBox(), chkDark = new CheckBox(), chkKeys = new CheckBox();
        NotifyIcon tray = new NotifyIcon();
        ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem launchMenu = new ToolStripMenuItem("Lancer un jeu");
        System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer { Interval = 1000 };
        System.Windows.Forms.Timer focus = new System.Windows.Forms.Timer { Interval = 300 };
        System.Windows.Forms.Timer testTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        Osd osd = new Osd();

        GameProfile active, suppressed;
        SavedState saved;
        bool testing, exiting, populating, trayHintShown, startHidden, hotkeysOn;
        int testLeft;
        // lancement en cours : on garde les réglages en attendant que le jeu démarre
        DateTime launchDeadline = DateTime.MinValue;
        bool seenRunning;
        // vibrance de la session (modifiable au clavier)
        int sessionVib = 100, lastVibOn = 200;
        // Alt+Tab
        bool onDesktop, wasForeground;
        int awayTicks;
        Dictionary<uint, bool> pidIsGame = new Dictionary<uint, bool>();
        // applis fermées à rouvrir
        readonly object closedLock = new object();
        List<string> closedApps = new List<string>();

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "StretchSwitch";

        public MainForm(bool minimized)
        {
            cfg = Store.LoadConfig();
            Theme.Dark = cfg.DarkTheme;
            startHidden = minimized || cfg.StartMinimized;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Stretch Switch " + Updater.Version;
            Icon = Ui.MakeIcon();
            Font = Ui.Normal;
            ClientSize = new Size(800, 520);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;

            Controls.Add(new Label { Text = "Mes jeux", Font = Ui.Title, Left = 12, Top = 10, Width = 300, Height = 26 });

            list.SetBounds(12, 42, 640, 330);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.CheckBoxes = true;
            list.Columns.Add("Jeu", 160); list.Columns.Add("Processus", 150); list.Columns.Add("Résolution", 115);
            list.Columns.Add("Étiré", 45); list.Columns.Add("HDR off", 55); list.Columns.Add("Vibrance", 60); list.Columns.Add("Lancer", 50);
            Controls.Add(list);

            int bx = 664, by = 42;
            var bAdd = MakeBtn("Ajouter un jeu", bx, by); by += 36;
            var bInstalled = MakeBtn("Mes jeux installés…", bx, by); by += 36;
            var bEdit = MakeBtn("Modifier", bx, by); by += 36;
            var bDel = MakeBtn("Supprimer", bx, by); by += 48;
            var bLaunch = MakeBtn("▶  Lancer le jeu", bx, by); bLaunch.Font = Ui.Bold; by += 36;
            var bTest = MakeBtn("Tester 10 s", bx, by); by += 48;
            var bExport = MakeBtn("Exporter…", bx, by); by += 36;
            var bImport = MakeBtn("Importer…", bx, by); by += 36;
            var bUpdate = MakeBtn("Mises à jour…", bx, by);

            lblScreen.SetBounds(12, 384, 640, 20); lblScreen.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
            lblScreen.Tag = "muted";
            Controls.Add(lblScreen);
            lblStatus.SetBounds(12, 408, 776, 22); lblStatus.Font = Ui.Bold; lblStatus.Tag = "accent";
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(lblStatus);
            lblKeys.SetBounds(12, 434, 776, 20); lblKeys.Tag = "muted"; lblKeys.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(lblKeys);

            Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Left = 12, Top = 464, Width = 776, Height = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom });
            int cy = 482;
            chkStartup.Text = "Lancer avec Windows"; chkStartup.SetBounds(12, cy, 156, 24);
            chkMinimized.Text = "Démarrer réduit"; chkMinimized.SetBounds(170, cy, 124, 24);
            chkNotif.Text = "Notifications"; chkNotif.SetBounds(296, cy, 108, 24);
            chkDark.Text = "Thème sombre"; chkDark.SetBounds(406, cy, 116, 24);
            chkKeys.Text = "Raccourcis clavier"; chkKeys.SetBounds(524, cy, 134, 24);
            foreach (var c in new[] { chkStartup, chkMinimized, chkNotif, chkDark, chkKeys }) { c.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; Controls.Add(c); }
            var bQuit = new Button { Text = "Quitter", Left = 664, Top = 478, Width = 124, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            Controls.Add(bQuit);
            ResumeLayout(false);

            chkStartup.Checked = IsStartupEnabled();
            chkMinimized.Checked = cfg.StartMinimized;
            chkNotif.Checked = cfg.Notifications;
            chkDark.Checked = cfg.DarkTheme;
            chkKeys.Checked = cfg.HotkeysEnabled;

            // ---------- icône près de l'horloge
            tray.Icon = Icon; tray.Text = "Stretch Switch"; tray.Visible = true;
            menu.Items.Add("Ouvrir", null, (s, e) => ShowWindow());
            menu.Items.Add(launchMenu);
            menu.Items.Add("Remettre l'écran d'origine", null, (s, e) => EmergencyReset());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => QuitApp());
            menu.Opening += (s, e) => BuildLaunchMenu();
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowWindow();

            // ---------- événements
            bAdd.Click += (s, e) => AddProfile();
            bInstalled.Click += (s, e) => AddInstalled();
            bEdit.Click += (s, e) => EditSelected();
            bDel.Click += (s, e) => DeleteSelected();
            bLaunch.Click += (s, e) => { var p = SelectedProfile(); if (p == null) Info("Sélectionne un jeu dans la liste."); else LaunchGame(p); };
            bTest.Click += (s, e) => TestSelected();
            bExport.Click += (s, e) => ExportConfig();
            bImport.Click += (s, e) => ImportConfig();
            bUpdate.Click += (s, e) => CheckUpdatesManual();
            bQuit.Click += (s, e) => QuitApp();
            list.DoubleClick += (s, e) => EditSelected();
            list.ItemChecked += (s, e) =>
            {
                if (populating || !(e.Item.Tag is GameProfile)) return;
                ((GameProfile)e.Item.Tag).Enabled = e.Item.Checked;
                Store.SaveConfig(cfg);
            };
            chkStartup.CheckedChanged += (s, e) => SetStartup(chkStartup.Checked);
            chkMinimized.CheckedChanged += (s, e) => { cfg.StartMinimized = chkMinimized.Checked; Store.SaveConfig(cfg); };
            chkNotif.CheckedChanged += (s, e) => { cfg.Notifications = chkNotif.Checked; Store.SaveConfig(cfg); };
            chkDark.CheckedChanged += (s, e) =>
            {
                cfg.DarkTheme = chkDark.Checked; Store.SaveConfig(cfg);
                Theme.Dark = cfg.DarkTheme; Theme.Apply(this); Theme.Apply(menu);
                if (active == null && !testing) SetIdleStatus();
            };
            chkKeys.CheckedChanged += (s, e) =>
            {
                cfg.HotkeysEnabled = chkKeys.Checked; Store.SaveConfig(cfg);
                UnregisterHotkeys(); if (cfg.HotkeysEnabled) RegisterHotkeys();
                UpdateKeysLabel();
            };
            SystemEvents.SessionEnding += (s, e) => Restore();

            PopulateList();
            RecoverAfterCrash();
            RefreshScreenInfo();
            SetIdleStatus();
            Theme.Apply(this);
            Theme.Apply(menu);

            watch.Tick += (s, e) => Tick();
            watch.Start();
            focus.Tick += (s, e) => FocusTick();
            focus.Start();
            testTimer.Tick += (s, e) =>
            {
                testLeft--;
                if (testLeft > 0) { lblStatus.Text = "Test en cours… retour à la normale dans " + testLeft + " s"; return; }
                StopTest(); Restore(); SetIdleStatus();
            };

            GameScanner.Prewarm();
            if (Updater.Configured && cfg.CheckUpdates) CheckUpdatesInBackground();
        }

        Button MakeBtn(string text, int x, int y)
        {
            var b = new Button { Text = text, Left = x, Top = y, Width = 124, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(b);
            return b;
        }

        void Info(string text) { MessageBox.Show(this, text, "Stretch Switch"); }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        void ShowWindow()
        {
            startHidden = false;
            Show(); WindowState = FormWindowState.Normal; Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!trayHintShown)
                {
                    trayHintShown = true;
                    tray.ShowBalloonTip(3000, "Stretch Switch tourne toujours", "Il surveille tes jeux en arrière-plan. Clic droit sur l'icône près de l'horloge pour quitter.", ToolTipIcon.Info);
                }
                return;
            }
            StopTest();
            Restore();
            UnregisterHotkeys();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        void QuitApp()
        {
            exiting = true;
            Close();
            Application.Exit();
        }

        // ================================================================== raccourcis clavier

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (cfg.HotkeysEnabled) RegisterHotkeys();
            UpdateKeysLabel();
        }

        void RegisterHotkeys()
        {
            if (hotkeysOn || !IsHandleCreated) return;
            bool a = RegisterHotKey(Handle, HK_TOGGLE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.V);
            bool b = RegisterHotKey(Handle, HK_UP, MOD_CONTROL | MOD_ALT, (uint)Keys.Up);
            bool c = RegisterHotKey(Handle, HK_DOWN, MOD_CONTROL | MOD_ALT, (uint)Keys.Down);
            bool d = RegisterHotKey(Handle, HK_RESET, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.R);
            hotkeysOn = true;
            if (!(a && b && c && d)) lblKeys.Text = "⚠ Certains raccourcis sont déjà pris par une autre appli.";
        }

        void UnregisterHotkeys()
        {
            if (!hotkeysOn || !IsHandleCreated) return;
            for (int i = 1; i <= 4; i++) UnregisterHotKey(Handle, i);
            hotkeysOn = false;
        }

        void UpdateKeysLabel()
        {
            if (lblKeys.Text.StartsWith("⚠")) return;
            lblKeys.Text = cfg.HotkeysEnabled
                ? "Raccourcis : Ctrl+Alt+V vibrance on/off  ·  Ctrl+Alt+↑ / ↓ vibrance ±10 %  ·  Ctrl+Alt+R tout remettre d'origine"
                : "Raccourcis clavier désactivés.";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) // WM_HOTKEY
            {
                switch (m.WParam.ToInt32())
                {
                    case HK_TOGGLE: ToggleVibrance(); break;
                    case HK_UP: StepVibrance(+10); break;
                    case HK_DOWN: StepVibrance(-10); break;
                    case HK_RESET: EmergencyReset(); break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        void ToggleVibrance()
        {
            if (Vibrance.Current != 100)
            {
                lastVibOn = Vibrance.Current;
                sessionVib = 100; Vibrance.Base = 100; Vibrance.Set(100);
                osd.ShowText("Vibrance : normale");
            }
            else
            {
                int v = active != null && active.Vibrance != 100 ? active.Vibrance : lastVibOn;
                if (v == 100) v = 200;
                sessionVib = v; Vibrance.Base = v; Vibrance.Set(v);
                osd.ShowText("Vibrance : " + v + " %");
            }
        }

        void StepVibrance(int delta)
        {
            int v = Math.Max(0, Math.Min(400, Vibrance.Current + delta));
            sessionVib = v; Vibrance.Base = v; Vibrance.Set(v);
            if (v != 100) lastVibOn = v;
            if (active != null)
            {
                active.Vibrance = v; Store.SaveConfig(cfg); PopulateList();
                osd.ShowText("Vibrance : " + v + " %  (" + active.Name + ")");
            }
            else osd.ShowText("Vibrance : " + v + " %");
        }

        void EmergencyReset()
        {
            StopTest();
            if (active != null)
            {
                suppressed = active; // ne pas réappliquer tant que ce jeu tourne
                launchDeadline = DateTime.MinValue;
                Restore();
                SetIdleStatus();
                lblStatus.Text = "Écran remis d'origine. Réactivé au prochain lancement du jeu.";
            }
            else { sessionVib = 100; Vibrance.Base = 100; Vibrance.Set(100); Restore(); }
            osd.ShowText("Écran remis d'origine");
        }

        // ================================================================== liste

        void PopulateList()
        {
            populating = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var p in cfg.Profiles)
            {
                var it = new ListViewItem(p.Name);
                it.SubItems.Add(string.Join(", ", Catalog.SplitNames(p.ProcessName).Select(n => n + ".exe")));
                it.SubItems.Add(p.ResolutionText());
                it.SubItems.Add(p.ChangeResolution && p.Stretch ? "oui" : "—");
                it.SubItems.Add(p.DisableHdr ? "oui" : "—");
                it.SubItems.Add(p.Vibrance == 100 ? "—" : p.Vibrance + " %");
                it.SubItems.Add(string.IsNullOrWhiteSpace(p.LaunchCommand) ? "—" : "oui");
                it.Checked = p.Enabled;
                it.Tag = p;
                list.Items.Add(it);
            }
            list.EndUpdate();
            populating = false;
        }

        GameProfile SelectedProfile()
        {
            if (list.SelectedItems.Count == 0) return null;
            return list.SelectedItems[0].Tag as GameProfile;
        }

        void AddProfile()
        {
            var p = new GameProfile();
            using (var f = new ProfileForm(p))
                if (f.ShowDialog(this) == DialogResult.OK) { cfg.Profiles.Add(p); Store.SaveConfig(cfg); PopulateList(); SelectProfile(p); }
        }

        void AddInstalled()
        {
            Cursor = Cursors.WaitCursor;
            InstalledGamesForm f;
            try { f = new InstalledGamesForm(cfg.Profiles); } finally { Cursor = Cursors.Default; }
            using (f)
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Chosen.Count == 0) return;
                foreach (var g in f.Chosen)
                    cfg.Profiles.Add(new GameProfile { Name = g.Name.Replace(" (en partie)", ""), ProcessName = g.Procs, LaunchCommand = g.Launch });
                Store.SaveConfig(cfg); PopulateList();
                Info(f.Chosen.Count + " jeu(x) ajouté(s). Clique « Modifier » pour régler chacun (résolution, vibrance…).");
            }
        }

        void SelectProfile(GameProfile p)
        {
            foreach (ListViewItem it in list.Items) if (it.Tag == p) { it.Selected = true; it.EnsureVisible(); }
        }

        void EditSelected()
        {
            var p = SelectedProfile();
            if (p == null) { Info("Sélectionne un jeu dans la liste."); return; }
            using (var f = new ProfileForm(p))
                if (f.ShowDialog(this) == DialogResult.OK) { Store.SaveConfig(cfg); PopulateList(); SelectProfile(p); }
        }

        void DeleteSelected()
        {
            var p = SelectedProfile();
            if (p == null) return;
            if (MessageBox.Show(this, "Supprimer « " + p.Name + " » ?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            cfg.Profiles.Remove(p);
            Store.SaveConfig(cfg);
            PopulateList();
        }

        void BuildLaunchMenu()
        {
            launchMenu.DropDownItems.Clear();
            foreach (var p in cfg.Profiles.Where(x => !string.IsNullOrWhiteSpace(x.LaunchCommand)))
            {
                var prof = p;
                launchMenu.DropDownItems.Add(prof.Name, null, (s, e) => LaunchGame(prof));
            }
            if (launchMenu.DropDownItems.Count == 0) launchMenu.DropDownItems.Add("(aucun jeu avec lancement)").Enabled = false;
            Theme.Apply(menu);
        }

        void ExportConfig()
        {
            using (var d = new SaveFileDialog { Filter = "Profils Stretch Switch (*.xml)|*.xml", FileName = "stretch-switch-jeux.xml" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var copy = new AppConfig { Profiles = cfg.Profiles };
                try { Store.Export(d.FileName, copy); Info("Profils exportés. Envoie ce fichier à tes potes, ils feront « Importer… »."); }
                catch (Exception ex) { Info("Erreur : " + ex.Message); }
            }
        }

        void ImportConfig()
        {
            using (var d = new OpenFileDialog { Filter = "Profils Stretch Switch (*.xml)|*.xml" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var other = Store.Import(d.FileName);
                if (other == null) { Info("Fichier illisible."); return; }
                int added = 0;
                foreach (var p in other.Profiles)
                {
                    var existing = cfg.Profiles.FirstOrDefault(x => string.Equals(x.ProcessName, p.ProcessName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) cfg.Profiles.Remove(existing);
                    cfg.Profiles.Add(p); added++;
                }
                Store.SaveConfig(cfg); PopulateList();
                Info(added + " jeu(x) importé(s). Vérifie que les résolutions existent bien sur ton écran.");
            }
        }

        // ================================================================== état / messages

        void RefreshScreenInfo()
        {
            try
            {
                string dev = Display.PrimaryDevice();
                var dm = Display.Current(dev);
                lblScreen.Text = "Écran principal : " + dm.dmPelsWidth + " x " + dm.dmPelsHeight + " @ " + dm.dmDisplayFrequency + " Hz   •   HDR : " + Hdr.Describe(dev);
            }
            catch { lblScreen.Text = ""; }
        }

        void SetIdleStatus()
        {
            lblStatus.ForeColor = Theme.Text;
            int n = cfg.Profiles.Count(p => p.Enabled);
            lblStatus.Text = n == 0 ? "Ajoute un jeu pour commencer." : "En attente d'un jeu… (" + n + " surveillé" + (n > 1 ? "s" : "") + ")";
            tray.Text = "Stretch Switch — en attente";
        }

        void SetGameStatus(GameProfile p, string extra)
        {
            lblStatus.ForeColor = Ui.Accent;
            lblStatus.Text = "🎮 " + p.Name + " — " + (extra ?? Summary(p));
            tray.Text = Trunc("Stretch Switch — " + p.Name, 63);
        }

        void Notify(string text)
        {
            if (cfg.Notifications) tray.ShowBalloonTip(2500, "Stretch Switch", text, ToolTipIcon.Info);
        }

        static string Trunc(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }

        static string Summary(GameProfile p)
        {
            var parts = new List<string>();
            if (p.ChangeResolution) parts.Add(p.ResolutionText());
            if (p.DisableHdr) parts.Add("HDR coupé");
            if (p.Vibrance != 100) parts.Add("vibrance " + p.Vibrance + " %");
            if (!string.IsNullOrWhiteSpace(p.CloseApps)) parts.Add("applis fermées");
            return parts.Count == 0 ? "en cours" : string.Join(", ", parts);
        }

        // ================================================================== surveillance

        static bool IsRunning(string names)
        {
            foreach (var name in Catalog.SplitNames(names))
            {
                var ps = Process.GetProcessesByName(name);
                bool r = ps.Length > 0;
                foreach (var p in ps) p.Dispose();
                if (r) return true;
            }
            return false;
        }

        void Tick()
        {
            if (testing) return;
            if (suppressed != null && !IsRunning(suppressed.ProcessName)) suppressed = null;

            if (active != null)
            {
                if (IsRunning(active.ProcessName)) { seenRunning = true; return; }
                if (!seenRunning && DateTime.Now < launchDeadline) return; // le jeu est en train de se lancer
                string n = active.Name;
                bool wasLaunch = !seenRunning;
                Restore();
                SetIdleStatus();
                Notify(wasLaunch ? n + " ne s'est pas lancé — écran remis comme avant." : n + " fermé — écran remis comme avant.");
                return;
            }
            foreach (var p in cfg.Profiles)
            {
                if (p.Enabled && p != suppressed && IsRunning(p.ProcessName))
                {
                    StartSession(p, false);
                    break;
                }
            }
        }

        void StartSession(GameProfile p, bool launching)
        {
            string err = Apply(p);
            seenRunning = !launching;
            SetGameStatus(p, launching ? "lancement… " + Summary(p) : null);
            Notify(err ?? (p.Name + (launching ? " : lancement — " : " détecté — ") + Summary(p)));
        }

        void LaunchGame(GameProfile p)
        {
            if (string.IsNullOrWhiteSpace(p.LaunchCommand)) { Info("Pas de commande de lancement pour « " + p.Name + " ». Clique « Modifier » > Lancement."); return; }
            if (active != null && active != p) { Info(active.Name + " est déjà en cours."); return; }
            if (testing) { StopTest(); Restore(); }
            if (active == null)
            {
                suppressed = null;
                launchDeadline = DateTime.Now.AddMinutes(3);
                StartSession(p, true);
            }
            string err = Launcher.Start(p.LaunchCommand);
            if (err != null)
            {
                Info(err);
                if (!seenRunning) { Restore(); SetIdleStatus(); }
            }
        }

        string Apply(GameProfile p)
        {
            string dev = Display.PrimaryDevice();
            var cur = Display.Current(dev);
            var st = new SavedState
            {
                Device = dev, ProfileName = p.Name,
                Width = (int)cur.dmPelsWidth, Height = (int)cur.dmPelsHeight,
                Frequency = (int)cur.dmDisplayFrequency, Bpp = (int)cur.dmBitsPerPel,
                FixedOutput = cur.dmDisplayFixedOutput
            };
            active = p; saved = st;
            onDesktop = false; wasForeground = false; awayTicks = 0; pidIsGame.Clear();
            string error = null;

            if (p.DisableHdr)
            {
                LUID a; uint t; bool sup, en;
                if (Hdr.FindTarget(dev, out a, out t) && Hdr.GetState(a, t, out sup, out en) && en)
                {
                    st.HdrChanged = true; Store.SaveState(st);
                    if (Hdr.Set(a, t, false)) Thread.Sleep(500);
                    else { st.HdrChanged = false; error = "Impossible de couper le HDR."; }
                }
            }
            if (p.ChangeResolution)
            {
                st.ResolutionChanged = true; Store.SaveState(st);
                string err = Display.SetMode(dev, p.Width, p.Height, p.RefreshRate, p.Stretch);
                if (err != null) { st.ResolutionChanged = false; error = err; }
            }
            sessionVib = p.Vibrance;
            if (p.Vibrance != 100)
            {
                lastVibOn = p.Vibrance;
                Vibrance.Base = p.Vibrance;
                if (!Vibrance.Set(p.Vibrance)) error = (error == null ? "" : error + " ") + "Impossible d'appliquer la vibrance.";
            }
            if (st.HdrChanged || st.ResolutionChanged) Store.SaveState(st); else Store.DeleteState();

            // fermeture des applis en arrière-plan (pour ne pas bloquer la fenêtre)
            if (!string.IsNullOrWhiteSpace(p.CloseApps) && !testing)
            {
                string names = p.CloseApps, game = p.ProcessName;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var paths = AppCloser.CloseAll(names, game);
                    lock (closedLock) closedApps.AddRange(paths);
                });
            }
            RefreshScreenInfo();
            return error;
        }

        void Restore()
        {
            Vibrance.Base = 100;
            if (Vibrance.Current != 100) Vibrance.Set(100);
            sessionVib = 100;
            var st = saved;
            if (st != null)
            {
                try
                {
                    if (st.ResolutionChanged) Display.Restore(st);
                    if (st.HdrChanged)
                    {
                        if (st.ResolutionChanged) Thread.Sleep(400);
                        LUID a; uint t;
                        if (Hdr.FindTarget(st.Device, out a, out t)) Hdr.Set(a, t, true);
                    }
                }
                catch { }
            }
            List<string> reopen;
            lock (closedLock) { reopen = closedApps; closedApps = new List<string>(); }
            if (active != null && active.RelaunchApps && reopen.Count > 0)
                ThreadPool.QueueUserWorkItem(_ => AppCloser.Relaunch(reopen));

            Store.DeleteState();
            active = null; saved = null; onDesktop = false;
            launchDeadline = DateTime.MinValue;
            RefreshScreenInfo();
        }

        void RecoverAfterCrash()
        {
            var st = Store.LoadState();
            if (st == null) return;
            saved = st;
            Restore();
            Notify("Réglages d'origine restaurés (l'app s'était fermée pendant une partie).");
        }

        // ================================================================== Alt+Tab

        // true = le jeu est au premier plan, false = autre chose, null = on ne sait pas
        bool? GameIsForeground()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return null;
            bool isGame;
            if (!pidIsGame.TryGetValue(pid, out isGame))
            {
                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                        isGame = Catalog.SplitNames(active.ProcessName).Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
                }
                catch { isGame = false; }
                pidIsGame[pid] = isGame;
            }
            return isGame;
        }

        void FocusTick()
        {
            if (active == null || testing || !active.AltTabRestore || saved == null) return;
            bool? fg = GameIsForeground();
            if (fg == null) return;

            if (fg.Value)
            {
                awayTicks = 0; wasForeground = true;
                if (onDesktop)
                {
                    onDesktop = false;
                    if (saved.ResolutionChanged) Display.SetMode(saved.Device, active.Width, active.Height, active.RefreshRate, active.Stretch);
                    if (sessionVib != 100) Vibrance.Set(sessionVib);
                    SetGameStatus(active, null);
                    RefreshScreenInfo();
                }
            }
            else if (wasForeground && !onDesktop)
            {
                if (++awayTicks < 2) return; // petite marge pour éviter les allers-retours
                onDesktop = true;
                if (saved.ResolutionChanged) Display.Restore(saved);
                if (Vibrance.Current != 100) Vibrance.Set(100);
                SetGameStatus(active, "en arrière-plan (bureau en résolution normale)");
                RefreshScreenInfo();
            }
        }

        // ================================================================== test

        void TestSelected()
        {
            var p = SelectedProfile();
            if (p == null) { Info("Sélectionne un jeu dans la liste."); return; }
            if (active != null || testing) { Info("Un jeu ou un test est déjà en cours."); return; }
            testing = true;
            string err = Apply(p);
            if (err != null) MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            testLeft = 10;
            lblStatus.ForeColor = Ui.Accent;
            lblStatus.Text = "Test en cours… retour à la normale dans " + testLeft + " s";
            testTimer.Start();
        }

        void StopTest()
        {
            if (!testing) return;
            testTimer.Stop();
            testing = false;
        }

        // ================================================================== mises à jour

        void CheckUpdatesInBackground()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Updater.Release r = null;
                try { r = Updater.Check(); } catch { }
                if (r != null) try { BeginInvoke((Action)(() => OfferUpdate(r))); } catch { }
            });
        }

        void CheckUpdatesManual()
        {
            if (!Updater.Configured)
            {
                Info("Version " + Updater.Version + ".\n\nLes mises à jour automatiques ne sont pas encore branchées (aucun dépôt GitHub configuré).");
                return;
            }
            Updater.Release r;
            Cursor = Cursors.WaitCursor;
            try { r = Updater.Check(); }
            catch (Exception ex) { Cursor = Cursors.Default; Info("Impossible de vérifier : " + ex.Message); return; }
            Cursor = Cursors.Default;
            if (r == null) Info("Tu as déjà la dernière version (" + Updater.Version + ").");
            else OfferUpdate(r);
        }

        void OfferUpdate(Updater.Release r)
        {
            string notes = r.Notes.Length > 600 ? r.Notes.Substring(0, 600) + "…" : r.Notes;
            if (MessageBox.Show(this, "Nouvelle version " + r.Tag + " disponible (tu as " + Updater.Version + ").\n\n" + notes + "\n\nL'installer maintenant ? L'app va redémarrer.",
                                "Mise à jour", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                StopTest(); Restore();
                Updater.DownloadAndSchedule(r);
                QuitApp();
            }
            catch (Exception ex) { Cursor = Cursors.Default; Info("La mise à jour a échoué : " + ex.Message); }
        }

        // ================================================================== démarrage auto

        static bool IsStartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --minimized");
                    else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
                }
            }
            catch (Exception ex) { Info("Erreur : " + ex.Message); }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "StretchSwitch_SingleInstance", out created))
            {
                if (!created)
                {
                    // juste après une mise à jour, l'ancienne version peut encore se fermer : on attend un peu
                    bool got;
                    try { got = mutex.WaitOne(4500); } catch (AbandonedMutexException) { got = true; }
                    if (!got)
                    {
                        MessageBox.Show("Stretch Switch est déjà lancé (icône près de l'horloge).", "Stretch Switch");
                        return;
                    }
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool min = args.Any(a => a == "--minimized");
                Application.Run(new MainForm(min));
            }
        }
    }
}
