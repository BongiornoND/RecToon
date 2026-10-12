"""Build analysis/symbols.tsv (RVA -> name) from Il2CppDumper's script.json.
Kinds: M = method start, S = string literal slot, T = type/metadata slot, U = method usage slot."""
import json
from pathlib import Path
root = Path(__file__).resolve().parent.parent
s = json.loads((root / "analysis/dump/script.json").read_text(encoding="utf-8"))
base = 0x180000000
rows = []
for m in s.get("ScriptMethod", []): rows.append((m["Address"], "M", m["Name"]))
for m in s.get("ScriptString", []): rows.append((m["Address"], "S", json.dumps(m["Value"])[:200]))
for m in s.get("ScriptMetadata", []): rows.append((m["Address"], "T", m["Name"]))
for m in s.get("ScriptMetadataMethod", []): rows.append((m["Address"], "U", m["Name"]))
with open(root / "analysis/symbols.tsv", "w", encoding="utf-8") as f:
    for a, k, n in rows:
        a = a - base if a >= base else a
        f.write(f"{a:x}\t{k}\t{n}\n")
print(len(rows), "symbols", {k: sum(1 for r in rows if r[1] == k) for k in "MSTU"})
