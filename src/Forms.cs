// Fenêtres secondaires : choix d'un programme ouvert, fiche d'un jeu, jeux installés.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StretchSwitch
{
    static class UiX
    {
        public static Label Hint(string text, int x, int y, int w)
        {
            var l = Ui.Lbl(text, x, y, w); l.Tag = "muted"; l.ForeColor = Theme.Muted; return l;
        }
        public static Label Head(string text, int x, int y, int w)
        {
            var l = Ui.Lbl(text, x, y, w); l.Font = Ui.Bold; return l;
        }
        public static void Dialog(Form f, string title, int w, int h)
        {
            f.AutoScaleDimensions = new SizeF(96f, 96f); f.AutoScaleMode = AutoScaleMode.Dpi;
            f.Text = title; f.ClientSize = new Size(w, h); f.Font = Ui.Normal;
            f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent; f.ShowInTaskbar = false;
        }
    }

    // ------------------------------------------------------------------ Choix d'un programme ouvert

    class ProcessPicker : Form
    {
        ListView box = new ListView();
        bool forClosing;
        public string Selected, SelectedTitle;
        public List<string> SelectedMany = new List<string>();

        static readonly HashSet<string> NotGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer","chrome","msedge","firefox","opera","brave","Discord","Spotify","steamwebhelper","steam","EpicGamesLauncher",
            "RiotClientUx","RiotClientServices","LeagueClientUx","Battle.net","EADesktop","Origin","UbisoftConnect","upc","GalaxyClient",
            "TextInputHost","ApplicationFrameHost","SystemSettings","Taskmgr","Teams","ms-teams","OUTLOOK","WINWORD","EXCEL","Code","claude",
            "Claude","NVIDIA Overlay","nvcontainer","RadeonSoftware","obs64","Medal","SearchHost","StartMenuExperienceHost","ShellExperienceHost",
            "WindowsTerminal","powershell","cmd","notepad","LockApp","StretchSwitch","Overwolf","WhatsApp","Telegram","vlc"
        };

        // forClosing = true : on choisit des applis à fermer (plusieurs possibles)
        public ProcessPicker(bool forClosing)
        {
            this.forClosing = forClosing;
            SuspendLayout();
            UiX.Dialog(this, forClosing ? "Applis à fermer pendant le jeu" : "Choisir un jeu ouvert", 520, 420);

            Controls.Add(Ui.Lbl(forClosing
                ? "Coche les applis à fermer quand le jeu se lance :"
                : "Lance ton jeu (jusqu'à être en jeu / menu), reviens ici et choisis-le :", 12, 12, 496));
            box.SetBounds(12, 38, 496, 326);
            box.View = View.Details; box.FullRowSelect = true; box.MultiSelect = false; box.HideSelection = false;
            box.CheckBoxes = forClosing;
            box.Columns.Add("Programme", 300); box.Columns.Add("Processus", 170);
            Controls.Add(box);

            var refresh = new Button { Text = "Actualiser", Left = 12, Top = 376, Width = 96, Height = 30 };
            var ok = new Button { Text = forClosing ? "Ajouter" : "Choisir", Left = 328, Top = 376, Width = 86, Height = 30, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Annuler", Left = 422, Top = 376, Width = 86, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(refresh); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            refresh.Click += (s, e) => Fill();
            if (!forClosing) box.DoubleClick += (s, e) => { DialogResult = DialogResult.OK; };
            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                if (forClosing)
                {
                    foreach (ListViewItem it in box.CheckedItems) SelectedMany.Add(it.SubItems[1].Text);
                    if (SelectedMany.Count == 0 && box.SelectedItems.Count > 0) SelectedMany.Add(box.SelectedItems[0].SubItems[1].Text);
                    if (SelectedMany.Count == 0) e.Cancel = true;
                    return;
                }
                if (box.SelectedItems.Count == 0) { e.Cancel = true; return; }
                Selected = box.SelectedItems[0].SubItems[1].Text;
                SelectedTitle = box.SelectedItems[0].Text;
            };
            ResumeLayout(false);
            Theme.Apply(this);
            Fill();
        }

        void Fill()
        {
            box.BeginUpdate();
            box.Items.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ListViewItem>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                    if (forClosing ? AppCloser.IsForbidden(p.ProcessName) : NotGames.Contains(p.ProcessName)) continue;
                    if (!seen.Add(p.ProcessName)) continue;
                    string label = p.MainWindowTitle;
                    try
                    {
                        string desc = p.MainModule.FileVersionInfo.FileDescription;
                        if (!string.IsNullOrWhiteSpace(desc) && desc.Length < 60) label = desc;
                    }
                    catch { } // processus protégés (anti-cheat) : on garde le titre de la fenêtre
                    var it = new ListViewItem(label);
                    it.SubItems.Add(p.ProcessName);
                    rows.Add(it);
                }
                catch { }
                finally { p.Dispose(); }
            }
            // pour la fermeture : les applis en arrière-plan courantes même sans fenêtre
            if (forClosing)
                foreach (var name in new[] { "Discord", "Spotify", "chrome", "msedge", "OneDrive", "Teams", "ms-teams", "Overwolf", "Medal" })
                    if (!seen.Contains(name) && Process.GetProcessesByName(name).Length > 0)
                    {
                        seen.Add(name);
                        var it = new ListViewItem(name + " (en arrière-plan)"); it.SubItems.Add(name); rows.Add(it);
                    }
            box.Items.AddRange(rows.OrderBy(r => r.Text, StringComparer.OrdinalIgnoreCase).ToArray());
            box.EndUpdate();
        }
    }

    // ------------------------------------------------------------------ Fiche d'un jeu

    class ProfileForm : Form
    {
        public GameProfile Profile;
        TextBox txtName = new TextBox(), txtProc = new TextBox(), txtLaunch = new TextBox(), txtClose = new TextBox();
        string exePath = "";
        CheckBox chkRes = new CheckBox(), chkStretch = new CheckBox(), chkHdr = new CheckBox();
        CheckBox chkAltTab = new CheckBox(), chkRelaunch = new CheckBox();
        ComboBox cmbRes = new ComboBox(), cmbHz = new ComboBox(), cmbGame = new ComboBox();
        TrackBar trkVib = new TrackBar();
        Label lblVib = new Label();
        NumericUpDown numVib = new NumericUpDown();
        Timer vibPreview = new Timer { Interval = 60 };
        List<int[]> modes;
        bool filling;
        const string OtherGame = "— Autre jeu (je le choisis moi-même) —";

        public ProfileForm(GameProfile p)
        {
            Profile = p;
            modes = Display.Modes(Display.PrimaryDevice());

            SuspendLayout();
            UiX.Dialog(this, string.IsNullOrEmpty(p.Name) ? "Ajouter un jeu" : "Modifier — " + p.Name, 900, 500);

            // ---------- colonne gauche : jeu + écran
            int y = 14;
            Controls.Add(UiX.Head("Choisis ton jeu dans la liste", 14, y, 400)); y += 22;
            cmbGame.SetBounds(14, y, 490, 24); cmbGame.DropDownStyle = ComboBoxStyle.DropDownList; cmbGame.MaxDropDownItems = 20;
            Controls.Add(cmbGame); y += 30;
            Controls.Add(UiX.Hint("✓ = installé sur ce PC. Pas dans la liste ? Lance le jeu puis « Jeu déjà ouvert… ».", 14, y, 490)); y += 28;

            Controls.Add(Ui.Lbl("Nom du jeu", 14, y + 4, 140));
            txtName.SetBounds(160, y, 344, 24); Controls.Add(txtName); y += 32;
            Controls.Add(Ui.Lbl("Processus surveillé", 14, y + 4, 140));
            txtProc.SetBounds(160, y, 344, 24); Controls.Add(txtProc); y += 32;
            var pick = new Button { Text = "Jeu déjà ouvert…", Left = 160, Top = y, Width = 168, Height = 28 };
            var browse = new Button { Text = "Choisir le .exe…", Left = 336, Top = y, Width = 168, Height = 28 };
            Controls.Add(pick); Controls.Add(browse); y += 42;

            chkRes.Text = "Changer la résolution pendant le jeu"; chkRes.SetBounds(14, y, 400, 24); Controls.Add(chkRes); y += 30;
            Controls.Add(Ui.Lbl("Résolution", 34, y + 4, 80));
            cmbRes.SetBounds(116, y, 150, 24); cmbRes.DropDownStyle = ComboBoxStyle.DropDown; Controls.Add(cmbRes);
            Controls.Add(Ui.Lbl("Fréquence", 284, y + 4, 70));
            cmbHz.SetBounds(356, y, 148, 24); cmbHz.DropDownStyle = ComboBoxStyle.DropDownList; Controls.Add(cmbHz); y += 32;
            chkStretch.Text = "Image étirée en plein écran (pas de bandes noires)"; chkStretch.SetBounds(34, y, 450, 24); Controls.Add(chkStretch); y += 36;

            chkHdr.Text = "Désactiver le HDR pendant le jeu"; chkHdr.SetBounds(14, y, 400, 24); Controls.Add(chkHdr); y += 34;

            Controls.Add(Ui.Lbl("Vibrance des couleurs", 14, y + 8, 140));
            trkVib.SetBounds(156, y, 250, 40); trkVib.Minimum = 0; trkVib.Maximum = 400; trkVib.TickFrequency = 50;
            trkVib.SmallChange = 5; trkVib.LargeChange = 25; Controls.Add(trkVib);
            numVib.SetBounds(412, y + 4, 64, 24); numVib.Minimum = 0; numVib.Maximum = 400; numVib.Increment = 5;
            numVib.Font = Ui.Bold; numVib.TextAlign = HorizontalAlignment.Right; Controls.Add(numVib);
            lblVib.SetBounds(480, y + 8, 24, 20); lblVib.Text = "%"; Controls.Add(lblVib); y += 40;
            Controls.Add(UiX.Hint("100 % = normal · 200-400 % = couleurs ultra vives (aperçu en direct)", 14, y, 490));

            // séparateur
            Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Left = 522, Top = 14, Width = 2, Height = 420 });

            // ---------- colonne droite : options
            int x = 540, w = 346; y = 14;
            Controls.Add(UiX.Head("Lancement", x, y, w)); y += 22;
            txtLaunch.SetBounds(x, y, w, 24); Controls.Add(txtLaunch); y += 28;
            var hLaunch = UiX.Hint("Rempli tout seul pour les jeux de la liste. Sinon :\nsteam://rungameid/ID ou le chemin du .exe", x, y, w);
            hLaunch.Height = 36; Controls.Add(hLaunch); y += 40;

            Controls.Add(UiX.Head("Alt+Tab", x, y, w)); y += 22;
            chkAltTab.Text = "Résolution normale quand je quitte le jeu\navec Alt+Tab (le HDR ne bouge pas)";
            chkAltTab.SetBounds(x, y, w, 40); Controls.Add(chkAltTab); y += 42;
            var hAlt = UiX.Hint("Quasi instantané si la mise à l'échelle est faite par\nle GPU (voir LISEZ-MOI). Le HDR, lui, n'est changé\nqu'au lancement et à la fermeture du jeu.", x, y, w);
            hAlt.Height = 52; Controls.Add(hAlt); y += 62;

            Controls.Add(UiX.Head("Fermer des applis pendant le jeu", x, y, w)); y += 22;
            txtClose.SetBounds(x, y, w - 96, 24); Controls.Add(txtClose);
            var addClose = new Button { Text = "Ajouter…", Left = x + w - 88, Top = y - 2, Width = 88, Height = 28 };
            Controls.Add(addClose); y += 30;
            Controls.Add(UiX.Hint("Ex : chrome; Spotify; OneDrive (séparés par ;)", x, y, w)); y += 24;
            chkRelaunch.Text = "Les rouvrir quand le jeu se ferme"; chkRelaunch.SetBounds(x, y, w, 24); Controls.Add(chkRelaunch); y += 34;

            var hEnd = UiX.Hint("Tout est remis comme avant automatiquement\nquand le jeu se ferme.", x, y, w);
            hEnd.Height = 36; Controls.Add(hEnd);

            var ok = new Button { Text = "Enregistrer", Left = 698, Top = 456, Width = 92, Height = 30 };
            var cancel = new Button { Text = "Annuler", Left = 798, Top = 456, Width = 88, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            ResumeLayout(false);

            // ---------- remplissage
            filling = true;
            foreach (var r in modes.Select(m => m[0] + " x " + m[1]).Distinct()) cmbRes.Items.Add(r);
            txtName.Text = p.Name; exePath = p.ExePath ?? ""; txtProc.Text = p.ProcessName;
            txtLaunch.Text = p.LaunchCommand ?? ""; txtClose.Text = p.CloseApps ?? "";
            chkRes.Checked = p.ChangeResolution; chkStretch.Checked = p.Stretch; chkHdr.Checked = p.DisableHdr;
            chkAltTab.Checked = p.AltTabRestore; chkRelaunch.Checked = p.RelaunchApps;
            cmbRes.Text = p.Width + " x " + p.Height;
            FillHz(p.RefreshRate);
            UpdateEnabled();

            var running = Catalog.RunningNames();
            cmbGame.Items.Add(OtherGame);
            cmbGame.SelectedIndex = 0;
            foreach (var g in Catalog.AllChoices())
            {
                bool open = Catalog.SplitNames(g.Procs).Any(running.Contains);
                int idx = cmbGame.Items.Add(new GameItem { G = g, Open = open });
                if (p.ProcessName != "" && string.Equals(p.ProcessName, g.Procs, StringComparison.OrdinalIgnoreCase) && cmbGame.SelectedIndex == 0)
                    cmbGame.SelectedIndex = idx;
            }
            filling = false;
            cmbGame.SelectedIndexChanged += (s, e) =>
            {
                var gi = cmbGame.SelectedItem as GameItem;
                if (gi == null || filling) return;
                txtName.Text = gi.G.Name.Replace(" (en partie)", "");
                txtProc.Text = gi.G.Procs;
                txtLaunch.Text = gi.G.Launch;
                exePath = "";
            };

            trkVib.Value = Math.Max(0, Math.Min(400, p.Vibrance));
            numVib.Value = trkVib.Value;
            trkVib.ValueChanged += (s, e) =>
            {
                if (numVib.Value != trkVib.Value) numVib.Value = trkVib.Value;
                vibPreview.Stop(); vibPreview.Start();
            };
            numVib.ValueChanged += (s, e) => { if (trkVib.Value != (int)numVib.Value) trkVib.Value = (int)numVib.Value; };
            numVib.KeyUp += (s, e) =>
            {
                int v;
                if (int.TryParse(numVib.Text, out v) && v >= 0 && v <= 400 && v != trkVib.Value) numVib.Value = v;
            };
            vibPreview.Tick += (s, e) => { vibPreview.Stop(); Vibrance.Set(trkVib.Value); };
            FormClosed += (s, e) => { vibPreview.Stop(); Vibrance.BackToBase(); };

            chkRes.CheckedChanged += (s, e) => UpdateEnabled();
            cmbRes.TextChanged += (s, e) => FillHz(SelectedHz());
            browse.Click += (s, e) => Browse();
            pick.Click += (s, e) =>
            {
                using (var f = new ProcessPicker(false))
                    if (f.ShowDialog(this) == DialogResult.OK && f.Selected != null)
                    {
                        cmbGame.SelectedIndex = 0;
                        txtProc.Text = f.Selected;
                        txtName.Text = f.SelectedTitle ?? f.Selected;
                    }
            };
            addClose.Click += (s, e) =>
            {
                using (var f = new ProcessPicker(true))
                    if (f.ShowDialog(this) == DialogResult.OK)
                    {
                        var all = Catalog.SplitNames(txtClose.Text).ToList();
                        foreach (var n in f.SelectedMany) if (!all.Contains(n, StringComparer.OrdinalIgnoreCase)) all.Add(n);
                        txtClose.Text = string.Join("; ", all);
                    }
            };
            ok.Click += (s, e) => Save();
            Theme.Apply(this);
        }

        class GameItem
        {
            public GameChoice G; public bool Open;
            public override string ToString()
            {
                return (G.Installed ? "✓  " : "     ") + G.Name +
                       (G.Installed && G.Source != "" ? "   (" + G.Source + ")" : "") +
                       (Open ? "     ▶ ouvert en ce moment" : "");
            }
        }

        void UpdateEnabled() { cmbRes.Enabled = cmbHz.Enabled = chkStretch.Enabled = chkRes.Checked; }

        bool ParseRes(out int w, out int h)
        {
            w = h = 0;
            var m = Regex.Match(cmbRes.Text, @"(\d+)\s*[xX×*]\s*(\d+)");
            if (!m.Success) return false;
            w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value);
            return w >= 320 && h >= 200;
        }

        int SelectedHz()
        {
            if (cmbHz.SelectedItem == null) return 0;
            var m = Regex.Match(cmbHz.SelectedItem.ToString(), @"^(\d+) Hz");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        void FillHz(int want)
        {
            cmbHz.Items.Clear();
            cmbHz.Items.Add("Garder l'actuelle");
            int w, h;
            if (ParseRes(out w, out h))
                foreach (var hz in modes.Where(m => m[0] == w && m[1] == h).Select(m => m[2]).Distinct())
                    cmbHz.Items.Add(hz + " Hz");
            cmbHz.SelectedIndex = 0;
            for (int i = 1; i < cmbHz.Items.Count; i++)
                if (cmbHz.Items[i].ToString() == want + " Hz") cmbHz.SelectedIndex = i;
        }

        void Browse()
        {
            using (var d = new OpenFileDialog { Filter = "Jeu (*.exe)|*.exe", Title = "Choisis le .exe du jeu" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                cmbGame.SelectedIndex = 0;
                exePath = d.FileName;
                txtProc.Text = Path.GetFileNameWithoutExtension(d.FileName);
                if (txtLaunch.Text.Trim() == "") txtLaunch.Text = "\"" + d.FileName + "\"";
                string desc = null;
                try { desc = FileVersionInfo.GetVersionInfo(d.FileName).FileDescription; } catch { }
                txtName.Text = string.IsNullOrWhiteSpace(desc) ? Path.GetFileNameWithoutExtension(d.FileName) : desc.Trim();
            }
        }

        void Save()
        {
            string proc = string.Join(";", Catalog.SplitNames(txtProc.Text));
            if (proc == "") { MessageBox.Show(this, "Choisis ton jeu dans la liste, ou clique « Jeu déjà ouvert… ».", Text); return; }
            if (txtName.Text.Trim() == "") { MessageBox.Show(this, "Donne un nom au jeu.", Text); return; }
            int w, h, typed;
            int vib = int.TryParse(numVib.Text, out typed) ? Math.Max(0, Math.Min(400, typed)) : trkVib.Value;
            if (chkRes.Checked && !ParseRes(out w, out h)) { MessageBox.Show(this, "Résolution invalide (ex : 1440 x 1080).", Text); return; }
            if (!chkRes.Checked && !chkHdr.Checked && vib == 100 && txtClose.Text.Trim() == "")
            { MessageBox.Show(this, "Choisis au moins une option (résolution, HDR, vibrance ou applis à fermer).", Text); return; }

            Profile.Name = txtName.Text.Trim();
            Profile.ExePath = exePath;
            Profile.ProcessName = proc;
            Profile.ChangeResolution = chkRes.Checked;
            if (ParseRes(out w, out h)) { Profile.Width = w; Profile.Height = h; }
            Profile.RefreshRate = SelectedHz();
            Profile.Stretch = chkStretch.Checked;
            Profile.DisableHdr = chkHdr.Checked;
            Profile.Vibrance = vib;
            Profile.LaunchCommand = txtLaunch.Text.Trim();
            Profile.CloseApps = string.Join("; ", Catalog.SplitNames(txtClose.Text));
            Profile.RelaunchApps = chkRelaunch.Checked;
            Profile.AltTabRestore = chkAltTab.Checked;
            DialogResult = DialogResult.OK;
        }
    }

    // ------------------------------------------------------------------ Jeux installés (ajout en masse)

    class InstalledGamesForm : Form
    {
        ListView box = new ListView();
        public List<GameChoice> Chosen = new List<GameChoice>();

        public InstalledGamesForm(IEnumerable<GameProfile> existing)
        {
            SuspendLayout();
            UiX.Dialog(this, "Mes jeux installés", 560, 460);
            Controls.Add(Ui.Lbl("Jeux trouvés sur ce PC (Steam, Epic). Coche ceux à ajouter :", 12, 12, 536));
            box.SetBounds(12, 38, 536, 334);
            box.View = View.Details; box.FullRowSelect = true; box.CheckBoxes = true; box.HideSelection = false;
            box.Columns.Add("Jeu", 250); box.Columns.Add("Source", 70); box.Columns.Add("Processus", 190);
            Controls.Add(box);
            Controls.Add(UiX.Hint("Réglages par défaut : 1440 x 1080 étiré, HDR coupé. Modifie chaque jeu ensuite si besoin.", 12, 380, 536));

            var rescan = new Button { Text = "Rechercher à nouveau", Left = 12, Top = 416, Width = 160, Height = 30 };
            var ok = new Button { Text = "Ajouter", Left = 368, Top = 416, Width = 86, Height = 30, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Annuler", Left = 462, Top = 416, Width = 86, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(rescan); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            ResumeLayout(false);

            var have = new HashSet<string>(existing.SelectMany(p => Catalog.SplitNames(p.ProcessName)), StringComparer.OrdinalIgnoreCase);
            Action fill = () =>
            {
                box.BeginUpdate(); box.Items.Clear();
                foreach (var g in Catalog.AllChoices().Where(g => g.Installed && g.Procs != ""))
                {
                    if (Catalog.SplitNames(g.Procs).Any(have.Contains)) continue;
                    var it = new ListViewItem(g.Name.Replace(" (en partie)", ""));
                    it.SubItems.Add(g.Source); it.SubItems.Add(g.Procs.Replace(";", ", "));
                    it.Tag = g;
                    box.Items.Add(it);
                }
                if (box.Items.Count == 0) box.Items.Add(new ListViewItem("(aucun nouveau jeu trouvé)"));
                box.EndUpdate();
            };
            rescan.Click += (s, e) => { Cursor = Cursors.WaitCursor; GameScanner.Invalidate(); fill(); Cursor = Cursors.Default; };
            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                foreach (ListViewItem it in box.CheckedItems) if (it.Tag is GameChoice) Chosen.Add((GameChoice)it.Tag);
            };
            Theme.Apply(this);
            fill();
        }
    }
}
