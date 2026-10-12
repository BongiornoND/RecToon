"""Extract the full avatar item catalog from resources.assets.

usage: python -I tools/extract_avatar_items.py <RecRoom_Data/resources.assets> <out.json>

Every serialized RecRoom.Avatars.Data.Runtime.AvatarItem (AvatarItemWardrobeRuntimeConfig's
allPossibleCombinations and friends) is an AvatarItemData followed by an AvatarItemVisualData:

  AvatarItemData:        Name (string), AvatarItemGuid (legacy id string: a guid or a 22-char
                         short id), avatarItemId (16-byte modern Guid), OutfitType (int), ...
  AvatarItemVisualData:  ...asset references..., prefabGuid (string), avatarItemId (16 bytes,
                         the prefab's id), combinationGuid (legacy material ids joined by
                         commas), materialId0..2 (16-byte modern ids)

The server describes an item as "<legacy item id>[,<legacy material id>...]" (GENDMICEHPM),
with legacy ids from the AssetIdMapper_* text assets in the same file.
"""
import json
import re
import struct
import sys
import uuid

data = open(sys.argv[1], "rb").read()


def mapper(name):
    start = data.find(b"{", data.find(name.encode()))
    end = data.find(b"\n}", data.find(b"\n  }", start) + 3) + 2
    return json.loads(data[start:end].decode("utf8"))["modernToLegacy"]


item_legacy = mapper("AssetIdMapper_AvatarItemId")
material_legacy = mapper("AssetIdMapper_MaterialId")
combination_legacy = set(mapper("AssetIdMapper_CombinationId").values())

# A length-prefixed legacy id: 36-char guid or 22-char short id (padded to 24 bytes).
ID_RE = re.compile(
    rb"(?:\x24\x00\x00\x00([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})"
    rb"|\x16\x00\x00\x00([A-Za-z0-9_\-]{22})\x00\x00)"
)
COMBO_CHARS = re.compile(rb"^[A-Za-z0-9_,\-]*$")
ZERO = bytes(16)


def read_string(pos):
    n = struct.unpack_from("<I", data, pos)[0]
    if n > 400:
        return None, pos
    s = data[pos + 4:pos + 4 + n]
    return s, pos + 4 + n + (-n % 4)


def guid_at(pos):
    return str(uuid.UUID(bytes_le=data[pos:pos + 16]))


def find_visual(start):
    """Legacy material ids of the AvatarItemVisualData following an AvatarItemData.

    Its avatarItemId is the item's prefab id (not always the item's own id), then
    combinationGuid, then materialId0..2. The string must match the materials, which
    rules out false matches."""
    for p in range(start, start + 4000, 4):
        if data[p:p + 16] == ZERO or guid_at(p) not in item_legacy:
            continue
        combo, pos = read_string(p + 16)
        if combo is None or not COMBO_CHARS.match(combo):
            continue
        materials = [guid_at(pos + 16 * i) for i in range(3) if data[pos + 16 * i:pos + 16 * (i + 1)] != ZERO]
        if not all(mat in material_legacy for mat in materials):
            continue
        legacy = [material_legacy[mat] or mat for mat in materials]
        if combo.decode() in (",".join(legacy), ",".join(materials)):
            return legacy
    return None


catalog = {}
for m in ID_RE.finditer(data):
    legacy_id = (m.group(1) or m.group(2)).decode()
    after = m.end()
    modern = guid_at(after)
    # AvatarItemData.avatarItemId (modern id) follows and must map to the same legacy id.
    if modern not in item_legacy or (item_legacy[modern] or modern) != legacy_id:
        continue
    # Name is the length-prefixed string right before the id string.
    name = None
    for back in range(8, 80, 4):
        n = struct.unpack_from("<I", data, m.start() - back)[0]
        if n and n + 4 + (-n % 4) == back:
            name = data[m.start() - back + 4:m.start() - back + 4 + n].decode("utf8", "replace")
            break
    # AvatarItemData starts with its Name. An id + guid without one is the visual
    # data's prefabGuid (preceded by asset references), not an item.
    if not name or re.fullmatch(r"[0-9a-f]{32}", name):
        continue
    name = name.strip("()")
    outfit_type = struct.unpack_from("<i", data, after + 16)[0]
    # Every item can be worn in its default material ("<item id>" alone).
    catalog.setdefault(legacy_id, {
        "AvatarItemDesc": legacy_id, "Name": name, "OutfitType": outfit_type,
        "ItemGuid": modern, "KnownCombination": True,
    })
    legacy_mats = find_visual(after + 16)
    if not legacy_mats:
        continue
    desc = legacy_id + "," + ",".join(legacy_mats)
    catalog.setdefault(desc, {
        "AvatarItemDesc": desc, "Name": name, "OutfitType": outfit_type, "ItemGuid": modern,
        "KnownCombination": ",".join(legacy_mats) in combination_legacy,
    })

items = sorted(catalog.values(), key=lambda e: (e["Name"], e["AvatarItemDesc"]))
json.dump(items, open(sys.argv[2], "w"), indent=1)
unique_items = {e["ItemGuid"] for e in items}
print(f"{len(items)} item/material combinations covering {len(unique_items)} of {len(item_legacy) - 1} item ids")
print(f"{sum(not e['KnownCombination'] for e in items)} combinations not in AssetIdMapper_CombinationId")
