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

            var root = game.InstallDirectory;

            // 1. Direct file in install root
            if (TryReadAppId(root, "steam_appid.txt", out id))
            {
                return id;
            }

            // 2. steam_settings subfolder
            if (TryReadAppId(Path.Combine(root, "steam_settings"), "steam_appid.txt", out id))
            {
                return id;
            }

            // 3. Recurse up to depth 8 for steam_appid.txt (skip hidden/$ dirs)
            var found = SearchFileRecursive(root, "steam_appid.txt", 0, 8);
            if (found.HasValue)
            {
                return found;
            }

            // 4. Ini-based detection (SmartSteamEmu/SSE/flt)
            found = AppIdFromConfig(root);
            if (found.HasValue)
            {
                return found;
            }

            // 5. Trailing number in folder name
            var dirName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            var m = Regex.Match(dirName, @"(\d+)$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var tailId) && tailId > 10000)
            {
                return tailId;
            }

            return null;
        }

        private static bool TryReadAppId(string dir, string fileName, out int appId)
        {
            appId = 0;
            try
            {
                var path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path).Trim();
                    if (int.TryParse(content, out var id) && id > 0)
                    {
                        appId = id;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static int? SearchFileRecursive(string dir, string fileName, int depth, int maxDepth)
        {
            if (depth >= maxDepth) return null;

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { return null; }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith(".") || name.StartsWith("$")) continue;

                var file = Path.Combine(sub, fileName);
                if (File.Exists(file))
                {
                    try
                    {
                        var content = File.ReadAllText(file).Trim();
                        if (int.TryParse(content, out var id) && id > 0)
                        {
                            return id;
                        }
                    }
                    catch { }
                }

                var result = SearchFileRecursive(sub, fileName, depth + 1, maxDepth);
                if (result.HasValue) return result;
            }

            return null;
        }

        private static readonly Regex AppIdIniRegex =
            new Regex(@"(?im)^\s*(?:AppID|AppId|appid)\s*=\s*(\d{3,8})\s*$");

        private static readonly string[] IniFileNames =
        {
            "SteamConfig.ini", "SmartSteamEmu.ini", "steam_appid.txt",
            Path.Combine("steam_settings", "steam_appid.txt"), "flt.ini"
        };

        private static int? ReadAppIdFromIniAt(string dir)
        {
            foreach (var rel in IniFileNames)
            {
                var full = Path.Combine(dir, rel);
                if (!File.Exists(full)) continue;

                string text;
                try { text = File.ReadAllText(full); }
                catch { continue; }

                // steam_appid.txt is just a number
                if (rel.EndsWith("steam_appid.txt"))
                {
                    if (int.TryParse(text.Trim(), out var id) && id > 0)
                        return id;
                    continue;
                }

                var m = AppIdIniRegex.Match(text);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var id2) && id2 > 0)
                {
                    return id2;
                }
            }
            return null;
        }

        private static int? AppIdFromConfig(string gameDir)
        {
            // Search upward 5 levels
            var cur = gameDir;
            for (int i = 0; i < 5; i++)
            {
                var found = ReadAppIdFromIniAt(cur);
                if (found.HasValue) return found;

                var parent = Path.GetDirectoryName(cur);
                if (string.IsNullOrEmpty(parent)) break;
                cur = parent;
            }

            // Search downward recursively up to 6 levels
            return SearchIniRecursive(gameDir, 0, 6);
        }

        private static int? SearchIniRecursive(string dir, int depth, int maxDepth)
        {
            if (depth >= maxDepth) return null;

            var found = ReadAppIdFromIniAt(dir);
            if (found.HasValue) return found;

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { return null; }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith(".") || name.StartsWith("$")) continue;

                var result = SearchIniRecursive(sub, depth + 1, maxDepth);
                if (result.HasValue) return result;
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
