"""Rebuild .ico files from an exe's RT_GROUP_ICON resources.

usage: .venv/Scripts/python -I tools/extract_exe_icons.py <out_dir> <exe>...
"""
import os
import struct
import sys

import pefile

out_dir = sys.argv[1]
os.makedirs(out_dir, exist_ok=True)

for exe in sys.argv[2:]:
    pe = pefile.PE(exe)
    icons, groups = {}, []
    for rtype in getattr(pe, "DIRECTORY_ENTRY_RESOURCE", None).entries:
        for entry in rtype.directory.entries:
            lang = entry.directory.entries[0]
            blob = pe.get_data(lang.data.struct.OffsetToData, lang.data.struct.Size)
            if rtype.id == pefile.RESOURCE_TYPE["RT_ICON"]:
                icons[entry.id] = blob
            elif rtype.id == pefile.RESOURCE_TYPE["RT_GROUP_ICON"]:
                groups.append((entry.id or entry.name, blob))
    base = os.path.splitext(os.path.basename(exe))[0]
    for gid, blob in groups:
        _, _, count = struct.unpack_from("<HHH", blob)
        entries = [struct.unpack_from("<BBBBHHIH", blob, 6 + 14 * i) for i in range(count)]
        header = struct.pack("<HHH", 0, 1, count)
        offset = 6 + 16 * count
        dirs, images = b"", b""
        for w, h, colors, res, planes, bits, size, icon_id in entries:
            img = icons[icon_id]
            dirs += struct.pack("<BBBBHHII", w, h, colors, res, planes, bits, len(img), offset)
            images += img
            offset += len(img)
        path = os.path.join(out_dir, f"{base}_icon_{gid}.ico")
        open(path, "wb").write(header + dirs + images)
        sizes = ", ".join(f"{w or 256}x{h or 256}" for w, h, *_ in entries)
        print(f"{path}: {sizes}")
