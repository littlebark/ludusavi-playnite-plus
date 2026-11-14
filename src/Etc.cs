using Playnite.SDK.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;

namespace LudusaviPlaynite
{
    /// <summary>
    /// Miscellaneous utilities.
    /// </summary>
    public static class Etc
    {
        public static Version RECOMMENDED_APP_VERSION = new Version(0, 24, 0);
        private static Regex HOME_DIR = new Regex("^~");

        public static V GetDictValue<K, V>(Dictionary<K, V> dict, K key, V fallback)
        {
            if (dict == null || key == null)
            {
                return fallback;
            }

            V result;
            var found = dict.TryGetValue(key, out result);
            if (found)
            {
                return result;
            }
            else
            {
                return fallback;
            }
        }

        public static (int, string) RunCommand(string command, string args)
        {
            var p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = command;
            p.StartInfo.Arguments = args;
            p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            p.Start();

            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            return (p.ExitCode, stdout);
        }

        public static (int, string) RunCommand(string command, string args, string stdin)
        {
            var p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = command;
            p.StartInfo.Arguments = args;
            p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            p.Start();

            // Ensure we write UTF-8 data without any BOM.
            StreamWriter writer = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
            writer.WriteLine(stdin);
            writer.Close();

            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            return (p.ExitCode, stdout);
        }

        public static void RunCommandGui(string command, string args)
        {
            var p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.FileName = command;
            p.StartInfo.Arguments = args;
            p.Start();
        }

        public static bool IsOnSteam(Game game)
        {
            return game.Source?.Name == "Steam"
                || game.PluginId == Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        }

        public static int? SteamId(Game game)
        {
            // First, check if the game is from Steam directly.
            if (IsOnSteam(game) && int.TryParse(game.GameId, out var id))
            {
                return id;
            }

            if (string.IsNullOrEmpty(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
            {
                return null;
            }

            return DetectSteamAppId(game.InstallDirectory);
        }

        // Mirrors `detectSteamGame()` from `detect-steam-game.mjs`:
        // 1. steam_appid.txt (shallow first, then anywhere in the tree, shallowest wins).
        // 2. steam_emu.ini / rune.ini (RUNE-style emulators), preferring the one next to steam_api*.dll.
        // 3. Other emulator configs (SteamConfig.ini, SmartSteamEmu.ini, flt.ini, steam_appid.txt).
        // 4. Trailing number in the path.
        private static int? DetectSteamAppId(string gameDir)
        {
            return DetectGameAppId(gameDir)
                ?? ReadAppIdFromEmuIni(gameDir)
                ?? AppIdFromConfig(gameDir)
                ?? AppIdFromPath(gameDir);
        }

        private static readonly string[] FindSkipDirs =
            { "node_modules", ".git", ".svn", ".hg", "__MACOSX" };

        private const int FindMaxFiles = 200000;

        private static bool IsSkippedDir(string name)
        {
            foreach (var skipped in FindSkipDirs)
            {
                if (string.Equals(skipped, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Full-tree search by file name, with no depth limit.
        /// Skips irrelevant directories, stops after FindMaxFiles entries,
        /// and does not recurse into symlinked directories.
        /// </summary>
        private static List<string> FindFiles(string root, params string[] fileNames)
        {
            var results = new List<string>();
            var state = new int[] { 0 };
            WalkFiles(root, fileNames, results, state);
            return results;
        }

        private static void WalkFiles(string dir, string[] fileNames, List<string> results, int[] state)
        {
            if (state[0] > FindMaxFiles)
            {
                return;
            }

            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch { return; }

            foreach (var entry in entries)
            {
                state[0]++;
                if (state[0] > FindMaxFiles)
                {
                    return;
                }

                var name = Path.GetFileName(entry);

                if (Directory.Exists(entry))
                {
                    if (IsSkippedDir(name))
                    {
                        continue;
                    }

                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch { continue; }

                    WalkFiles(entry, fileNames, results, state);
                }
                else
                {
                    foreach (var fileName in fileNames)
                    {
                        if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(entry);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>Read a file that contains a bare numeric AppID (e.g. steam_appid.txt).</summary>
        private static int? ReadAppIdNumber(string path)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { return null; }

            var trimmed = text.Trim();
            if (trimmed.Length == 0 || !trimmed.All(char.IsDigit))
            {
                return null;
            }

            int id;
            if (int.TryParse(trimmed, out id) && id > 0)
            {
                return id;
            }
            return null;
        }

        private static readonly Regex AppIdLineRegex =
            new Regex(@"(?im)^\s*AppId\s*=\s*(\d{3,8})\s*$");

        private static readonly Regex AppIdConfigRegex =
            new Regex(@"(?im)(?:^\s*(?:AppID|AppId|appid)\s*=\s*|^\s*)(\d{3,8})\s*$");

        private static int? ReadAppIdFromRegex(string path, Regex regex)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { return null; }

            var m = regex.Match(text);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var id) && id > 0)
            {
                return id;
            }
            return null;
        }

        /// <summary>Shallow shortcut: steam_appid.txt in the game directory or its steam_settings subfolder.</summary>
        private static int? ReadAppIdIn(string dir)
        {
            var direct = ReadAppIdNumber(Path.Combine(dir, "steam_appid.txt"));
            if (direct.HasValue)
            {
                return direct;
            }
            return ReadAppIdNumber(Path.Combine(dir, "steam_settings", "steam_appid.txt"));
        }

        /// <summary>1) steam_appid.txt in the game directory, then anywhere in the tree (shallowest first).</summary>
        private static int? DetectGameAppId(string gameDir)
        {
            var direct = ReadAppIdIn(gameDir);
            if (direct.HasValue)
            {
                return direct;
            }

            var files = FindFiles(gameDir, "steam_appid.txt");
            if (files.Count == 0)
            {
                return null;
            }

            files.Sort((a, b) =>
                a.Split(Path.DirectorySeparatorChar).Length.CompareTo(
                    b.Split(Path.DirectorySeparatorChar).Length));

            foreach (var file in files)
            {
                var id = ReadAppIdNumber(file);
                if (id.HasValue)
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>
        /// 2) RUNE and similar emulators don't create steam_appid.txt; the AppID lives in
        /// steam_emu.ini / rune.ini, possibly buried deep. Prefer the copy next to steam_api*.dll.
        /// </summary>
        private static int? ReadAppIdFromEmuIni(string gameDir)
        {
            var iniFiles = FindFiles(gameDir, "steam_emu.ini", "rune.ini");
            if (iniFiles.Count == 0)
            {
                return null;
            }

            var dllDirs = new HashSet<string>(
                FindFiles(gameDir, "steam_api.dll", "steam_api64.dll")
                    .Select(f => Path.GetDirectoryName(f).ToLowerInvariant())
            );

            foreach (var ini in iniFiles)
            {
                if (dllDirs.Contains(Path.GetDirectoryName(ini).ToLowerInvariant()))
                {
                    var id = ReadAppIdFromRegex(ini, AppIdLineRegex);
                    if (id.HasValue)
                    {
                        return id;
                    }
                }
            }

            foreach (var ini in iniFiles)
            {
                var id = ReadAppIdFromRegex(ini, AppIdLineRegex);
                if (id.HasValue)
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>3) Upstream config files (SmartSteamEmu / SSE / flt), anywhere in the tree.</summary>
        private static int? AppIdFromConfig(string gameDir)
        {
            var files = FindFiles(
                gameDir,
                "SteamConfig.ini",
                "SmartSteamEmu.ini",
                "steam_appid.txt",
                "flt.ini");

            foreach (var file in files)
            {
                var id = ReadAppIdFromRegex(file, AppIdConfigRegex);
                if (id.HasValue)
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>
        /// 4) Trailing numeric segment of the path (e.g. .../2288630),
        /// ignoring the SteamEmu stats folders that some emulators append.
        /// </summary>
        private static int? AppIdFromPath(string gameDir)
        {
            var s = gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var suffixes = new[]
            {
                "\\stats", "/stats",
                "\\SteamEmu\\UserStats", "/SteamEmu/UserStats",
                "\\SteamEmu", "/SteamEmu",
            };

            foreach (var suffix in suffixes)
            {
                if (s.Length >= suffix.Length &&
                    string.Equals(s.Substring(s.Length - suffix.Length), suffix, StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(0, s.Length - suffix.Length);
                    break;
                }
            }

            var m = Regex.Match(s, @"(\d+)$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var id) && id > 0)
            {
                return id;
            }

            return null;
        }

        public static bool TrySteamId(Game game, out int result)
        {
            var id = SteamId(game);
            if (id != null)
            {
                result = (int)id;
                return true;
            }

            result = 0;
            return false;
        }

        public static bool IsOnPc(Game game)
        {
            var pcSpecs = new List<string> { "macintosh", "pc_dos", "pc_linux", "pc_windows" };
            var pcNames = new List<string> { "Macintosh", "PC", "PC (DOS)", "PC (Linux)", "PC (Windows)" };
            return game.Platforms == null
                || game.Platforms.Count == 0
                || game.Platforms.Any(x => pcSpecs.Contains(x.SpecificationId))
                || game.Platforms.Any(x => pcNames.Contains(x.Name));
        }

        public static string GetTitleId(Game game)
        {
            return string.Format("{0}:{1}:{2}", game.PluginId, game.GameId, game.Name);
        }

        public static Platform GetGamePlatform(Game game)
        {
            return game?.Platforms?.ElementAtOrDefault(0);
        }

        public static bool ShouldSkipGame(Game game)
        {
            return Etc.HasTag(game, Tags.SKIP);
        }

        public static bool HasTag(Game game, string tagName)
        {
            return game.Tags?.Any(tag => tag.Name == tagName) ?? false;
        }

        public static string NormalizePath(string path)
        {
            return HOME_DIR.Replace(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Replace("/", "\\");
        }

        public static bool OpenDir(string path)
        {
            try
            {
                Process.Start(NormalizePath(path));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetBackupDisplayLine(Cli.Output.Backup backup)
        {
            var retSb = new StringBuilder();
            retSb.Append(backup.When.ToLocalTime().ToString());

            if (!string.IsNullOrEmpty(backup.Os) && backup.Os != "windows")
            {
                retSb.AppendFormat(" [{0}]", backup.Os);
            }

            if (backup.Locked)
            {
                retSb.Append(" [🔒]");
            }

            if (!string.IsNullOrEmpty(backup.Comment))
            {
                var lineSb = new StringBuilder();
                var parts = backup.Comment.Split();

                foreach (var part in parts)
                {
                    if (lineSb.Length > 0)
                    {
                        lineSb.Append(" ");
                    }

                    lineSb.Append(part);

                    if (lineSb.Length > 60)
                    {
                        retSb.AppendFormat("\n    {0}", lineSb);
                        lineSb.Clear();
                    }
                }

                if (lineSb.Length > 0)
                {
                    retSb.AppendFormat("\n    {0}", lineSb);
                }
            }

            return retSb.ToString();
        }

        public static void OpenUrl(string url)
        {
            try
            {
                RunCommand("cmd.exe", $"/c \"start {url}\"");
            }
            catch
            { }
        }

        public static void OpenLudusaviMainPage()
        {
            OpenUrl("https://github.com/mtkennerly/ludusavi");
        }

        public static void OpenLudusaviReleasePage()
        {
            OpenUrl("https://github.com/mtkennerly/ludusavi/releases");
        }
    }

    public static class Tags
    {
        private const string PREFIX = "[Ludusavi] ";

        public const string LEGACY_SKIP = "ludusavi-skip";
        public const string SKIP = PREFIX + "Skip";

        public const string GAME_BACKUP = PREFIX + "Game: backup";
        public const string GAME_NO_BACKUP = PREFIX + "Game: no backup";

        public const string GAME_BACKUP_AND_RESTORE = PREFIX + "Game: backup and restore";
        public const string GAME_NO_RESTORE = PREFIX + "Game: no restore";

        public const string PLATFORM_BACKUP = PREFIX + "Platform: backup";
        public const string PLATFORM_NO_BACKUP = PREFIX + "Platform: no backup";

        public const string PLATFORM_BACKUP_AND_RESTORE = PREFIX + "Platform: backup and restore";
        public const string PLATFORM_NO_RESTORE = PREFIX + "Platform: no restore";

        public const string BACKED_UP = PREFIX + "Backed up";
        public const string UNKNOWN_SAVE_DATA = PREFIX + "Unknown save data";

        // Format: {new tag, {conflicting tags}}
        public static Dictionary<string, string[]> CONFLICTS = new Dictionary<string, string[]> {
            {SKIP, new string[] {}},
            {GAME_BACKUP, new string[] {SKIP, GAME_NO_BACKUP}},
            {GAME_NO_BACKUP, new string[] {GAME_BACKUP, GAME_BACKUP_AND_RESTORE}},
            {GAME_BACKUP_AND_RESTORE, new string[] {SKIP, GAME_BACKUP, GAME_NO_BACKUP, GAME_NO_RESTORE}},
            {GAME_NO_RESTORE, new string[] {GAME_BACKUP_AND_RESTORE}},
            {PLATFORM_BACKUP, new string[] {SKIP, PLATFORM_NO_BACKUP}},
            {PLATFORM_NO_BACKUP, new string[] {PLATFORM_BACKUP, PLATFORM_BACKUP_AND_RESTORE}},
            {PLATFORM_BACKUP_AND_RESTORE, new string[] {SKIP, PLATFORM_BACKUP, PLATFORM_NO_BACKUP, PLATFORM_NO_RESTORE}},
            {PLATFORM_NO_RESTORE, new string[] {PLATFORM_BACKUP_AND_RESTORE}},
        };

        // Format: {(new tag, conflicting tag), conflict replacement}
        public static Dictionary<(string, string), string> REPLACEMENTS = new Dictionary<(string, string), string> {
            {(GAME_NO_RESTORE, GAME_BACKUP_AND_RESTORE), GAME_BACKUP},
            {(PLATFORM_NO_RESTORE, PLATFORM_BACKUP_AND_RESTORE), PLATFORM_BACKUP},
        };
    }
}
