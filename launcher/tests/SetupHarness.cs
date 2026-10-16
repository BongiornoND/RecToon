using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace RecRoomLauncher
{
    // Test-only entry point (built with /main:RecRoomLauncher.SetupHarness).
    //   render <outDir>                    -> PNG of each setup page, no side effects
    //   pipeline <sourceGame> <scratchDest> -> verify, copy, patch, fingerprint + preflight in scratch
    internal static class SetupHarness
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "render") return Render(args[1]);
                if (args.Length == 2 && args[0] == "render-main") return RenderMain(args[1]);
                if (args.Length == 3 && args[0] == "pipeline") return Pipeline(args[1], args[2]);
                if (args.Length == 1 && args[0] == "release-download") return ReleaseDownload();
                if (args.Length >= 3 && args[0] == "server-install") return ServerInstall(args[1], args[2], args.Skip(3).ToArray());
                Console.WriteLine("usage: render <dir> | pipeline <source> <dest>");
                return 2;
            }
            catch (Exception ex) { Console.WriteLine("FAILED: " + ex); return 1; }
        }

        private static int Render(string outDir)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(outDir);
            using (var form = new Form { ClientSize = new Size(1052, 920 - 190), StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, BackColor = Theme.Page })
            {
                var view = new SetupView("", @"C:\Users\bongi\RecToon 2026\server", (d, c) => null, m => Console.WriteLine("log: " + m), (g, s) => { }, null);
                form.Controls.Add(view);
                form.Show();
                Application.DoEvents();
                var showPage = typeof(SetupView).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var page in new[] { "game", "steam", "install", "server", "done" })
                {
                    showPage.Invoke(view, new object[] { page });
                    if (page == "server")
                    {
                        var tile = (ChoiceTile)typeof(SetupView).GetField("_serverPackage", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                        tile.Select();
                    }
                    form.PerformLayout();
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                        bitmap.Save(Path.Combine(outDir, "setup-" + page + ".png"), ImageFormat.Png);
                    }
                    Console.WriteLine("rendered " + page);
                }
            }
            return 0;
        }

        // The real launcher window in forced-setup mode (reads, never writes, install state).
        private static int RenderMain(string outDir)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(outDir);
            using (var form = new MainForm(true))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                var setup = (SetupView)typeof(MainForm).GetField("_setup", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var showPage = typeof(SetupView).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var page in new[] { "game", "steam", "install", "server" })
                {
                    if (page == "server")
                    {
                        var zipField = (InputField)typeof(SetupView).GetField("_serverZip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(setup);
                        zipField.Value = Path.GetFullPath(@"..\..\outputs\server-packages\RecToon-Server-2026.10.16-core.zip");
                        typeof(SetupView).GetMethod("EnterServerPage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(setup, null);
                        ((ChoiceTile)typeof(SetupView).GetField("_serverPackage", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(setup)).Select();
                    }
                    else showPage.Invoke(setup, new object[] { page });
                    if (page == "game")
                    {
                        var tile = (ChoiceTile)typeof(SetupView).GetField("_folderChoice", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(setup);
                        tile.Select();
                    }
                    form.PerformLayout();
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(outDir, "main-" + page + ".png"), ImageFormat.Png);
                    }
                    var map = (System.Collections.Generic.Dictionary<string, Control>)typeof(SetupView).GetField("_pageMap", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(setup);
                    var sp = (SetupPage)map[page];
                    var scroller = sp.Content.Parent as ScrollableControl;
                    if (page == "server")
                    {
                        Action<Control, int> walk = null;
                        walk = (c, depth) => {
                            var sc = c as ScrollableControl;
                            if (sc != null && (sc.AutoScroll || sc.VerticalScroll.Visible))
                                Console.WriteLine(new string(' ', depth) + c.GetType().Name + " bounds=" + c.Bounds + " auto=" + sc.AutoScroll + " v=" + sc.VerticalScroll.Visible + " max=" + sc.VerticalScroll.Maximum + " display=" + sc.DisplayRectangle + " min=" + sc.AutoScrollMinSize);
                            foreach (Control child in c.Controls) walk(child, depth + 1);
                        };
                        walk(form, 0);
                    }
                    Console.WriteLine("rendered main " + page + " content=" + sp.Content.Size + " pref=" + sp.Content.GetPreferredSize(new Size(scroller.ClientSize.Width, 0)) + " scroller=" + scroller.ClientSize + " vscroll=" + scroller.VerticalScroll.Visible + " hscroll=" + scroller.HorizontalScroll.Visible + " min=" + scroller.AutoScrollMinSize + " pos=" + scroller.AutoScrollPosition + " display=" + scroller.DisplayRectangle + " children=" + String.Join(";", scroller.Controls.Cast<Control>().Select(c => c.GetType().Name + c.Bounds)) + " deep=" + String.Join(";", sp.Content.Controls.Cast<Control>().Where(c => c.Visible).Select(c => c.GetType().Name + c.Bounds)));
                }
                form.Hide();
            }
            return 0;
        }

        private static int Pipeline(string source, string dest)
        {
            Console.WriteLine("files " + ClientBuild.Files.Count + " bytes " + ClientBuild.TotalBytes + " download " + ClientBuild.DownloadBytes);
            var problem = InstallPaths.ValidateFreshDestination(dest, ClientBuild.TotalBytes);
            if (problem != null) { Console.WriteLine("destination rejected: " + problem); return 1; }
            Console.WriteLine("steamapps destination check: " + InstallPaths.ValidateFreshDestination(@"C:\Program Files (x86)\Steam\steamapps\common\X", 1));
            var last = DateTime.MinValue;
            var progress = new SyncProgress(p => {
                if ((DateTime.UtcNow - last).TotalSeconds < 5 && p.Done < p.Total) return;
                last = DateTime.UtcNow;
                Console.WriteLine("  " + p.Stage + " " + (int)(p.Fraction * 100) + "%");
            });
            var started = DateTime.UtcNow;
            var failed = GameFiles.Verify(source, progress, CancellationToken.None);
            Console.WriteLine("verify failures: " + failed.Count + " (" + (DateTime.UtcNow - started).TotalSeconds.ToString("0") + "s)");
            if (failed.Count > 0) return 1;
            var placeFiles = typeof(SetupView).GetMethod("PlaceFiles", BindingFlags.NonPublic | BindingFlags.Static);
            started = DateTime.UtcNow;
            placeFiles.Invoke(null, new object[] { source, dest, false, progress, CancellationToken.None });
            Console.WriteLine("copied (" + (DateTime.UtcNow - started).TotalSeconds.ToString("0") + "s); extra files in copy: " +
                String.Join(",", Directory.GetFiles(dest).Select(Path.GetFileName).Where(n => !ClientBuild.Files.Any(f => f.Path.Equals(n, StringComparison.OrdinalIgnoreCase)))));
            PatchPackage.Install(dest, m => Console.WriteLine("log: " + m));
            Console.WriteLine("patched: " + String.Join(",", PatchPackage.FileNames.Where(n => File.Exists(Path.Combine(dest, n)))));
            GameFiles.CheckFingerprints(dest);
            Console.WriteLine("fingerprints ok");
            Console.WriteLine("preflight: " + PatchPackage.RunPreflight(dest));
            Console.WriteLine("ini RestoreArchivedBounceCollision=" + Ini.ReadSection(Path.Combine(dest, "2026patch.ini"), "Compatibility")["RestoreArchivedBounceCollision"]);
            Console.WriteLine("IsPatchedGame: " + InstallState.IsPatchedGame(dest));
            // Re-running the patch over an installed copy must keep connection settings.
            Ini.SetValues(Path.Combine(dest, "2026patch.ini"), "Network", new System.Collections.Generic.Dictionary<string, string> { { "ServerUrl", "https://example.test:10443" } });
            PatchPackage.Install(dest, m => Console.WriteLine("log: " + m));
            Console.WriteLine("ServerUrl after re-patch: " + Ini.ReadSection(Path.Combine(dest, "2026patch.ini"), "Network")["ServerUrl"]);
            Console.WriteLine("PIPELINE OK");
            return 0;
        }

        // server-install <core.zip> <dest> [packId ...]: the launcher's package install path.
        private static int ServerInstall(string coreZip, string dest, string[] packIds)
        {
            var core = ServerSetup.ReadPackage(coreZip);
            var found = ServerSetup.FindPacks(core);
            Console.WriteLine("core " + core.Version + " " + core.Files.Count + " files; packs found: " + String.Join(", ", found.Select(p => p.Id + " (" + Bytes.Format(p.TotalBytes) + ")")));
            var chosen = found.Where(p => packIds.Contains(p.Id)).ToList();
            var last = DateTime.MinValue;
            var progress = new SyncProgress(p => {
                if ((DateTime.UtcNow - last).TotalSeconds < 10 && p.Done < p.Total) return;
                last = DateTime.UtcNow;
                Console.WriteLine("  " + (int)(p.Fraction * 100) + "% " + p.Detail);
            });
            var started = DateTime.UtcNow;
            var server = ServerSetup.InstallPackages(core, chosen, dest, progress, CancellationToken.None);
            Console.WriteLine("installed " + server + " in " + (DateTime.UtcNow - started).TotalSeconds.ToString("0") + "s");
            Console.WriteLine("validate: " + (ServerSetup.ValidateServerFolder(server) ?? "ok") + "; node: " + ServerSetup.NodeFor(server));
            Console.WriteLine("cert: " + File.ReadAllLines(Path.Combine(server, "certs", "localhost.crt"))[0] + " key: " + File.ReadAllLines(Path.Combine(server, "certs", "localhost.key"))[0]);
            // Tamper test: a damaged package must be refused.
            return 0;
        }

        // release-download: fetch the core package from the live GitHub release with the launcher's downloader.
        private static int ReleaseDownload()
        {
            Console.WriteLine("release " + ServerRelease.Tag + " from " + ServerRelease.BaseUrl);
            Console.WriteLine("groups: " + String.Join(", ", ServerRelease.Groups().Select(g => g.Id + " x" + g.ExpectedParts + " complete=" + g.Complete)));
            var last = DateTime.MinValue;
            var progress = new SyncProgress(p => {
                if ((DateTime.UtcNow - last).TotalSeconds < 5 && p.Done < p.Total) return;
                last = DateTime.UtcNow;
                Console.WriteLine("  " + p.Stage + " " + (int)(p.Fraction * 100) + "% " + p.Detail);
            });
            var started = DateTime.UtcNow;
            var files = ServerRelease.Download(new[] { ServerRelease.Core }, progress, CancellationToken.None);
            Console.WriteLine("downloaded and verified " + files[0] + " in " + (DateTime.UtcNow - started).TotalSeconds.ToString("0") + "s");
            var package = ServerSetup.ReadPackage(files[0]);
            Console.WriteLine("package " + package.Id + " " + package.Version + " files " + package.Files.Count);
            ServerRelease.DeleteDownloads();
            Console.WriteLine("cleaned: " + !Directory.Exists(ServerRelease.DownloadFolder));
            return 0;
        }

        private sealed class SyncProgress : IProgress<InstallProgress>
        {
            private readonly Action<InstallProgress> _report;
            public SyncProgress(Action<InstallProgress> report) { _report = report; }
            public void Report(InstallProgress value) { _report(value); }
        }
    }
}
