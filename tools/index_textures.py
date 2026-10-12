"""Index every Texture2D / Sprite name in the build.

usage: .venv/Scripts/python -I tools/index_textures.py <RecRoom_Data> <out.tsv>

Writes one line per object: file, path_id, type, name, width, height.
Scans serialized files (level*, sharedassets*, resources.assets, globalgamemanagers.assets)
and the Addressables bundles under StreamingAssets/aa.
"""
import os
import sys

import UnityPy

root, out_path = sys.argv[1], sys.argv[2]

files = [os.path.join(root, f) for f in os.listdir(root)
         if f.endswith(".assets") or (f.startswith("level") and "." not in f)]
aa = os.path.join(root, "StreamingAssets", "aa", "StandaloneWindows64")
files += sorted(os.path.join(aa, f) for f in os.listdir(aa) if f.endswith(".bundle"))

with open(out_path, "w", encoding="utf8") as out:
    for i, path in enumerate(files):
        rel = os.path.relpath(path, root)
        try:
            env = UnityPy.load(path)
            for obj in env.objects:
                if obj.type.name not in ("Texture2D", "Sprite"):
                    continue
                try:
                    d = obj.read()
                    w = getattr(d, "m_Width", "") or getattr(getattr(d, "m_Rect", None), "width", "")
                    h = getattr(d, "m_Height", "") or getattr(getattr(d, "m_Rect", None), "height", "")
                    out.write(f"{rel}\t{obj.path_id}\t{obj.type.name}\t{d.m_Name}\t{w}\t{h}\n")
                except Exception as e:
                    out.write(f"{rel}\t{obj.path_id}\t{obj.type.name}\t?ERR {e}\t\t\n")
        except Exception as e:
            print(f"skip {rel}: {e}", file=sys.stderr)
        if i % 250 == 0:
            print(f"{i}/{len(files)} {rel}", flush=True)
            out.flush()
print("done")
