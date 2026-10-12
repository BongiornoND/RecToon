"""Point the client at a Rec Room server (local or another PC's IP).

Rec Room's global-metadata.dat has a shuffled header, but the string-literal
table and data are plain. Each table entry is (uint32 length, uint32 dataIndex)
with dataIndex relative to the literal data section. We append replacement
strings to the end of the file and repoint the entries, so new URLs can be any
length.

- RecNet name server + auth  -> https://<host>:9443
- Photon name server         -> <host> (Photon server on the same machine)
- Third-party analytics/crash hosts -> local sink, so nothing leaves the machine

Always patches from the pristine copy so it is safe to re-run.

Usage: python -I patch_hosts.py <original build folder> <work build folder> [host]
       host defaults to localhost
"""
import struct
import sys
from pathlib import Path

META = Path("RecRoom_Data") / "il2cpp_data" / "Metadata" / "global-metadata.dat"
LITERAL_TABLE = (2644112, 448088)  # (offset, size) of the (length, dataIndex) table
LITERAL_DATA_OFFSET = 3092200

original, work = Path(sys.argv[1]), Path(sys.argv[2])
host = sys.argv[3] if len(sys.argv) > 3 else "localhost"
api = f"https://{host}:9443"
# Photon resolves "localhost" to ::1 first; the Photon server listens on IPv4.
photon_host = "127.0.0.1" if host == "localhost" else host

# Literal prefix -> replacement prefix
PREFIXES = {
    "https://ns.rec.net": api,
    "https://auth.rec.net": api + "/auth",
    "https://api2.amplitude.com": api + "/analytics/amplitude",
    "https://recroom-dataplane.rudderstack.com": api + "/analytics/rudderstack",
    "https://statsigapi.net": api + "/analytics/statsig",
    "https://events.backtrace.io": api + "/analytics/backtrace",
    "https://submit.backtrace.io": api + "/analytics/backtrace",
    "https://userreporting.cloud.unity3d.com": api + "/analytics/unity",
}
# Whole-literal replacements
EXACT = {
    "ns.exitgames.com": photon_host,
    "http://ns.exitgames.com:80/photon/n": f"http://{photon_host}:9090/photon/n",
    "http://ns.exitgamescloud.com:80/photon/n": f"http://{photon_host}:9090/photon/n",
}

data = bytearray((original / META).read_bytes())
changed = 0
table_start, table_size = LITERAL_TABLE
for entry in range(table_start, table_start + table_size, 8):
    length, index = struct.unpack_from("<II", data, entry)
    start = LITERAL_DATA_OFFSET + index
    value = bytes(data[start:start + length]).decode("utf-8", "replace")
    replacement = EXACT.get(value)
    if replacement is None:
        for old, new in PREFIXES.items():
            if value.startswith(old):
                replacement = new + value[len(old):]
                break
    if replacement is None:
        continue
    encoded = replacement.encode()
    new_offset = len(data)
    data += encoded
    struct.pack_into("<II", data, entry, len(encoded), new_offset - LITERAL_DATA_OFFSET)
    changed += 1

(work / META).write_bytes(data)
print(f"server host: {host}  ({changed} literals redirected)")
