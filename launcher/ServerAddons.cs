using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace RecRoomLauncher
{
    internal enum AddonKind { Clothing, Room, Archive, Missing }

    // One add-on as seen in an installed server folder. Turning an add-on off never deletes it:
    //   Clothing: "enabled" flag in data/downloadable-clothing.json (the server re-reads it, no restart)
    //   Room:     data/ugc/<room>  <->  data/ugc-disabled/<room>   (applies on the next server start)
    //   Archive:  cdn/archive      <->  cdn/archive-disabled        (the route checks files per request)
    // Every archived room has a fixed LocalRoomId, so moving one never renumbers the others.
    internal sealed class ServerAddon
    {
        public AddonKind Kind;
        public string Id;
        public string Title;
        public string Detail;
        public bool Enabled;
        public PackGroup Download;   // set for add-ons that are not installed but published in the release
        public bool NeedsRestart { get { return Kind == AddonKind.Room; } }
    }

    internal static class ServerAddons
    {
        private static readonly Regex EnabledFlag = new Regex("\"enabled\"\\s*:\\s*(true|false)");

        private static readonly Dictionary<string, string> RoomNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "MakeItToMidnight", "Make It to Midnight" }, { "MyLittleMonsters", "My Little Monsters" },
            { "RunTheBlock", "Run the Block" }, { "Showdown", "Showdown" },
        };

        private static string Root(string serverDirectory) { return Path.GetDirectoryName(serverDirectory.TrimEnd('\\', '/')); }
        private static string ClothingManifest(string serverDirectory) { return Path.Combine(serverDirectory, "data", "downloadable-clothing.json"); }
        private static string UgcOn(string serverDirectory) { return Path.Combine(serverDirectory, "data", "ugc"); }
        private static string UgcOff(string serverDirectory) { return Path.Combine(serverDirectory, "data", "ugc-disabled"); }
        private static string ArchiveOn(string serverDirectory) { return Path.Combine(serverDirectory, "cdn", "archive"); }
        private static string ArchiveOff(string serverDirectory) { return Path.Combine(serverDirectory, "cdn", "archive-disabled"); }

        public static string FriendlyRoomName(string folder)
        {
            string name;
            if (RoomNames.TryGetValue(folder, out name)) return name;
            return Regex.Replace(folder, "(?<=[a-z])(?=[A-Z])", " ");
        }

        // The top-level flag sits in the first lines; a targeted edit keeps the 10 MB manifest byte-identical otherwise.
        private static bool? ReadClothingFlag(string manifest)
        {
            using (var reader = new StreamReader(manifest, Encoding.UTF8))
            {
                var head = new char[4096];
                var read = reader.Read(head, 0, head.Length);
                var match = EnabledFlag.Match(new string(head, 0, read));
                if (!match.Success) return null;
                return match.Groups[1].Value == "true";
            }
        }

        public static List<ServerAddon> Detect(string serverDirectory)
        {
            var addons = new List<ServerAddon>();
            var manifest = ClothingManifest(serverDirectory);
            if (File.Exists(manifest))
            {
                var bundles = Directory.Exists(Path.Combine(serverDirectory, "cdn", "clothing"))
                    ? Directory.EnumerateFiles(Path.Combine(serverDirectory, "cdn", "clothing"), "*.bundle").Count() : 0;
                var flag = ReadClothingFlag(manifest);
                addons.Add(new ServerAddon {
                    Kind = AddonKind.Clothing, Id = "clothing", Title = "Clothing library",
                    Detail = bundles.ToString("N0") + " archived clothing files · switches immediately",
                    Enabled = flag == true
                });
            }
            var rooms = new List<ServerAddon>();
            foreach (var pair in new[] { new { Dir = UgcOn(serverDirectory), On = true }, new { Dir = UgcOff(serverDirectory), On = false } })
            {
                if (!Directory.Exists(pair.Dir)) continue;
                foreach (var dir in Directory.GetDirectories(pair.Dir))
                {
                    if (!File.Exists(Path.Combine(dir, "room.json"))) continue;
                    var folder = Path.GetFileName(dir);
                    rooms.Add(new ServerAddon {
                        Kind = AddonKind.Room, Id = "room:" + folder, Title = FriendlyRoomName(folder),
                        Detail = "Community room · applies when the server restarts", Enabled = pair.On
                    });
                }
            }
            addons.AddRange(rooms.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase));
            var archiveOn = Directory.Exists(ArchiveOn(serverDirectory));
            if (archiveOn || Directory.Exists(ArchiveOff(serverDirectory)))
                addons.Add(new ServerAddon {
                    Kind = AddonKind.Archive, Id = "archive", Title = "Archived asset catalog",
                    Detail = "Archived Rec Room asset catalog served by the local CDN · switches immediately", Enabled = archiveOn
                });

            // Offer published add-ons this server does not have at all.
            List<PackGroup> groups;
            try { groups = ServerRelease.Groups(); }
            catch (Exception) { groups = new List<PackGroup>(); }
            foreach (var group in groups)
            {
                var present = group.Id == "clothing" ? addons.Any(a => a.Kind == AddonKind.Clothing)
                    : group.Id == "community-rooms" ? rooms.Count > 0
                    : group.Id == "archive" ? addons.Any(a => a.Kind == AddonKind.Archive)
                    : true;
                if (present || !group.Complete) continue;
                addons.Add(new ServerAddon {
                    Kind = AddonKind.Missing, Id = "install:" + group.Id, Title = group.Title,
                    Detail = "Not installed · " + Bytes.Format(group.DownloadBytes) + " download from release " + ServerRelease.Tag + ". " + group.Description,
                    Enabled = false, Download = group
                });
            }
            return addons;
        }

        // Turn installed add-ons on or off. Returns a message for each change, or throws with the first failure.
        public static List<string> Apply(string serverDirectory, IList<ServerAddon> addons, IDictionary<string, bool> wanted)
        {
            var done = new List<string>();
            foreach (var addon in addons)
            {
                bool on;
                if (addon.Kind == AddonKind.Missing || !wanted.TryGetValue(addon.Id, out on) || on == addon.Enabled) continue;
                switch (addon.Kind)
                {
                    case AddonKind.Clothing:
                        SetClothingFlag(ClothingManifest(serverDirectory), on);
                        break;
                    case AddonKind.Room:
                        var folder = addon.Id.Substring("room:".Length);
                        MoveFolder(Path.Combine(on ? UgcOff(serverDirectory) : UgcOn(serverDirectory), folder),
                                   Path.Combine(on ? UgcOn(serverDirectory) : UgcOff(serverDirectory), folder));
                        break;
                    case AddonKind.Archive:
                        MoveFolder(on ? ArchiveOff(serverDirectory) : ArchiveOn(serverDirectory), on ? ArchiveOn(serverDirectory) : ArchiveOff(serverDirectory));
                        break;
                }
                addon.Enabled = on;
                done.Add((on ? "Turned on " : "Turned off ") + addon.Title + ".");
            }
            return done;
        }

        private static void SetClothingFlag(string manifest, bool on)
        {
            var bytes = File.ReadAllBytes(manifest);
            var text = new UTF8Encoding(false).GetString(bytes);
            var match = EnabledFlag.Match(text, 0, Math.Min(text.Length, 4096));
            if (!match.Success) throw new InvalidOperationException("The clothing manifest has no enabled setting.");
            var updated = text.Substring(0, match.Groups[1].Index) + (on ? "true" : "false") + text.Substring(match.Groups[1].Index + match.Groups[1].Length);
            var temporary = manifest + ".tmp";
            File.WriteAllText(temporary, updated, new UTF8Encoding(false));
            File.Replace(temporary, manifest, null);
        }

        private static void MoveFolder(string from, string to)
        {
            if (!Directory.Exists(from)) throw new InvalidOperationException("Missing folder: " + from);
            if (Directory.Exists(to)) throw new InvalidOperationException("Both an enabled and a disabled copy exist: " + to + ". Remove one of them by hand.");
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            try { Directory.Move(from, to); }
            catch (IOException ex) { throw new InvalidOperationException("Could not move " + Path.GetFileName(from) + " (" + ex.Message + "). Stop the server and try again."); }
        }

        // Download published add-ons and merge them into an existing server, checking every file.
        public static void Install(string serverDirectory, IList<PackGroup> groups, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            var root = Root(serverDirectory);
            long free;
            var needed = groups.Sum(g => g.DownloadBytes + g.InstalledBytes) + (64L << 20);
            if (InstallPaths.TryFreeSpace(root, out free) && free < needed)
                throw new InvalidOperationException("Not enough free space: " + Bytes.Format(needed) + " needed, " + Bytes.Format(free) + " available.");
            var assets = groups.SelectMany(g => g.Remote).ToList();
            var files = ServerRelease.Download(assets, progress, cancel);
            var packages = files.Select(ServerSetup.ReadPackage).ToList();
            var staging = Path.Combine(root, ".addon-install");
            // Windows file APIs used here stop at 260 characters; check before writing anything.
            var longest = packages.SelectMany(p => p.Files).Max(f => f.Path.Length);
            if (staging.Length + 1 + longest >= 248)
                throw new InvalidOperationException("The server folder path is too long for this add-on's files (Windows limits paths to 260 characters). Move the server to a shorter folder, for example C:\\RecToon, and choose it in Settings.");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            try
            {
                var total = packages.Sum(p => p.TotalBytes);
                long done = 0;
                foreach (var package in packages)
                    done = ServerSetup.ExtractVerifiedInto(package, staging, done, total, progress, cancel);
                MergeInto(staging, root);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (Exception) { }
            }
            ServerRelease.DeleteDownloads();
        }

        // Move staged files into the server. An existing file is kept only if it is the same size;
        // anything else is a conflict and nothing further is moved.
        private static void MergeInto(string staging, string root)
        {
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToList())
            {
                var relative = file.Substring(staging.Length).TrimStart('\\');
                var target = Path.Combine(root, relative);
                if (File.Exists(target))
                {
                    if (new FileInfo(target).Length == new FileInfo(file).Length) continue;
                    throw new InvalidOperationException("The add-on would replace a different existing file: " + relative);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(file, target);
            }
        }
    }
}
