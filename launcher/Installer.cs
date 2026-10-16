using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RecRoomLauncher
{
    // The one client build the bundled patch supports. Files and SHA-1s come from Steam's
    // depot manifest 8867414017562658247 (tools/make_build_manifest.py), embedded at build time.
    internal static class ClientBuild
    {
        public const string Version = "20260323.14";
        public const int AppId = 471710;
        public const int DepotId = 471711;
        public const string ManifestId = "8867414017562658247";
        public const string SteamCommand = "download_depot 471710 471711 8867414017562658247";

        // The patch's own BuildGuard fingerprints (patch-2026/AGENTS.md); checked after install.
        public static readonly KeyValuePair<string, string>[] ModuleFingerprints = {
            new KeyValuePair<string, string>("GameAssembly.dll", "A6D90770CDFDA4043EADB278A16198ED0907C3BA2AC09BC37D06CD4DD22A1D84"),
            new KeyValuePair<string, string>("Referee.dll", "5868C90A7C67757F3B678AA356CBD933D646C9749E0ACAA31B7DCC9EAEE12EAD"),
            new KeyValuePair<string, string>("UnityPlayer.dll", "30F4A99C49566B2C5045469B5A39572DD2298ADC31F7FE2BC39D8BB8FC9D75BB"),
        };

        private static List<BuildFile> _files;
        private static List<string> _directories;
        private static long _downloadBytes;

        public static List<BuildFile> Files { get { Load(); return _files; } }
        public static List<string> Directories { get { Load(); return _directories; } }
        public static long TotalBytes { get { return Files.Sum(f => f.Size); } }
        public static long DownloadBytes { get { Load(); return _downloadBytes; } }

        private static void Load()
        {
            if (_files != null) return;
            var files = new List<BuildFile>();
            var dirs = new List<string>();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecRoomLauncher.build-20260323.14.manifest"))
            {
                if (stream == null) throw new InvalidOperationException("The game file list is missing from the launcher.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("# files="))
                        {
                            var download = line.Split(' ').FirstOrDefault(p => p.StartsWith("download="));
                            if (download != null) long.TryParse(download.Substring(9), out _downloadBytes);
                            continue;
                        }
                        var parts = line.Split('\t');
                        if (parts.Length == 2 && parts[0] == "D") dirs.Add(parts[1]);
                        else if (parts.Length == 4 && parts[0] == "F")
                            files.Add(new BuildFile(parts[3], long.Parse(parts[1]), parts[2]));
                    }
                }
            }
            if (files.Count == 0) throw new InvalidOperationException("The game file list in the launcher is empty.");
            _directories = dirs;
            _files = files;
        }
    }

    internal sealed class BuildFile
    {
        public readonly string Path;
        public readonly long Size;
        public readonly string Sha1;
        public BuildFile(string path, long size, string sha1) { Path = path; Size = size; Sha1 = sha1; }
    }

    internal sealed class InstallProgress
    {
        public readonly string Stage;
        public readonly long Done;
        public readonly long Total;
        public readonly string Detail;
        public InstallProgress(string stage, long done, long total, string detail) { Stage = stage; Done = done; Total = total; Detail = detail; }
        public double Fraction { get { return Total <= 0 ? 0 : Math.Min(1.0, (double)Done / Total); } }
    }

    internal static class Bytes
    {
        public static string Format(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.00") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }
    }

    // ---------- Game files ----------

    internal static class GameFiles
    {
        private const int BufferSize = 1 << 20;

        public static string FullPath(string root, BuildFile file) { return Path.Combine(root, file.Path); }

        // Cheap startup check: every listed file exists with the right size.
        public static bool LooksComplete(string root, out string problem)
        {
            problem = null;
            if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) { problem = "The game folder does not exist."; return false; }
            foreach (var file in ClientBuild.Files)
            {
                var info = new FileInfo(FullPath(root, file));
                if (!info.Exists || info.Length != file.Size) { problem = "Missing or incomplete game file: " + file.Path; return false; }
            }
            return true;
        }

        // Bytes of the build already present with the exact final size (for download progress).
        public static long PresentBytes(string root)
        {
            long done = 0;
            if (!Directory.Exists(root)) return 0;
            foreach (var file in ClientBuild.Files)
            {
                var info = new FileInfo(FullPath(root, file));
                if (info.Exists) done += Math.Min(info.Length, file.Size);
            }
            return done;
        }

        // Full SHA-1 verification against Steam's manifest. Returns the files that failed.
        public static List<string> Verify(string root, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            var failed = new List<string>();
            var total = ClientBuild.TotalBytes;
            long done = 0;
            var buffer = new byte[BufferSize];
            foreach (var file in ClientBuild.Files)
            {
                cancel.ThrowIfCancellationRequested();
                var path = FullPath(root, file);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != file.Size)
                {
                    failed.Add(file.Path);
                    done += file.Size;
                    continue;
                }
                using (var sha = SHA1.Create())
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        done += read;
                        progress.Report(new InstallProgress("verify", done, total, file.Path));
                    }
                    sha.TransformFinalBlock(buffer, 0, 0);
                    if (!Hex(sha.Hash).Equals(file.Sha1, StringComparison.OrdinalIgnoreCase)) failed.Add(file.Path);
                }
            }
            return failed;
        }

        // Copy only the build's listed files (nothing else from the source folder).
        public static void CopyBuild(string source, string target, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            var total = ClientBuild.TotalBytes;
            long done = 0;
            var buffer = new byte[BufferSize];
            foreach (var dir in ClientBuild.Directories) Directory.CreateDirectory(Path.Combine(target, dir));
            foreach (var file in ClientBuild.Files)
            {
                cancel.ThrowIfCancellationRequested();
                var destination = FullPath(target, file);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using (var input = new FileStream(FullPath(source, file), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        done += read;
                        progress.Report(new InstallProgress("copy", done, total, file.Path));
                    }
                }
            }
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                return Hex(sha.ComputeHash(stream));
        }

        public static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        public static void CheckFingerprints(string root)
        {
            foreach (var module in ClientBuild.ModuleFingerprints)
            {
                var path = Path.Combine(root, module.Key);
                if (!File.Exists(path) || !Sha256(path).Equals(module.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(module.Key + " is not the Rec Room " + ClientBuild.Version + " file the patch requires.");
            }
        }
    }

    // ---------- Destination rules ----------

    internal static class InstallPaths
    {
        public static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecRoomCommunityLauncher");
        public static readonly string DefaultGame = Path.Combine(DataDirectory, "Game");
        public static readonly string DefaultServer = Path.Combine(DataDirectory, "Server");

        public static string Normalize(string path)
        {
            return Path.GetFullPath((path ?? "").Trim()).TrimEnd('\\', '/');
        }

        // A fresh install goes into a new or empty folder, never Steam's own library.
        public static string ValidateFreshDestination(string path, long requiredBytes)
        {
            if (String.IsNullOrWhiteSpace(path)) return "Choose a folder for Rec Room.";
            string full;
            try { full = Normalize(path); }
            catch (Exception ex) { return "That folder path is not valid: " + ex.Message; }
            if (!Path.IsPathRooted(full) || Path.GetPathRoot(full).TrimEnd('\\') == full.TrimEnd('\\')) return "Choose a folder, not the root of a drive.";
            if (full.IndexOf(@"\steamapps\", StringComparison.OrdinalIgnoreCase) >= 0 || full.EndsWith(@"\steamapps", StringComparison.OrdinalIgnoreCase))
                return "Choose a folder outside Steam's library so Steam cannot update this pinned build.";
            if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
                return "That folder already has files in it. Choose a new or empty folder.";
            if (File.Exists(full)) return "A file already exists at that path.";
            long free;
            if (TryFreeSpace(full, out free) && free < requiredBytes)
                return "Not enough free space: " + Bytes.Format(requiredBytes) + " needed, " + Bytes.Format(free) + " available.";
            return null;
        }

        public static bool TryFreeSpace(string path, out long free)
        {
            free = 0;
            try
            {
                var root = Path.GetPathRoot(Normalize(path));
                if (String.IsNullOrEmpty(root)) return false;
                free = new DriveInfo(root).AvailableFreeSpace;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool SameVolume(string a, string b)
        {
            try { return String.Equals(Path.GetPathRoot(Normalize(a)), Path.GetPathRoot(Normalize(b)), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }
    }

    // ---------- Steam depot download ----------

    // The supported build is fetched by the user's own signed-in Steam client through its console
    // (download_depot). The launcher never handles Steam credentials; it watches Steam's output folder.
    internal static class SteamDepot
    {
        public static string SteamDirectory()
        {
            var path = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!String.IsNullOrEmpty(path)) path = path.Replace('/', '\\');
            if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(path) ? path : null;
        }

        public static string OutputDirectory()
        {
            var steam = SteamDirectory();
            return steam == null ? null : Path.Combine(steam, "steamapps", "content", "app_" + ClientBuild.AppId, "depot_" + ClientBuild.DepotId);
        }

        public static bool IsRunning()
        {
            var processes = Process.GetProcessesByName("steam");
            var running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            return running;
        }

        public static void OpenConsole()
        {
            Process.Start(new ProcessStartInfo("steam://open/console") { UseShellExecute = true });
        }

        public static void StartSteam()
        {
            var exe = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
            if (String.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                var dir = SteamDirectory();
                exe = dir == null ? null : Path.Combine(dir, "steam.exe");
            }
            if (exe == null || !File.Exists(exe)) throw new InvalidOperationException("Steam is not installed. Install Steam, sign in, then try again.");
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
        }
    }

    // ---------- Patch payload ----------

    internal static class PatchPackage
    {
        public const string Label = "2026patch (Oct 15 build, hidden-console launch scripts Oct 16)";
        public static readonly string[] FileNames = { "2026patch.dll", "Injector.exe", "2026patch.ini", "RecRoomScreen.bat", "RecRoomVR.bat" };

        private static Stream Open(string name)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecRoomLauncher.patch." + name);
            if (stream == null) throw new InvalidOperationException(name + " is missing from the launcher's patch package.");
            return stream;
        }

        // SHA-256 list written by build.ps1 beside the embedded files.
        private static Dictionary<string, string> ExpectedHashes()
        {
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new StreamReader(Open("SHA256SUMS"), Encoding.ASCII))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(new[] { ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2) hashes[parts[1]] = parts[0];
                }
            }
            return hashes;
        }

        // Writes the five patch files beside RecRoom.exe. Existing connection settings in
        // 2026patch.ini are kept; replaced files are backed up first.
        public static void Install(string gameDirectory, Action<string> log)
        {
            var hashes = ExpectedHashes();
            var backup = Path.Combine(InstallPaths.DataDirectory, "backups", "patch-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Dictionary<string, string> keptNetwork = null;
            foreach (var name in FileNames)
            {
                byte[] data;
                using (var stream = Open(name))
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    data = buffer.ToArray();
                }
                string expected;
                using (var sha = SHA256.Create())
                    if (!hashes.TryGetValue(name, out expected) || !GameFiles.Hex(sha.ComputeHash(data)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The launcher's copy of " + name + " failed its integrity check.");
                var target = Path.Combine(gameDirectory, name);
                if (File.Exists(target))
                {
                    Directory.CreateDirectory(backup);
                    File.Copy(target, Path.Combine(backup, name), true);
                    if (name == "2026patch.ini") keptNetwork = Ini.ReadSection(target, "Network");
                }
                var temporary = target + ".new";
                File.WriteAllBytes(temporary, data);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temporary, target);
            }
            if (keptNetwork != null && keptNetwork.Count > 0)
            {
                Ini.SetValues(Path.Combine(gameDirectory, "2026patch.ini"), "Network", keptNetwork);
                log("Kept your existing connection settings in 2026patch.ini.");
            }
        }

        // The patch's own read-only preflight: DLL present, INI valid, client fingerprints match.
        public static string RunPreflight(string gameDirectory)
        {
            var start = new ProcessStartInfo(Path.Combine(gameDirectory, "Injector.exe"), "--check-install") {
                WorkingDirectory = gameDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(120000)) { try { process.Kill(); } catch (Exception) { } throw new InvalidOperationException("The patch preflight check did not finish."); }
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var last = lines.Length > 0 ? lines[lines.Length - 1].Trim() : "";
                if (process.ExitCode != 0) throw new InvalidOperationException("The patch preflight check failed: " + last);
                return last;
            }
        }
    }

    internal static class Ini
    {
        public static Dictionary<string, string> ReadSection(string path, string wanted)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) section = line.Substring(1, line.Length - 2);
                else if (section.Equals(wanted, StringComparison.OrdinalIgnoreCase) && !line.StartsWith(";") && line.Contains("="))
                {
                    var split = line.IndexOf('=');
                    values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
                }
            }
            return values;
        }

        // Replace values of existing keys in one section; other lines and comments are untouched.
        public static void SetValues(string path, string wanted, Dictionary<string, string> values)
        {
            var lines = File.ReadAllLines(path);
            var section = "";
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                if (!section.Equals(wanted, StringComparison.OrdinalIgnoreCase) || line.StartsWith(";") || !line.Contains("=")) continue;
                var key = line.Substring(0, line.IndexOf('=')).Trim();
                string value;
                if (values.TryGetValue(key, out value)) lines[i] = key + "=" + value;
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
    }

    // ---------- Referee ----------

    // Steam's install script for this build runs RefereeClientInstaller.exe -install (as administrator).
    // A depot download skips that, so setup offers to run the same step.
    internal static class Referee
    {
        public const string ServiceName = "RefereeClientApp";

        public static bool IsInstalled()
        {
            try { return ServiceController.GetServices().Any(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase)); }
            catch (Exception) { return false; }
        }

        public static async Task<int> Install(string gameDirectory)
        {
            var installer = Path.Combine(gameDirectory, "RefereeClientInstaller.exe");
            if (!File.Exists(installer)) throw new InvalidOperationException("RefereeClientInstaller.exe is missing from the game folder.");
            var start = new ProcessStartInfo(installer, "-install") {
                WorkingDirectory = gameDirectory, UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(start))
            {
                await Task.Run(() => process.WaitForExit());
                return process.ExitCode;
            }
        }
    }

    // ---------- Server ----------

    internal static class ServerSetup
    {
        // The launcher starts "Start Server.cmd" from the folder above server.js.
        public static string ValidateServerFolder(string serverDirectory)
        {
            var dir = (serverDirectory ?? "").Trim().TrimEnd('\\', '/');
            if (!File.Exists(Path.Combine(dir, "server.js"))) return "That folder does not contain server.js.";
            var parent = Path.GetDirectoryName(dir);
            if (parent == null || !File.Exists(Path.Combine(parent, "Start Server.cmd"))) return "Start Server.cmd was not found beside the server folder.";
            if (!Directory.Exists(Path.Combine(dir, "data"))) return "The server folder has no data folder.";
            return null;
        }

        // node.exe bundled with a server package (runtime\node.exe beside "Start Server.cmd"), else from PATH.
        public static string NodeFor(string serverDirectory)
        {
            try
            {
                var parent = Path.GetDirectoryName((serverDirectory ?? "").Trim().TrimEnd('\\', '/'));
                var bundled = parent == null ? null : Path.Combine(parent, "runtime", "node.exe");
                if (bundled != null && File.Exists(bundled)) return bundled;
            }
            catch (ArgumentException) { }
            return FindNode();
        }

        public static string FindNode()
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim().Trim('"'), "node.exe");
                    if (dir.Trim().Length > 0 && File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        // Packages are ZIPs built by tools/package_server.py with recToon-package.json at the root.
        public const string PackageManifestName = "recToon-package.json";
        private static readonly string[] CoreExtras = { "Start Server.cmd", "README.txt" };

        public static ServerPackage ReadPackage(string zipPath)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.GetEntry(PackageManifestName);
                if (entry == null) throw new InvalidOperationException(Path.GetFileName(zipPath) + " is not a RecToon server package.");
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var package = serializer.Deserialize<ServerPackage>(reader.ReadToEnd());
                    if (package == null || package.Format != 1 || package.Product != "RecToon server" || String.IsNullOrEmpty(package.Id) || package.Files == null)
                        throw new InvalidOperationException(Path.GetFileName(zipPath) + " is not a supported RecToon server package.");
                    package.ZipPath = zipPath;
                    return package;
                }
            }
        }

        // Optional packs for the same version, found beside the core package.
        public static List<ServerPackage> FindPacks(ServerPackage core)
        {
            var packs = new List<ServerPackage>();
            var folder = Path.GetDirectoryName(core.ZipPath);
            foreach (var zip in Directory.GetFiles(folder, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (String.Equals(zip, core.ZipPath, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var pack = ReadPackage(zip);
                    if (pack.Id != "core" && pack.Version == core.Version && pack.Requires == "core") packs.Add(pack);
                }
                catch (Exception) { }
            }
            return packs;
        }

        // A core package shipped beside the launcher (server-packages\RecToon-Server-*-core.zip), if any.
        public static string FindBundledCore()
        {
            try
            {
                var folder = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "server-packages");
                if (!Directory.Exists(folder)) return null;
                return Directory.GetFiles(folder, "RecToon-Server-*-core.zip").OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            }
            catch (Exception) { return null; }
        }

        // Install core plus the chosen packs into a new or empty folder through a staging folder.
        // Every file is checked against its package's SHA-256 list while it is written, and a fresh
        // TLS certificate is generated. Returns the folder that contains server.js.
        public static string InstallPackages(ServerPackage core, IList<ServerPackage> packs, string destination, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            if (core.Id != "core") throw new InvalidOperationException("Choose the server's core package (the file ending in -core.zip).");
            var root = InstallPaths.Normalize(destination);
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidOperationException("Choose a new or empty folder for the server.");
            var all = new List<ServerPackage> { core };
            all.AddRange(packs);
            long free;
            var needed = all.Sum(p => p.TotalBytes) + (64L << 20);
            if (InstallPaths.TryFreeSpace(root, out free) && free < needed)
                throw new InvalidOperationException("Not enough free space: " + Bytes.Format(needed) + " needed, " + Bytes.Format(free) + " available.");
            var staging = root + ".installing";
            // Windows file APIs used here stop at 260 characters; check before writing anything.
            var longest = all.SelectMany(p => p.Files).Max(f => f.Path.Length);
            if (staging.Length + 1 + longest >= 248)
                throw new InvalidOperationException("That install folder path is too long for the server's files (Windows limits paths to 260 characters). Choose a shorter folder, for example C:\\RecToon\\Server.");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            try
            {
                var total = all.Sum(p => p.TotalBytes);
                long done = 0;
                foreach (var package in all)
                    done = ExtractVerified(package, staging, done, total, progress, cancel);
                ServerCertificate.Generate(Path.Combine(staging, "server", "certs"));
                if (Directory.Exists(root)) Directory.Delete(root);
                var parent = Path.GetDirectoryName(root);
                if (parent != null) Directory.CreateDirectory(parent);
                Directory.Move(staging, root);
            }
            catch (Exception)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (Exception) { }
                throw;
            }
            var finalServer = Path.Combine(root, "server");
            var problem = ValidateServerFolder(finalServer);
            if (problem != null) throw new InvalidOperationException("The installed server is incomplete: " + problem);
            return finalServer;
        }

        private static long ExtractVerified(ServerPackage package, string staging, long done, long total, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            var expected = package.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stagingFull = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var buffer = new byte[1 << 20];
            var zipName = Path.GetFileName(package.ZipPath);
            using (var archive = ZipFile.OpenRead(package.ZipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    cancel.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace(Path.DirectorySeparatorChar, '/');
                    if (name == PackageManifestName || name.EndsWith("/")) continue;
                    PackageFile file;
                    var extra = package.Id == "core" && CoreExtras.Contains(name);
                    if (!expected.TryGetValue(name, out file) && !extra)
                        throw new InvalidOperationException(zipName + " contains a file it does not list: " + name);
                    var target = Path.GetFullPath(Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The server package contains an unsafe path: " + name);
                    if (File.Exists(target)) throw new InvalidOperationException("Two packages contain the same file: " + name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    long written = 0;
                    using (var sha = SHA256.Create())
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length))
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            output.Write(buffer, 0, read);
                            written += read;
                            if (!extra) progress.Report(new InstallProgress("server", done + written, total, package.Title + " · " + name));
                        }
                        sha.TransformFinalBlock(buffer, 0, 0);
                        if (!extra && (written != file.Size || !GameFiles.Hex(sha.Hash).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException(name + " in " + zipName + " is damaged (checksum mismatch).");
                    }
                    if (!extra) { done += written; seen.Add(name); }
                }
            }
            var missing = package.Files.FirstOrDefault(f => !seen.Contains(f.Path));
            if (missing != null) throw new InvalidOperationException(zipName + " is missing " + missing.Path + ".");
            return done;
        }
    }

    internal sealed class PackageFile
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
    }

    internal sealed class ServerPackage
    {
        public int Format { get; set; }
        public string Product { get; set; }
        public string Version { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Requires { get; set; }
        public string Group { get; set; }
        public int GroupParts { get; set; }
        public long TotalBytes { get; set; }
        public List<PackageFile> Files { get; set; }
        public string ZipPath { get; set; }
    }

    // A fresh self-signed HTTPS certificate for this PC's server; packages never contain one.
    // The patch accepts it for the configured service domain (DisableCertificatePinning=1).
    internal static class ServerCertificate
    {
        public static void Generate(string certDirectory)
        {
            Directory.CreateDirectory(certDirectory);
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost");
                names.AddDnsName("cdn.localhost");
                names.AddIpAddress(System.Net.IPAddress.Loopback);
                request.CertificateExtensions.Add(names.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                using (var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10)))
                {
                    File.WriteAllText(Path.Combine(certDirectory, "localhost.crt"), Pem("CERTIFICATE", certificate.RawData), Encoding.ASCII);
                    File.WriteAllText(Path.Combine(certDirectory, "localhost.key"), Pem("RSA PRIVATE KEY", Pkcs1(rsa.ExportParameters(true))), Encoding.ASCII);
                }
            }
        }

        private static string Pem(string label, byte[] der)
        {
            var text = new StringBuilder("-----BEGIN " + label + "-----\n");
            var base64 = Convert.ToBase64String(der);
            for (var i = 0; i < base64.Length; i += 64) text.Append(base64.Substring(i, Math.Min(64, base64.Length - i))).Append('\n');
            return text.Append("-----END " + label + "-----\n").ToString();
        }

        // PKCS#1 RSAPrivateKey: SEQUENCE { version 0, n, e, d, p, q, dp, dq, qinv }.
        private static byte[] Pkcs1(RSAParameters p)
        {
            var parts = new[] { new byte[] { 0 }, p.Modulus, p.Exponent, p.D, p.P, p.Q, p.DP, p.DQ, p.InverseQ };
            return Tlv(0x30, parts.SelectMany(Integer).ToArray());
        }

        private static byte[] Integer(byte[] value)
        {
            var start = 0;
            while (start < value.Length - 1 && value[start] == 0) start++;
            var trimmed = value.Skip(start).ToArray();
            if ((trimmed[0] & 0x80) != 0) trimmed = new byte[] { 0 }.Concat(trimmed).ToArray();
            return Tlv(0x02, trimmed);
        }

        private static byte[] Tlv(byte tag, byte[] content)
        {
            var length = content.Length;
            byte[] header;
            if (length < 0x80) header = new[] { tag, (byte)length };
            else if (length < 0x100) header = new[] { tag, (byte)0x81, (byte)length };
            else if (length < 0x10000) header = new[] { tag, (byte)0x82, (byte)(length >> 8), (byte)length };
            else header = new[] { tag, (byte)0x83, (byte)(length >> 16), (byte)(length >> 8), (byte)length };
            return header.Concat(content).ToArray();
        }
    }

    // ---------- Server package groups and release downloads ----------

    // One selectable item in setup: a pack, or all parts of a split pack (installed all-or-nothing).
    internal sealed class PackGroup
    {
        public string Id;
        public string Title;
        public string Description;
        public int ExpectedParts;
        public readonly List<ServerPackage> Local = new List<ServerPackage>();
        public readonly List<ReleaseAsset> Remote = new List<ReleaseAsset>();
        public long InstalledBytes;
        public long DownloadBytes;
        public bool Complete { get { return Local.Count > 0 ? Local.Count == ExpectedParts : Remote.Count == ExpectedParts; } }

        public static string BaseTitle(string title)
        {
            var at = (title ?? "").IndexOf(" (part ", StringComparison.Ordinal);
            return at < 0 ? title : title.Substring(0, at);
        }

        public static List<PackGroup> FromLocal(IEnumerable<ServerPackage> packs)
        {
            var groups = new List<PackGroup>();
            foreach (var pack in packs)
            {
                var id = String.IsNullOrEmpty(pack.Group) ? pack.Id : pack.Group;
                var group = groups.FirstOrDefault(g => g.Id == id);
                if (group == null)
                {
                    group = new PackGroup { Id = id, Title = BaseTitle(pack.Title), Description = pack.Description, ExpectedParts = Math.Max(1, pack.GroupParts) };
                    groups.Add(group);
                }
                group.Local.Add(pack);
                group.InstalledBytes += pack.TotalBytes;
            }
            return groups;
        }
    }

    internal sealed class ReleaseAsset
    {
        public string Id { get; set; }
        public string Group { get; set; }
        public string File { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public long Bytes { get; set; }
        public long InstalledBytes { get; set; }
        public string Sha256 { get; set; }
    }

    internal sealed class ReleaseIndex
    {
        public string Version { get; set; }
        public List<ReleaseAsset> Packages { get; set; }
    }

    // The server packages published with this launcher's GitHub release. File names, sizes and SHA-256s
    // are embedded at build time, so a download is trusted only if it matches them exactly.
    internal static class ServerRelease
    {
        public const string Repository = "BongiornoND/RecToon";
        public const string Tag = "v1.0.0";
        public static string BaseUrl { get { return "https://github.com/" + Repository + "/releases/download/" + Tag + "/"; } }
        public static string DownloadFolder { get { return Path.Combine(InstallPaths.DataDirectory, "downloads", "server-" + Index.Version); } }

        private static ReleaseIndex _index;

        public static ReleaseIndex Index
        {
            get
            {
                if (_index != null) return _index;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecRoomLauncher.server-packages.json"))
                {
                    if (stream == null) throw new InvalidOperationException("The server package list is missing from the launcher.");
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        _index = new JavaScriptSerializer().Deserialize<ReleaseIndex>(reader.ReadToEnd());
                }
                return _index;
            }
        }

        public static ReleaseAsset Core { get { return Index.Packages.First(p => p.Id == "core"); } }

        public static List<PackGroup> Groups()
        {
            var groups = new List<PackGroup>();
            foreach (var asset in Index.Packages.Where(p => p.Id != "core"))
            {
                var id = String.IsNullOrEmpty(asset.Group) ? asset.Id : asset.Group;
                var group = groups.FirstOrDefault(g => g.Id == id);
                if (group == null)
                {
                    group = new PackGroup { Id = id, Title = PackGroup.BaseTitle(asset.Title), Description = asset.Description, ExpectedParts = Index.Packages.Count(p => (String.IsNullOrEmpty(p.Group) ? p.Id : p.Group) == id) };
                    groups.Add(group);
                }
                group.Remote.Add(asset);
                group.InstalledBytes += asset.InstalledBytes;
                group.DownloadBytes += asset.Bytes;
            }
            return groups;
        }

        // Download (resuming partial files) and verify each asset; returns the local paths in order.
        public static List<string> Download(IList<ReleaseAsset> assets, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            Directory.CreateDirectory(DownloadFolder);
            var total = assets.Sum(a => a.Bytes);
            long before = 0;
            var paths = new List<string>();
            foreach (var asset in assets)
            {
                var path = Path.Combine(DownloadFolder, asset.File);
                if (!(File.Exists(path) && new FileInfo(path).Length == asset.Bytes && GameFiles.Sha256(path).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    for (var attempt = 1; ; attempt++)
                    {
                        try
                        {
                            DownloadOne(asset, path, before, total, progress, cancel);
                            break;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { if (attempt >= 4) throw; Thread.Sleep(2000 * attempt); }
                    }
                    progress.Report(new InstallProgress("verify", before + asset.Bytes, total, "Checking " + asset.File));
                    if (!GameFiles.Sha256(path).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(path);
                        throw new InvalidOperationException(asset.File + " did not match the release checksum. It was deleted; try again.");
                    }
                }
                before += asset.Bytes;
                progress.Report(new InstallProgress("download", before, total, asset.File + " ready"));
                paths.Add(path);
            }
            return paths;
        }

        private static void DownloadOne(ReleaseAsset asset, string path, long before, long total, IProgress<InstallProgress> progress, CancellationToken cancel)
        {
            var partial = path + ".partial";
            long have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (have > asset.Bytes) { File.Delete(partial); have = 0; }
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(BaseUrl + Uri.EscapeDataString(asset.File));
            request.UserAgent = "RecRoomCommunityLauncher";
            request.AllowAutoRedirect = true;
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            if (have > 0) request.AddRange(have);
            using (cancel.Register(() => request.Abort()))
            using (var response = (System.Net.HttpWebResponse)request.GetResponse())
            {
                if (have > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0;
                using (var input = response.GetResponseStream())
                using (var output = new FileStream(partial, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    var buffer = new byte[1 << 20];
                    int read;
                    var done = have;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        done += read;
                        progress.Report(new InstallProgress("download", before + done, total, "Downloading " + asset.File));
                    }
                }
            }
            if (new FileInfo(partial).Length != asset.Bytes) throw new IOException("The download of " + asset.File + " ended early.");
            if (File.Exists(path)) File.Delete(path);
            File.Move(partial, path);
        }

        public static void DeleteDownloads()
        {
            try { if (Directory.Exists(DownloadFolder)) Directory.Delete(DownloadFolder, true); }
            catch (Exception) { }
        }
    }

    // ---------- Saved install state ----------

    internal sealed class GameState
    {
        public string Path { get; set; }
        public string Build { get; set; }
        public string ManifestId { get; set; }
        public string Status { get; set; }
        public string Patch { get; set; }
        public string Source { get; set; }
    }

    internal sealed class ServerStateRecord
    {
        public string Choice { get; set; }
        public string Path { get; set; }
        public string Status { get; set; }
    }

    internal sealed class InstallState
    {
        public int SchemaVersion { get; set; }
        public GameState Game { get; set; }
        public ServerStateRecord Server { get; set; }
        public bool SetupComplete { get; set; }
        public string UpdatedAt { get; set; }

        private static string FilePath { get { return Path.Combine(InstallPaths.DataDirectory, "install-state.json"); } }

        public static InstallState Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var state = new JavaScriptSerializer().Deserialize<InstallState>(File.ReadAllText(FilePath));
                return state != null && state.SchemaVersion == 1 ? state : null;
            }
            catch (Exception) { return null; }
        }

        // Atomic write: a crash leaves either the previous state or the new one.
        public void Save()
        {
            SchemaVersion = 1;
            UpdatedAt = DateTime.UtcNow.ToString("o");
            Directory.CreateDirectory(InstallPaths.DataDirectory);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }

        public static InstallState ForExistingGame(string gamePath, string source)
        {
            return new InstallState {
                Game = new GameState { Path = gamePath, Build = ClientBuild.Version, ManifestId = ClientBuild.ManifestId, Status = "ready", Patch = PatchPackage.Label, Source = source },
                Server = new ServerStateRecord { Choice = "undecided", Status = "notInstalled" }
            };
        }

        // An existing, already patched installation (e.g. from before this setup existed).
        public static bool IsPatchedGame(string gamePath)
        {
            if (String.IsNullOrWhiteSpace(gamePath)) return false;
            string ignored;
            return File.Exists(Path.Combine(gamePath, "2026patch.dll")) && File.Exists(Path.Combine(gamePath, "Injector.exe")) &&
                   File.Exists(Path.Combine(gamePath, "RecRoomScreen.bat")) && GameFiles.LooksComplete(gamePath, out ignored);
        }
    }
}
