"""Export branding textures (logos, splash images, brand icons) to PNG.

usage: .venv/Scripts/python -I tools/export_branding.py <RecRoom_Data> <texture_index.tsv> <out_dir>

Picks entries from the index built by tools/index_textures.py by name, skips material maps
(normal/spec/emission) and gear that merely carries a logo, and drops pixel-identical duplicates.
Rec Room / Against Gravity marks go to core/, logos of in-game modes and events to game_and_event/.
"""
import collections
import csv
import hashlib
import os
import re
import sys

import UnityPy

root, index_path, out_dir = sys.argv[1:4]

WANT = re.compile(
    r"logo|splash|wordmark|brand|^icon_RR|RecRoomFace|^Illustration_|RecRoomGallery"
    r"|rec room picaso|CreatorHubBanner|RRCA_Banner|RECtv|RecRoom",
    re.I)
SKIP = re.compile(r"_norm|_spec|_emit|_nrm|_rough|_metal|Roomba|Sword|Shield|Belt|Hat_|TuckedTee", re.I)
CORE = re.compile(r"recroom|rec_room|rec room|splash|^icon_RR|RRplus|RRLogo|agdev|AGLogo|REC_ROOM", re.I)

by_file = collections.defaultdict(list)
with open(index_path, encoding="utf8") as f:
    for rel, path_id, typ, name, *_ in csv.reader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
        if WANT.search(name) and not SKIP.search(name):
            by_file[rel].append((int(path_id), typ, name))

seen, written = set(), 0
for rel, wanted in sorted(by_file.items()):
    env = UnityPy.load(os.path.join(root, rel))
    objs = {o.path_id: o for o in env.objects}
    for path_id, typ, name in wanted:
        try:
            img = objs[path_id].read().image
        except Exception as e:
            print(f"fail {rel} {name}: {e}", file=sys.stderr)
            continue
        digest = hashlib.md5(img.tobytes()).hexdigest()
        if digest in seen:
            continue
        seen.add(digest)
        sub = "core" if CORE.search(name) else "game_and_event"
        os.makedirs(os.path.join(out_dir, sub), exist_ok=True)
        safe = re.sub(r"[^\w+\-]", "_", name)
        dest = os.path.join(out_dir, sub, f"{safe}.png")
        n = 2
        while os.path.exists(dest):
            dest = os.path.join(out_dir, sub, f"{safe}_{n}.png")
            n += 1
        img.save(dest)
        written += 1
        print(f"{sub}/{os.path.basename(dest)}  {img.width}x{img.height}  ({typ}, {rel})")
print(f"wrote {written}")
