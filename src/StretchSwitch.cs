// Stretch Switch - change la résolution / coupe le HDR quand un jeu se lance, et remet tout comme avant quand il se ferme.
// Compilé avec le csc.exe intégré à Windows (.NET Framework 4.x, C# 5) => un seul petit .exe qui marche sur tout Windows 10/11.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace StretchSwitch
{
    // ------------------------------------------------------------------ Config

    public class GameProfile
    {
        public string Name = "";
        public string ExePath = "";
        public string ProcessName = "";
        public bool Enabled = true;
        public bool ChangeResolution = true;
        public int Width = 1440;
        public int Height = 1080;
        public int RefreshRate = 0; // 0 = garder la fréquence actuelle
        public bool Stretch = true;
        public bool DisableHdr = true;
        public int Vibrance = 100; // saturation en % (100 = normal)
        public bool VibranceAllScreens = false; // false = seulement l'écran du jeu (NVIDIA)
        public string LaunchCommand = "";   // steam://..., com.epicgames..., chemin .exe
        public string CloseApps = "";       // processus à fermer au lancement (séparés par ;)
        public bool RelaunchApps = true;    // les relancer quand le jeu se ferme
        public bool AltTabRestore = false;  // résolution normale quand le jeu n'est pas au premier plan

        public string ResolutionText()
        {
            if (!ChangeResolution) return "inchangée";
            return Width + " x " + Height + (RefreshRate > 0 ? " @ " + RefreshRate + " Hz" : "");
        }
    }

    public class AppConfig
    {
        public List<GameProfile> Profiles = new List<GameProfile>();
        public bool StartMinimized = false;
        public bool Notifications = true;
        public bool DarkTheme = Theme.WindowsIsDark();
        public bool CheckUpdates = true;
        public bool HotkeysEnabled = true;
    }

    public class SavedState
    {
        public string Device = "";
        public string ProfileName = "";
        public int Width, Height, Frequency, Bpp;
        public uint FixedOutput;
        public bool ResolutionChanged;
        public bool HdrChanged;
    }

    public static class Store
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StretchSwitch"); }
        }
        static string ConfigPath { get { return Path.Combine(Dir, "config.xml"); } }
        static string StatePath { get { return Path.Combine(Dir, "etat-a-restaurer.xml"); } }

        static T Load<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var f = File.OpenRead(path)) return (T)new XmlSerializer(typeof(T)).Deserialize(f);
            }
            catch { return null; }
        }

        static void Save<T>(string path, T obj)
        {
            Directory.CreateDirectory(Dir);
            string tmp = path + ".tmp";
            using (var f = File.Create(tmp)) new XmlSerializer(typeof(T)).Serialize(f, obj);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static AppConfig LoadConfig() { return Load<AppConfig>(ConfigPath) ?? new AppConfig(); }
        public static void SaveConfig(AppConfig c) { try { Save(ConfigPath, c); } catch { } }
        public static SavedState LoadState() { return Load<SavedState>(StatePath); }
        public static void SaveState(SavedState s) { try { Save(StatePath, s); } catch { } }
        public static void DeleteState() { try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { } }

        // niveau de vibrance NVIDIA d'origine (pour le remettre même après un plantage)
        static string DvcPath { get { return Path.Combine(Dir, "vibrance-a-restaurer.txt"); } }
        public static void SaveDvc(string dev, int level)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(DvcPath, dev + "|" + level); } catch { }
        }
        public static bool LoadDvc(out string dev, out int level)
        {
            dev = null; level = 0;
            try
            {
                if (!File.Exists(DvcPath)) return false;
                var parts = File.ReadAllText(DvcPath).Trim().Split('|');
                if (parts.Length != 2 || !int.TryParse(parts[1], out level)) return false;
                dev = parts[0];
                return true;
            }
            catch { return false; }
        }
        public static void DeleteDvc() { try { if (File.Exists(DvcPath)) File.Delete(DvcPath); } catch { } }

        public static AppConfig Import(string path) { return Load<AppConfig>(path); }
        public static void Export(string path, AppConfig c) { Save(path, c); }
    }

    // ------------------------------------------------------------------ Win32

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct PATH_SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_TARGET
    {
        public LUID adapterId; public uint id, modeInfoIdx, outputTechnology, rotation, scaling, refreshNum, refreshDen, scanLineOrdering;
        public int targetAvailable; public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] public struct PATH_INFO { public PATH_SOURCE source; public PATH_TARGET target; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] public struct MODE_INFO { public uint infoType, id; public LUID adapterId; public ulong u0, u1, u2, u3, u4, u5; }
    [StructLayout(LayoutKind.Sequential)] public struct DI_HEADER { public uint type, size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SOURCE_NAME { public DI_HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdiName; }
    [StructLayout(LayoutKind.Sequential)] public struct COLOR_INFO { public DI_HEADER header; public uint value, colorEncoding, bitsPerColorChannel; }
    [StructLayout(LayoutKind.Sequential)] public struct COLOR_INFO2 { public DI_HEADER header; public uint value, colorEncoding, bitsPerColorChannel, activeColorMode; }
    [StructLayout(LayoutKind.Sequential)] public struct SET_VALUE { public DI_HEADER header; public uint value; }

    static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string dev, IntPtr dm, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
        [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths, ref uint numMode, [Out] MODE_INFO[] modes, IntPtr topology);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME p);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref COLOR_INFO p);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref COLOR_INFO2 p);
        [DllImport("user32.dll")] public static extern int DisplayConfigSetDeviceInfo(ref SET_VALUE p);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    }

    // ------------------------------------------------------------------ Résolution

    public static class Display
    {
        const int ENUM_CURRENT_SETTINGS = -1;
        const uint DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000, DM_BITSPERPEL = 0x40000, DM_DISPLAYFIXEDOUTPUT = 0x20000000;
        public const uint DMDFO_STRETCH = 1;

        public static string PrimaryDevice() { return Screen.PrimaryScreen.DeviceName; }

        static DEVMODE NewDevMode()
        {
            var dm = new DEVMODE();
            dm.dmDeviceName = new string('\0', 32);
            dm.dmFormName = new string('\0', 32);
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }

        public static DEVMODE Current(string dev)
        {
            var dm = NewDevMode();
            Native.EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref dm);
            return dm;
        }

        public static List<int[]> Modes(string dev)
        {
            var list = new List<int[]>();
            var seen = new HashSet<string>();
            var dm = NewDevMode();
            for (int i = 0; Native.EnumDisplaySettings(dev, i, ref dm); i++)
            {
                if (dm.dmBitsPerPel < 32) continue;
                string k = dm.dmPelsWidth + "x" + dm.dmPelsHeight + "@" + dm.dmDisplayFrequency;
                if (seen.Add(k)) list.Add(new[] { (int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency });
            }
            return list.OrderByDescending(m => m[0]).ThenByDescending(m => m[1]).ThenByDescending(m => m[2]).ToList();
        }

        static int Change(string dev, DEVMODE dm) { return Native.ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, 0, IntPtr.Zero); }

        // Renvoie null si OK, sinon un message d'erreur
        public static string SetMode(string dev, int w, int h, int hz, bool stretch)
        {
            var dm = Current(dev);
            dm.dmPelsWidth = (uint)w; dm.dmPelsHeight = (uint)h;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
            if (hz > 0) { dm.dmDisplayFrequency = (uint)hz; dm.dmFields |= DM_DISPLAYFREQUENCY; }
            if (stretch) { dm.dmDisplayFixedOutput = DMDFO_STRETCH; dm.dmFields |= DM_DISPLAYFIXEDOUTPUT; }

            int r = Change(dev, dm);
            if (r != 0 && stretch) { dm.dmFields &= ~DM_DISPLAYFIXEDOUTPUT; r = Change(dev, dm); }
            if (r != 0 && hz > 0) { dm.dmFields &= ~DM_DISPLAYFREQUENCY; r = Change(dev, dm); }
            if (r == 0) return null;
            return "La résolution " + w + "x" + h + " a été refusée (code " + r + "). Elle doit exister dans la liste de ton écran / carte graphique.";
        }

        public static void Restore(SavedState s)
        {
            var dm = Current(s.Device);
            dm.dmPelsWidth = (uint)s.Width; dm.dmPelsHeight = (uint)s.Height;
            dm.dmDisplayFrequency = (uint)s.Frequency; dm.dmBitsPerPel = (uint)s.Bpp;
            dm.dmDisplayFixedOutput = s.FixedOutput;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL | DM_DISPLAYFIXEDOUTPUT;
            if (Change(s.Device, dm) == 0) return;
            dm.dmFields &= ~DM_DISPLAYFIXEDOUTPUT;
            if (Change(s.Device, dm) == 0) return;
            // dernier recours : les réglages enregistrés de Windows
            Native.ChangeDisplaySettingsEx(s.Device, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }
    }

    // ------------------------------------------------------------------ HDR

    public static class Hdr
    {
        const uint QDC_ONLY_ACTIVE_PATHS = 2;
        const uint GET_SOURCE_NAME = 1, GET_ADVANCED_COLOR_INFO = 9, SET_ADVANCED_COLOR_STATE = 10, GET_ADVANCED_COLOR_INFO_2 = 15, SET_HDR_STATE = 16;

        public static bool FindTarget(string gdiName, out LUID adapter, out uint targetId)
        {
            adapter = new LUID(); targetId = 0;
            uint np, nm;
            if (Native.GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return false;
            var paths = new PATH_INFO[np];
            var modes = new MODE_INFO[nm];
            if (Native.QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < np; i++)
            {
                var sn = new SOURCE_NAME();
                sn.header.type = GET_SOURCE_NAME;
                sn.header.size = (uint)Marshal.SizeOf(typeof(SOURCE_NAME));
                sn.header.adapterId = paths[i].source.adapterId;
                sn.header.id = paths[i].source.id;
                if (Native.DisplayConfigGetDeviceInfo(ref sn) == 0 && string.Equals(sn.gdiName, gdiName, StringComparison.OrdinalIgnoreCase))
                {
                    adapter = paths[i].target.adapterId;
                    targetId = paths[i].target.id;
                    return true;
                }
            }
            return false;
        }

        // Taille du bureau (source) et taille réellement envoyée à l'écran (signal).
        // Si elles diffèrent, c'est la carte graphique qui étire l'image => changements de résolution rapides.
        public static bool SignalInfo(string gdiName, out int srcW, out int srcH, out int sigW, out int sigH)
        {
            srcW = srcH = sigW = sigH = 0;
            uint np, nm;
            if (Native.GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return false;
            var paths = new PATH_INFO[np];
            var modes = new MODE_INFO[nm];
            if (Native.QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < np; i++)
            {
                var sn = new SOURCE_NAME();
                sn.header.type = GET_SOURCE_NAME;
                sn.header.size = (uint)Marshal.SizeOf(typeof(SOURCE_NAME));
                sn.header.adapterId = paths[i].source.adapterId;
                sn.header.id = paths[i].source.id;
                if (Native.DisplayConfigGetDeviceInfo(ref sn) != 0 || !string.Equals(sn.gdiName, gdiName, StringComparison.OrdinalIgnoreCase)) continue;
                uint si = paths[i].source.modeInfoIdx, ti = paths[i].target.modeInfoIdx;
                if (si >= nm || ti >= nm) return false;
                // mode source : width, height (u0) ; mode cible : activeSize (u3)
                srcW = (int)(modes[si].u0 & 0xFFFFFFFF); srcH = (int)(modes[si].u0 >> 32);
                sigW = (int)(modes[ti].u3 & 0xFFFFFFFF); sigH = (int)(modes[ti].u3 >> 32);
                return srcW > 0 && sigW > 0;
            }
            return false;
        }

        public static bool GetState(LUID a, uint id, out bool supported, out bool enabled)
        {
            supported = false; enabled = false;
            var i2 = new COLOR_INFO2();
            i2.header.type = GET_ADVANCED_COLOR_INFO_2;
            i2.header.size = (uint)Marshal.SizeOf(typeof(COLOR_INFO2));
            i2.header.adapterId = a; i2.header.id = id;
            if (Native.DisplayConfigGetDeviceInfo(ref i2) == 0) // Windows 11 24H2+
            {
                supported = (i2.value & 0x10) != 0;
                enabled = (i2.value & 0x20) != 0;
                return true;
            }
            var i1 = new COLOR_INFO();
            i1.header.type = GET_ADVANCED_COLOR_INFO;
            i1.header.size = (uint)Marshal.SizeOf(typeof(COLOR_INFO));
            i1.header.adapterId = a; i1.header.id = id;
            if (Native.DisplayConfigGetDeviceInfo(ref i1) != 0) return false;
            supported = (i1.value & 1) != 0;
            enabled = (i1.value & 2) != 0 && (i1.value & 4) == 0;
            return true;
        }

        public static bool Set(LUID a, uint id, bool enable)
        {
            var s = new SET_VALUE();
            s.header.type = SET_HDR_STATE;
            s.header.size = (uint)Marshal.SizeOf(typeof(SET_VALUE));
            s.header.adapterId = a; s.header.id = id;
            s.value = enable ? 1u : 0u;
            if (Native.DisplayConfigSetDeviceInfo(ref s) == 0) return true;
            s.header.type = SET_ADVANCED_COLOR_STATE; // anciennes versions de Windows
            return Native.DisplayConfigSetDeviceInfo(ref s) == 0;
        }

        public static string Describe(string dev)
        {
            LUID a; uint t; bool sup, en;
            if (!FindTarget(dev, out a, out t) || !GetState(a, t, out sup, out en)) return "inconnu";
            if (!sup) return "non supporté";
            return en ? "activé" : "désactivé";
        }
    }

    // ------------------------------------------------------------------ Vibrance (saturation)
    // Deux façons :
    //  - pilote NVIDIA (vibrance numérique) : seulement l'écran du jeu, aucun coût, max = 100 % du panneau NVIDIA
    //  - filtre de couleur Windows : tous les écrans, sans limite, marche avec AMD / Intel
    //    (disparaît tout seul si l'app se ferme ou plante)

    [StructLayout(LayoutKind.Sequential)]
    public struct MAGCOLOREFFECT { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)] public float[] transform; }

    public static class Vibrance
    {
        [DllImport("Magnification.dll")] static extern bool MagInitialize();
        [DllImport("Magnification.dll")] static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);

        static bool magReady;
        static int filterLevel = 100;
        static int? driverOriginal;   // niveau NVIDIA d'origine, à remettre
        static string driverDevice;

        public static int Current = 100;      // valeur affichée en ce moment
        public static bool AllScreens;        // mode utilisé en ce moment
        public static int Base = 100;         // valeur à remettre après un aperçu
        public static bool BaseAll;

        // Vibrance sur l'écran du jeu seulement possible ?
        public static bool PerScreen { get { return NvVibrance.Available; } }

        // Valeur max utile : au-delà de 200 % le pilote NVIDIA est déjà à fond
        public static int MaxFor(bool allScreens) { return !allScreens && PerScreen ? 200 : 400; }

        static float[] Matrix(int percent)
        {
            float s = percent / 100f;
            float[] lum = { 0.2126f, 0.7152f, 0.0722f };
            var m = new float[25];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    m[i * 5 + j] = (1 - s) * lum[i] + (i == j ? s : 0);
            m[3 * 5 + 3] = 1; m[4 * 5 + 4] = 1;
            return m;
        }

        static bool SetFilter(int percent)
        {
            if (percent == filterLevel) return true;
            try
            {
                if (!magReady) magReady = MagInitialize();
                if (!magReady) return false;
                var e = new MAGCOLOREFFECT { transform = Matrix(percent) };
                if (!MagSetFullscreenColorEffect(ref e)) return false;
                filterLevel = percent;
                return true;
            }
            catch { return false; }
        }

        static bool SetDriver(int percent)
        {
            if (percent == 100)
            {
                if (driverOriginal == null) return true;
                if (!NvVibrance.Set(driverDevice, driverOriginal.Value)) return false;
                driverOriginal = null; driverDevice = null;
                Store.DeleteDvc();
                return true;
            }
            string dev = driverDevice ?? Display.PrimaryDevice();
            int cur, min, max;
            if (!NvVibrance.Get(dev, out cur, out min, out max)) return false;
            if (driverOriginal == null)
            {
                driverOriginal = cur; driverDevice = dev;
                Store.SaveDvc(dev, cur);
            }
            int orig = driverOriginal.Value;
            int level = percent >= 100
                ? orig + (int)Math.Round((max - orig) * Math.Min(percent - 100, 100) / 100.0)
                : orig - (int)Math.Round((orig - min) * (100 - percent) / 100.0);
            return NvVibrance.Set(dev, Math.Max(min, Math.Min(max, level)));
        }

        public static bool Set(int percent) { return Set(percent, AllScreens); }

        public static bool Set(int percent, bool allScreens)
        {
            bool useDriver = !allScreens && PerScreen;
            bool ok;
            if (useDriver) { SetFilter(100); ok = SetDriver(percent); }
            else { SetDriver(100); ok = SetFilter(percent); }
            if (ok) { Current = percent; AllScreens = allScreens; }
            return ok;
        }

        // Tout remettre comme à l'origine (les deux méthodes)
        public static void ResetAll()
        {
            SetFilter(100);
            SetDriver(100);
            Current = 100; Base = 100;
        }

        public static void BackToBase() { Set(Base, BaseAll); }

        // Après un plantage : remettre le niveau NVIDIA d'origine
        public static void RecoverDriver()
        {
            string dev; int level;
            if (Store.LoadDvc(out dev, out level) && NvVibrance.Available) NvVibrance.Set(dev, level);
            Store.DeleteDvc();
        }
    }
    // ------------------------------------------------------------------ UI helpers

    static class Ui
    {
        public static readonly Font Normal = new Font("Segoe UI", 9f);
        public static readonly Font Bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font Title = new Font("Segoe UI Semibold", 12f);
        public static readonly Color Accent = Color.FromArgb(255, 70, 85);

        public static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(1, 1, 10, 10, 180, 90); path.AddArc(21, 1, 10, 10, 270, 90);
                        path.AddArc(21, 21, 10, 10, 0, 90); path.AddArc(1, 21, 10, 10, 90, 90);
                        path.CloseFigure();
                        using (var b = new SolidBrush(Accent)) g.FillPath(b, path);
                    }
                    using (var p = new Pen(Color.White, 3f))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, 7, 16, 25, 16);
                        g.DrawLine(p, 7, 16, 12, 11); g.DrawLine(p, 7, 16, 12, 21);
                        g.DrawLine(p, 25, 16, 20, 11); g.DrawLine(p, 25, 16, 20, 21);
                    }
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        public static Label Lbl(string text, int x, int y, int w)
        {
            return new Label { Text = text, Left = x, Top = y, Width = w, Height = 20, Font = Normal };
        }
    }

}
