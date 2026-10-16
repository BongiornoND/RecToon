using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;

namespace RecRoomLauncher
{
    internal static class LocalCdnHosts
    {
        internal const string Alias = "cdn.localhost";
        private static string HostsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts"); }
        }

        // Preserve all existing mappings. Refuse to replace a conflicting entry.
        internal static bool HasMapping(string text)
        {
            bool found = false;
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split('#')[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || !fields.Skip(1).Any(h => h.Equals(Alias, StringComparison.OrdinalIgnoreCase))) continue;
                if (fields[0] != "127.0.0.1") throw new InvalidOperationException("cdn.localhost already has a different hosts-file address. Resolve that entry before local hosting.");
                found = true;
            }
            return found;
        }

        internal static bool IsReady()
        {
            if (!HasMapping(File.ReadAllText(HostsPath))) return false;
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(Alias);
                return addresses.Length > 0 && addresses.All(a => a.Equals(IPAddress.Loopback));
            }
            catch (System.Net.Sockets.SocketException) { return false; }
        }

        internal static void Install()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Administrator access is needed to add the local CDN hostname.");
            }
            string existing = File.ReadAllText(HostsPath);
            if (HasMapping(existing)) return;
            string backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecRoomCommunityLauncher", "backups");
            Directory.CreateDirectory(backupDirectory);
            File.Copy(HostsPath, Path.Combine(backupDirectory, "hosts-before-cdn-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff") + ".txt"));
            File.AppendAllText(HostsPath, "\r\n# Local Rec Room archival CDN\r\n127.0.0.1 cdn.localhost\r\n", Encoding.ASCII);
        }
    }
}
