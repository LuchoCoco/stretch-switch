// Thème clair / sombre, affichage à l'écran (OSD) et mise à jour automatique.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StretchSwitch
{
    public static class Theme
    {
        public static bool Dark;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static Color Back { get { return Dark ? Color.FromArgb(28, 28, 30) : SystemColors.Control; } }
        public static Color Field { get { return Dark ? Color.FromArgb(44, 44, 48) : SystemColors.Window; } }
        public static Color Text { get { return Dark ? Color.FromArgb(236, 236, 236) : SystemColors.ControlText; } }
        public static Color Muted { get { return Dark ? Color.FromArgb(150, 150, 156) : Color.Gray; } }
        public static Color ButtonBack { get { return Dark ? Color.FromArgb(54, 54, 60) : SystemColors.Control; } }
        public static Color Border { get { return Dark ? Color.FromArgb(78, 78, 86) : SystemColors.ControlDark; } }
        public static Color Header { get { return Dark ? Color.FromArgb(38, 38, 42) : Color.FromArgb(240, 240, 240); } }

        public static bool WindowsIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static void Apply(Form f)
        {
            f.BackColor = Back; f.ForeColor = Text;
            if (f.IsHandleCreated) TitleBar(f);
            else f.HandleCreated += (s, e) => TitleBar(f);
            ApplyChildren(f);
            f.Invalidate(true);
        }

        static void TitleBar(Form f)
        {
            try { int v = Dark ? 1 : 0; DwmSetWindowAttribute(f.Handle, 20, ref v, 4); } catch { }
        }

        static void ApplyChildren(Control parent)
        {
            foreach (Control c in parent.Controls) { ApplyOne(c); ApplyChildren(c); }
        }

        static void ApplyOne(Control c)
        {
            if (c is Button)
            {
                var b = (Button)c;
                if (Dark)
                {
                    b.FlatStyle = FlatStyle.Flat; b.BackColor = ButtonBack; b.ForeColor = Text;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 78);
                }
                else { b.FlatStyle = FlatStyle.Standard; b.BackColor = SystemColors.Control; b.ForeColor = SystemColors.ControlText; b.UseVisualStyleBackColor = true; }
            }
            else if (c is TextBox || c is NumericUpDown)
            {
                c.BackColor = Field; c.ForeColor = Text;
                if (c is TextBox) ((TextBox)c).BorderStyle = Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
            }
            else if (c is ComboBox)
            {
                var cb = (ComboBox)c;
                cb.BackColor = Field; cb.ForeColor = Text; cb.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
            }
            else if (c is ListView)
            {
                var lv = (ListView)c;
                lv.BackColor = Field; lv.ForeColor = Text;
                lv.BorderStyle = Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                if (lv.IsHandleCreated) DarkScroll(lv); else lv.HandleCreated += (s, e) => DarkScroll(lv);
                if (!(lv.Tag is string && (string)lv.Tag == "themed"))
                {
                    lv.Tag = "themed";
                    lv.OwnerDraw = true;
                    lv.DrawColumnHeader += (s, e) =>
                    {
                        using (var bg = new SolidBrush(Header)) e.Graphics.FillRectangle(bg, e.Bounds);
                        using (var pen = new Pen(Border)) e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                        var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                        TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, r, Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    };
                    lv.DrawItem += (s, e) => { e.DrawDefault = true; };
                    lv.DrawSubItem += (s, e) => { e.DrawDefault = true; };
                }
            }
            else if (c is TrackBar) c.BackColor = Back;
            else if (c is Label)
            {
                var tag = c.Tag as string;
                if (tag == "muted") c.ForeColor = Muted;
                else if (tag != "accent") c.ForeColor = Text;
            }
            else if (c is CheckBox) c.ForeColor = Text;
        }

        static void DarkScroll(Control c)
        {
            try { SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        public static void Apply(ToolStrip menu)
        {
            menu.Renderer = Dark ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkMenuColors()) : new ToolStripProfessionalRenderer();
            menu.BackColor = Dark ? Color.FromArgb(40, 40, 44) : SystemColors.Menu;
            menu.ForeColor = Text;
            foreach (ToolStripItem i in menu.Items) ApplyItem(i);
        }

        static void ApplyItem(ToolStripItem i)
        {
            i.ForeColor = Text;
            var mi = i as ToolStripMenuItem;
            if (mi != null)
            {
                mi.DropDown.BackColor = Dark ? Color.FromArgb(40, 40, 44) : SystemColors.Menu;
                mi.DropDown.Renderer = Dark ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkMenuColors()) : new ToolStripProfessionalRenderer();
                foreach (ToolStripItem sub in mi.DropDownItems) ApplyItem(sub);
            }
        }

        class DarkMenuColors : ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(40, 40, 44), Sel = Color.FromArgb(64, 64, 72), Bd = Color.FromArgb(70, 70, 78);
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuBorder { get { return Bd; } }
            public override Color MenuItemBorder { get { return Sel; } }
            public override Color MenuItemSelected { get { return Sel; } }
            public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
            public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
            public override Color SeparatorDark { get { return Bd; } }
            public override Color SeparatorLight { get { return Bg; } }
        }
    }

    // ------------------------------------------------------------------ OSD (petit message par-dessus le jeu)

    class Osd : Form
    {
        Label lbl = new Label();
        Timer hide = new Timer { Interval = 1300 };

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(18, 18, 20); Opacity = 0.92;
            Size = new Size(340, 64);
            lbl.Dock = DockStyle.Fill; lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.ForeColor = Color.White; lbl.Font = new Font("Segoe UI Semibold", 15f);
            Controls.Add(lbl);
            hide.Tick += (s, e) => { hide.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x8; // NOACTIVATE | TOOLWINDOW | TRANSPARENT | TOPMOST
                return cp;
            }
        }

        public void ShowText(string text)
        {
            lbl.Text = text;
            float scale = DeviceDpi / 96f;
            int w = (int)(340 * scale), h = (int)(64 * scale);
            var b = Screen.PrimaryScreen.Bounds;
            Bounds = new Rectangle(b.Left + (b.Width - w) / 2, b.Top + (int)(60 * scale), w, h);
            if (!Visible) Show();
            hide.Stop(); hide.Start();
        }
    }

    // ------------------------------------------------------------------ Mise à jour (GitHub Releases)

    public static class Updater
    {
        public const string Version = "1.3.0";

        // Dépôt GitHub qui héberge les versions, ex : "pseudo/stretch-switch".
        // Vide = mises à jour désactivées.
        public const string GitHubRepo = "LuchoCoco/stretch-switch";

        public static bool Configured { get { return GitHubRepo != ""; } }

        public class Release { public string Tag = "", Url = "", Notes = ""; }

        // Renvoie la nouvelle version si elle est plus récente, sinon null. Lève une exception en cas d'erreur réseau.
        public static Release Check()
        {
            if (!Configured) return null;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("User-Agent", "StretchSwitch-Updater");
                string json = wc.DownloadString("https://api.github.com/repos/" + GitHubRepo + "/releases/latest");
                var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                var url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\"");
                var body = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (!tag.Success || !url.Success) return null;
                if (!IsNewer(tag.Groups[1].Value, Version)) return null;
                return new Release
                {
                    Tag = tag.Groups[1].Value, Url = url.Groups[1].Value,
                    Notes = body.Success ? Regex.Unescape(body.Groups[1].Value) : ""
                };
            }
        }

        static bool IsNewer(string tag, string current)
        {
            Version a, b;
            if (!System.Version.TryParse(tag.TrimStart('v', 'V').Trim(), out a)) return false;
            if (!System.Version.TryParse(current, out b)) return false;
            return a > b;
        }

        // Télécharge la nouvelle version et prépare le remplacement de l'exe après la fermeture de l'app
        public static void DownloadAndSchedule(Release r)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string tmp = Path.Combine(Path.GetTempPath(), "StretchSwitch_nouveau.exe");
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "StretchSwitch-Updater");
                wc.DownloadFile(r.Url, tmp);
            }
            var bytes = File.ReadAllBytes(tmp);
            if (bytes.Length < 10000 || bytes[0] != 'M' || bytes[1] != 'Z') throw new Exception("le fichier téléchargé n'est pas valide");

            string exe = Application.ExecutablePath;
            int pid = Process.GetCurrentProcess().Id;
            string script = Path.Combine(Path.GetTempPath(), "StretchSwitch_maj.cmd");
            string body =
                "@echo off\r\nchcp 65001 >nul\r\n" +
                ":wait\r\n" +
                "tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                "move /y \"" + tmp + "\" \"" + exe + "\" >nul\r\n" +
                "start \"\" \"" + exe + "\"\r\n" +
                "del \"%~f0\"\r\n";
            File.WriteAllText(script, body, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
