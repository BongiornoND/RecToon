"""Recreate RecNet's avatar item display names (FriendlyName) for every catalog entry.

usage: python -I tools/build_item_names.py <avatar_catalog.json> <analysis/item_names> <out.json>

The names lived on RecNet, not in the build. Sources (analysis/item_names/sources/, see
SOURCES.md there), all keyed by AvatarItemDesc ("<item id>[,<material id>...]"), best first:
  recnet_defaultunlocked_20260601  real RecNet response (Wayback, econ.rec.net, 2026-06-01)
  cannednet_*                      RecNet dumps shipped with CannedNet
  mocha_* / femrec_*               full-inventory RecNet dumps (2025) from 2023 server projects
  rebornrec_avataritems            2016-2020 era names (older wording, last resort)
Then, for descs no source names exactly:
  1. variant: the item's base name + the colour label RecNet used for the same material
     combination on other items ("Headband" + "Red" -> "Headband (Red)")
  2. manual_names.json: hand-matched names for items no ID-keyed source covers
Anything left gets no entry; the server derives a name from the asset name.
"""
import collections
import json
import os
import re
import sys

catalog = json.load(open(sys.argv[1]))
root = sys.argv[2]
SOURCES = [
    "recnet_defaultunlocked_20260601", "cannednet_AvatarItems", "cannednet_storefront3",
    "cannednet_defaultAvatarItems", "mocha_AvatarItemsDefault", "mocha_AvatarItems",
    "femrec_AvatarItems", "rebornrec_avataritems",
]


def norm(desc):
    return ",".join(p for p in desc.split(",") if p)


def clean(name):
    name = " ".join(name.split())
    # A few dump entries have a stray ")" ("Baseball Cap (White))").
    while name.endswith(")") and name.count(")") > name.count("("):
        name = name[:-1]
    return name


def placeholder(name):
    # Server projects fill unknown names with the asset name: "(Jersey_Team)".
    return not name or name.startswith("(") or "_" in name


def pairs(o):
    if isinstance(o, dict):
        if isinstance(o.get("AvatarItemDesc"), str) and isinstance(o.get("FriendlyName"), str):
            yield norm(o["AvatarItemDesc"]), clean(o["FriendlyName"])
        for v in o.values():
            yield from pairs(v)
    elif isinstance(o, list):
        for v in o:
            yield from pairs(v)


known = {}  # desc -> (name, source)
for src in SOURCES:
    data = json.load(open(os.path.join(root, "sources", src + ".json"), encoding="utf-8-sig"))
    for desc, name in pairs(data):
        if not placeholder(name):
            known.setdefault(desc, (name, src))

SUFFIX = re.compile(r"^(.*?) \(([^()]+)\)$")
base_votes = collections.defaultdict(collections.Counter)   # item id -> base names
label_votes = collections.defaultdict(collections.Counter)  # material combo -> labels
for desc, (name, _) in known.items():
    item, _, combo = desc.partition(",")
    m = SUFFIX.match(name)
    base_votes[item][m.group(1) if m else name] += 1
    if m and combo:
        label_votes[combo][m.group(2)] += 1

manual = {k: v for k, v in json.load(open(os.path.join(root, "manual_names.json"))).items() if not k.startswith("_")}

out = {}
for entry in catalog:
    desc = entry["AvatarItemDesc"]
    item, _, combo = desc.partition(",")
    if desc in known:
        name, source = known[desc]
    elif item in base_votes or entry["Name"] in manual:
        if item in base_votes:
            base, source = base_votes[item].most_common(1)[0][0], "variant"
        else:
            base, confidence, _ = manual[entry["Name"]]
            source = "manual-" + confidence
        label = label_votes[combo].most_common(1)[0][0] if combo in label_votes else None
        name = f"{base} ({label})" if label else base
    else:
        continue
    out[desc] = {"FriendlyName": name, "Source": source, "Asset": entry["Name"]}

json.dump(out, open(sys.argv[3], "w", encoding="utf8"), indent=1, ensure_ascii=False)
items = {e["AvatarItemDesc"].split(",")[0] for e in catalog}
named = {d.split(",")[0] for d in out}
print(f"{len(out)}/{len(catalog)} item/material combinations named, {len(named)}/{len(items)} items")
print(dict(collections.Counter(v["Source"] for v in out.values())))
unnamed = sorted({e["Name"] for e in catalog if e["AvatarItemDesc"].split(",")[0] not in named})
print(f"{len(unnamed)} items without a name: {', '.join(unnamed)}")
