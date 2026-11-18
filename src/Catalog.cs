// Jeux connus + détection des jeux installés (Steam / Epic) + lancement.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace StretchSwitch
{
    public class GameChoice
    {
        public string Name = "", Procs = "", Launch = "";
        public bool Installed, Known;
        public string Source = ""; // "Steam", "Epic", ""
    }

    public static class Catalog
    {
        // Nom affiché, processus (séparés par ;), commande de lancement
        // "riot:xxx" = lancé via le Riot Client
        static readonly string[][] Known = new[]
        {
            new[] { "ROB RUN", "ROB RUN", "steam://rungameid/4262460" },
            new[] { "Valorant", "VALORANT-Win64-Shipping", "riot:valorant" },
            new[] { "Fortnite", "FortniteClient-Win64-Shipping", "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true" },
            new[] { "Counter-Strike 2", "cs2", "steam://rungameid/730" },
            new[] { "League of Legends (en partie)", "League of Legends", "riot:league_of_legends" },
            new[] { "Apex Legends", "r5apex;r5apex_dx12", "steam://rungameid/1172470" },
            new[] { "Rocket League", "RocketLeague", "steam://rungameid/252950" },
            new[] { "Overwatch 2", "Overwatch", "steam://rungameid/2357570" },
            new[] { "Rainbow Six Siege", "RainbowSix;RainbowSix_Vulkan;RainbowSix_DX11;RainbowSix_BE", "steam://rungameid/359550" },
            new[] { "Call of Duty (BO6 / BO7 / Warzone / MW3)", "cod;cod24-cod;cod25-cod", "steam://rungameid/1938090" },
            new[] { "Marvel Rivals", "Marvel-Win64-Shipping", "steam://rungameid/2767030" },
            new[] { "The Finals", "Discovery", "steam://rungameid/2073850" },
            new[] { "Arc Raiders", "PioneerGame", "steam://rungameid/1808500" },
            new[] { "Battlefield 6", "bf6", "steam://rungameid/2807960" },
            new[] { "Battlefield 2042", "BF2042", "steam://rungameid/1517290" },
            new[] { "Rust", "RustClient", "steam://rungameid/252490" },
            new[] { "PUBG", "TslGame", "steam://rungameid/578080" },
            new[] { "GTA V", "GTA5;GTA5_Enhanced", "steam://rungameid/271590" },
            new[] { "Escape from Tarkov", "EscapeFromTarkov", "" },
            new[] { "Delta Force", "DeltaForceClient-Win64-Shipping", "steam://rungameid/2507950" },
            new[] { "Hunt: Showdown 1896", "HuntGame", "steam://rungameid/594650" },
            new[] { "Deadlock", "project8", "steam://rungameid/1422450" },
            new[] { "Dota 2", "dota2", "steam://rungameid/570" },
            new[] { "Destiny 2", "destiny2", "steam://rungameid/1085660" },
            new[] { "Halo Infinite", "HaloInfinite", "steam://rungameid/1240440" },
            new[] { "Minecraft (Bedrock)", "Minecraft.Windows", "" },
            new[] { "Minecraft (Java)", "javaw", "" },
            new[] { "Roblox", "RobloxPlayerBeta", "" },
            new[] { "Brawlhalla", "Brawlhalla", "steam://rungameid/291550" },
            new[] { "osu!", "osu!", "" },
        };

        public static string[] SplitNames(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            return s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x.Substring(0, x.Length - 4) : x)
                    .Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static HashSet<string> RunningNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) { try { set.Add(p.ProcessName); } catch { } finally { p.Dispose(); } }
            return set;
        }

        static string SteamId(string launch)
        {
            var m = Regex.Match(launch ?? "", @"^steam://rungameid/(\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        static bool SameProcs(string a, string b)
        {
            var x = SplitNames(a);
            return SplitNames(b).Any(n => x.Contains(n, StringComparer.OrdinalIgnoreCase));
        }

        // Jeux connus + jeux installés, fusionnés. Installés en premier.
        public static List<GameChoice> AllChoices()
        {
            var installed = GameScanner.Get();
            var result = new List<GameChoice>();
            var usedInstalled = new HashSet<InstalledGame>();

            foreach (var k in Known)
            {
                var c = new GameChoice { Name = k[0], Procs = k[1], Launch = k[2], Known = true };
                string sid = SteamId(k[2]);
                var match = installed.FirstOrDefault(g =>
                    (sid != null && g.SteamId == sid) ||
                    (k[2].Contains("%3A" + g.EpicAppName + "?") && g.EpicAppName != "") ||
                    SameProcs(g.Procs, k[1]));
                if (match != null)
                {
                    usedInstalled.Add(match);
                    c.Installed = true; c.Source = match.Source;
                    if (match.Source == "Epic" || c.Launch == "") c.Launch = match.Launch;
                }
                result.Add(c);
            }
            foreach (var g in installed.Where(g => !usedInstalled.Contains(g) && g.Procs != ""))
                result.Add(new GameChoice { Name = g.Name, Procs = g.Procs, Launch = g.Launch, Installed = true, Source = g.Source });

            return result.OrderByDescending(c => c.Installed)
                         .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    // ------------------------------------------------------------------ Scan Steam / Epic

    public class InstalledGame
    {
        public string Name = "", Procs = "", Launch = "", Source = "", SteamId = "", EpicAppName = "";
    }

    public static class GameScanner
    {
        static List<InstalledGame> cache;
        static readonly object gate = new object();

        public static void Prewarm() { ThreadPool.QueueUserWorkItem(_ => Get()); }

        public static void Invalidate() { lock (gate) cache = null; }

        public static List<InstalledGame> Get()
        {
            lock (gate)
            {
                if (cache == null)
                {
                    var list = new List<InstalledGame>();
                    try { list.AddRange(ScanSteam()); } catch { }
                    try { list.AddRange(ScanEpic()); } catch { }
                    cache = list;
                }
                return cache;
            }
        }

        static readonly Regex Junk = new Regex(@"Redistributable|Runtime|Proton|SDK|Dedicated Server|Soundtrack|Wallpaper Engine|SteamVR|Benchmark|Editor|Unreal Engine",
                                               RegexOptions.IgnoreCase);

        static IEnumerable<InstalledGame> ScanSteam()
        {
            var res = new List<InstalledGame>();
            string steam = null;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (k != null) steam = k.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steam)) return res;
            steam = steam.Replace('/', '\\');

            var libs = new List<string> { Path.Combine(steam, "steamapps") };
            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));

            var seen = new HashSet<string>();
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(lib)) continue;
                foreach (var acf in Directory.GetFiles(lib, "appmanifest_*.acf"))
                {
                    try
                    {
                        string t = File.ReadAllText(acf);
                        string id = Field(t, "appid"), name = Field(t, "name"), dir = Field(t, "installdir");
                        if (id == null || name == null || dir == null || !seen.Add(id)) continue;
                        if (id == "228980" || Junk.IsMatch(name)) continue;
                        string full = Path.Combine(lib, "common", dir);
                        res.Add(new InstalledGame
                        {
                            Name = name, Source = "Steam", SteamId = id,
                            Launch = "steam://rungameid/" + id,
                            Procs = GuessProcs(full, null)
                        });
                    }
                    catch { }
                }
            }
            return res;
        }

        static string Field(string text, string key)
        {
            var m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        static string Json(string text, string key)
        {
            var m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value.Replace(@"\\", @"\").Replace(@"\/", "/") : "";
        }

        static IEnumerable<InstalledGame> ScanEpic()
        {
            var res = new List<InstalledGame>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) return res;
            foreach (var item in Directory.GetFiles(dir, "*.item"))
            {
                try
                {
                    string t = File.ReadAllText(item);
                    string name = Json(t, "DisplayName"), loc = Json(t, "InstallLocation"), exe = Json(t, "LaunchExecutable");
                    string app = Json(t, "AppName"), ns = Json(t, "CatalogNamespace"), cat = Json(t, "CatalogItemId");
                    if (name == "" || app == "" || Junk.IsMatch(name) || app.StartsWith("UE_")) continue;
                    res.Add(new InstalledGame
                    {
                        Name = name, Source = "Epic", EpicAppName = app,
                        Launch = "com.epicgames.launcher://apps/" + ns + "%3A" + cat + "%3A" + app + "?action=launch&silent=true",
                        Procs = GuessProcs(loc, exe == "" ? null : Path.GetFileNameWithoutExtension(exe))
                    });
                }
                catch { }
            }
            return res;
        }

        static readonly Regex NotGameExe = new Regex(
            @"crash|setup|redist|unins|easyanticheat|eac_|battleye|beservice|^be_|vc_?redist|dxsetup|helper|report|update|install|start_protected_game|dotnet|launcher|cefprocess|webhelper|prereq|uploader|diagnostic|benchmark|editor|server|bssndrpt|bugsplat|plugin|launchpad|config|settings|tool",
            RegexOptions.IgnoreCase);

        // Devine le(s) .exe du jeu dans son dossier d'installation
        static string GuessProcs(string dir, string hint)
        {
            var names = new List<string>();
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                // Unreal Engine : <Jeu>-Win64-Shipping.exe dans Binaries\Win64
                foreach (var f in FindExes(dir, 5).Where(f => Path.GetFileName(f).IndexOf("-Shipping", StringComparison.OrdinalIgnoreCase) >= 0))
                    if (!NotGameExe.IsMatch(Path.GetFileName(f))) names.Add(Path.GetFileNameWithoutExtension(f));
                if (names.Count == 0)
                {
                    foreach (var f in SafeFiles(dir))
                        if (!NotGameExe.IsMatch(Path.GetFileName(f))) names.Add(Path.GetFileNameWithoutExtension(f));
                }
                if (names.Count == 0)
                {
                    foreach (var f in FindExes(dir, 3))
                        if (!NotGameExe.IsMatch(Path.GetFileName(f))) names.Add(Path.GetFileNameWithoutExtension(f));
                }
            }
            if (names.Count == 0 && !string.IsNullOrEmpty(hint) && !NotGameExe.IsMatch(hint)) names.Add(hint);
            return string.Join(";", names.Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
        }

        static string[] SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir, "*.exe"); } catch { return new string[0]; }
        }

        static List<string> FindExes(string dir, int depth)
        {
            var res = new List<string>();
            res.AddRange(SafeFiles(dir));
            if (depth <= 0 || res.Count > 200) return res;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return res; }
            foreach (var s in subs)
            {
                string n = Path.GetFileName(s);
                if (n.StartsWith(".") || Regex.IsMatch(n, @"^(Engine|EasyAntiCheat|BattlEye|_CommonRedist|Redist|redist|DirectX|Support)$", RegexOptions.IgnoreCase)) continue;
                res.AddRange(FindExes(s, depth - 1));
                if (res.Count > 400) break;
            }
            return res;
        }
    }

    // ------------------------------------------------------------------ Lancement

    public static class Launcher
    {
        public static string Start(string cmd)
        {
            try
            {
                cmd = (cmd ?? "").Trim();
                if (cmd == "") return "Pas de commande de lancement pour ce jeu (Modifier > Lancement).";

                if (cmd.StartsWith("riot:", StringComparison.OrdinalIgnoreCase))
                {
                    string client = RiotClient();
                    if (client == null) return "Riot Client introuvable.";
                    Process.Start(new ProcessStartInfo(client, "--launch-product=" + cmd.Substring(5) + " --launch-patchline=live") { UseShellExecute = true });
                    return null;
                }
                if (cmd.Contains("://"))
                {
                    Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true });
                    return null;
                }
                string file = cmd, args = "";
                var q = Regex.Match(cmd, "^\"([^\"]+)\"\\s*(.*)$");
                if (q.Success) { file = q.Groups[1].Value; args = q.Groups[2].Value; }
                else
                {
                    int i = cmd.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
                    if (i > 0) { file = cmd.Substring(0, i + 4); args = cmd.Substring(i + 5); }
                }
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = true };
                try { psi.WorkingDirectory = Path.GetDirectoryName(file); } catch { }
                Process.Start(psi);
                return null;
            }
            catch (Exception ex) { return "Impossible de lancer le jeu : " + ex.Message; }
        }

        static string RiotClient()
        {
            try
            {
                string json = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\RiotClientInstalls.json");
                if (File.Exists(json))
                {
                    string t = File.ReadAllText(json);
                    foreach (var key in new[] { "rc_default", "rc_live" })
                    {
                        var m = Regex.Match(t, "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"");
                        if (m.Success)
                        {
                            string p = m.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\');
                            if (File.Exists(p)) return p;
                        }
                    }
                }
            }
            catch { }
            string def = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
            return File.Exists(def) ? def : null;
        }
    }

    // ------------------------------------------------------------------ Fermer / relancer des applis

    public static class AppCloser
    {
        // Jamais fermés : système, lanceurs de jeux (fermer Steam ferme le jeu), l'app elle-même
        static readonly HashSet<string> Forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer","dwm","csrss","winlogon","wininit","services","lsass","smss","svchost","System","Idle","sihost","ctfmon",
            "fontdrvhost","audiodg","StretchSwitch","steam","steamwebhelper","steamservice","EpicGamesLauncher","EpicWebHelper",
            "RiotClientServices","RiotClientUx","RiotClientCrashHandler","vgc","vgtray","Battle.net","Agent","EADesktop","EABackgroundService",
            "upc","UbisoftConnect","GalaxyClient","EasyAntiCheat","EasyAntiCheat_EOS","BEService","start_protected_game",
            "SearchHost","StartMenuExperienceHost","ShellExperienceHost","TextInputHost","RuntimeBroker","SecurityHealthSystray"
        };

        public static bool IsForbidden(string name) { return Forbidden.Contains(name); }

        // Ferme les applis, renvoie les chemins des .exe pour les relancer plus tard
        public static List<string> CloseAll(string names, string gameProcs)
        {
            var paths = new List<string>();
            var game = new HashSet<string>(Catalog.SplitNames(gameProcs), StringComparer.OrdinalIgnoreCase);
            foreach (var name in Catalog.SplitNames(names))
            {
                if (Forbidden.Contains(name) || game.Contains(name)) continue;
                var procs = Process.GetProcessesByName(name);
                string path = null;
                foreach (var p in procs)
                {
                    try { if (path == null) path = p.MainModule.FileName; } catch { }
                    try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
                }
                foreach (var p in procs)
                {
                    try { if (!p.WaitForExit(2500)) p.Kill(); } catch { }
                    finally { p.Dispose(); }
                }
                if (path != null && !paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
            }
            return paths;
        }

        public static void Relaunch(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p)).Length > 0) continue;
                    Process.Start(new ProcessStartInfo(p) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(p) });
                }
                catch { }
            }
        }
    }
}
