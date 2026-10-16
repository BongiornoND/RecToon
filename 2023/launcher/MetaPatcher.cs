// Points the client at a Rec Room server by rewriting string literals in global-metadata.dat
// (same as tools/patch_hosts.py, so joining needs nothing but Windows).
//
// The literal table is (uint32 length, uint32 dataIndex) pairs, dataIndex relative to the
// literal data section. Replacements are appended to the end of the file and the entries
// repointed, so new strings can be any length. Always starts from the pristine copy.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class MetaPatcher
{
    const int TableStart = 2644112, TableSize = 448088, DataOffset = 3092200;

    public static int Patch(string pristinePath, string outputPath, string host)
    {
        string api = "https://" + host + ":9443";
        // Photon resolves "localhost" to ::1 first; the Photon server listens on IPv4.
        string photon = host == "localhost" ? "127.0.0.1" : host;
        var prefixes = new List<KeyValuePair<string, string>> {
            new KeyValuePair<string, string>("https://ns.rec.net", api),
            new KeyValuePair<string, string>("https://auth.rec.net", api + "/auth"),
            new KeyValuePair<string, string>("https://api2.amplitude.com", api + "/analytics/amplitude"),
            new KeyValuePair<string, string>("https://recroom-dataplane.rudderstack.com", api + "/analytics/rudderstack"),
            new KeyValuePair<string, string>("https://statsigapi.net", api + "/analytics/statsig"),
            new KeyValuePair<string, string>("https://events.backtrace.io", api + "/analytics/backtrace"),
            new KeyValuePair<string, string>("https://submit.backtrace.io", api + "/analytics/backtrace"),
            new KeyValuePair<string, string>("https://userreporting.cloud.unity3d.com", api + "/analytics/unity"),
        };
        var exact = new Dictionary<string, string> {
            { "ns.exitgames.com", photon },
            { "http://ns.exitgames.com:80/photon/n", "http://" + photon + ":9090/photon/n" },
            { "http://ns.exitgamescloud.com:80/photon/n", "http://" + photon + ":9090/photon/n" },
        };

        byte[] data = File.ReadAllBytes(pristinePath);
        var appended = new MemoryStream();
        var utf8 = new UTF8Encoding(false);
        int changed = 0;
        for (int entry = TableStart; entry < TableStart + TableSize; entry += 8)
        {
            int length = BitConverter.ToInt32(data, entry);
            int index = BitConverter.ToInt32(data, entry + 4);
            string value = utf8.GetString(data, DataOffset + index, length);
            string replacement;
            if (!exact.TryGetValue(value, out replacement))
            {
                replacement = null;
                foreach (var p in prefixes)
                {
                    if (value.StartsWith(p.Key, StringComparison.Ordinal))
                    {
                        replacement = p.Value + value.Substring(p.Key.Length);
                        break;
                    }
                }
            }
            if (replacement == null) continue;
            byte[] encoded = utf8.GetBytes(replacement);
            long newOffset = data.Length + appended.Length;
            appended.Write(encoded, 0, encoded.Length);
            Array.Copy(BitConverter.GetBytes(encoded.Length), 0, data, entry, 4);
            Array.Copy(BitConverter.GetBytes((int)(newOffset - DataOffset)), 0, data, entry + 4, 4);
            changed++;
        }

        using (var output = File.Create(outputPath))
        {
            output.Write(data, 0, data.Length);
            appended.WriteTo(output);
        }
        return changed;
    }
}
