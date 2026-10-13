# Rec Room 2023 build: local server notes

Build: app 471710, depot 471711, manifest 9010071090979861071 (client version 20231207).
Unity 2021.3.29f1, IL2CPP (metadata v29), BestHTTP (BouncyCastle TLS), Utf8Json, Photon PUN 2 + Voice.

## How to run
1. `node --watch server/server.js` (HTTPS on 9443, logs to `server/requests.jsonl`, rolled over to
   `requests.1.jsonl` at 50 MB; tokens are redacted)
2. `node photon/server.js` (Photon over WebSocket on 9093/9090/9091; `PHOTON_VERBOSE=1` logs every op)
3. `powershell tools/launch.ps1` (starts `work/RecRoom.exe` desktop mode, log in `logs/`)
4. `tools/play_test.ps1` presses PLAY and shows failing requests; `tools/screenshot.ps1` / `tools/click.ps1` drive the window.

Rebuilding `work/` from `original/`: run `tools/patch_hosts.py`, `tools/patch_code.py`,
`tools/patch_privacy.py` (each takes `<original> <work>`).

## Layout
- `original/` untouched depot copy (checksums in `original.sha256`)
- `work/` patched copy
- `analysis/` fixed metadata + Il2CppDumper output (`dump/dump.cs`, `stringliteral.json`)
- `server/data/` all persistent state: accounts, settings, outfits, rooms, relationships,
  gameconfigs (editable), secret.key (token signing)

## Client patches
- Metadata (patch_hosts.py): ns/auth hosts -> localhost; Amplitude, RudderStack, Statsig,
  Backtrace, Unity user reporting -> local sink. String-literal table: (len, dataIndex) at
  2644112, data at 3092200; new strings are appended and entries repointed.
- GameAssembly.dll (patch_code.py):
  - BestHTTP cert verifier `CBGDHMKNIMI.GIPJGEOHBCA.IsValid` -> true
  - `EACManager.GenerateChallengeResponse` -> echo challenge (EAC not running)
  - File integrity check init `IKLPFGACPII.JMCKLNABHHJ` -> ret (needs CDN signature; quits game)
  - UnityTls `VerifyCallback` (both overloads) -> success, for .NET HttpClient/WebRequest traffic
  - Photon `AppSettings` ctor: Protocol = WebSocket, protocol fallback off
  - `BEBJGBOPJEM.CCELHOLFBPP` (pending reliable commands, EnetPeer-only) -> 0
  - `--diagnostic` only: room-load errors show `exception.ToString()` instead of
    "Unknown error loading room"
- Privacy (patch_privacy.py): Unity cloud hosts in globalgamemanagers -> `.unity3d.inv`;
  Firebase config renamed. Verified: no external TCP connections from the game.
- Launch `RecRoom.exe` directly; `Recroom_Release.exe` is the EAC launcher.

## Metadata header
Header (64 uint32) and section order are shuffled; record layouts are standard v29.
`tools/fix_metadata.py` rebuilds a standard header and resets junk `genericContainerIndex`
on each image's `<Module>` (anti-dump trap). Il2CppDumper needs its 0x50000 version
heuristic patched (Rec Room has 525850 generic methods), see tools/Il2CppDumper.

## Reverse-engineering helpers (tools/xref.js)
- `node xref.js "<literal>"`: methods referencing a string literal
- `node xref.js --method <rva>`: literals a method uses
- `node xref.js --calls <rva>`: direct callers
- `node xref.js --json <Class> [depth]`: JSON field names of a DTO from its Utf8Json
  formatter (names are not in field order; match by nested shapes)
JSON reader quirks: an object with no recognised fields deserializes to null; types are strict.

## Status
- Boot, name server, version check, configs: done
- Login (cached login, EAC, OAuth token + signing key, account/me): done, no passwords
- Post-login (avatar/outfit, settings, presence, friends, chat, hub WebSocket, ConfigSettings): done
- PLAY -> matchmake/dorm -> room data for the dorm (scene 76d98498-..., DORM_ROOM): accepted
- Room sessions: done with the self-hosted Photon server (`photon/`), client patched to Photon
  over WebSocket (see Rooms below for what's verified).
- Presence: logout clears a player's room; a player silent for 5 minutes (no authenticated
  request, heartbeats included) stops counting toward an instance's capacity / in-progress state.

## Built-in scenes (resources.assets, AGRoomRuntimeConfig)
Dorm Room 76d98498-60a1-430c-ab76-b54a29b7a163, Rec Center cbad71af-0831-44d8-b8ef-69edafa841f6,
Legacy Charades 4078dfed-24bb-4db7-863f-578ba48d726b (more in the same asset).

## Branding / texture export
`.venv` has UnityPy + pefile. `analysis/texture_index.tsv` lists every Texture2D/Sprite in the build
(serialized files + Addressables bundles). Logos, icons and splash art are exported to `branding/`
(see `branding/README.md`).

## Avatar items (full catalog unlocked)
- `GET /econ/api/avatar/v4/items` (DTO `NPDFEKIHHDP`: AvatarItemType, AvatarItemDesc, FriendlyName,
  Tooltip, Rarity, TagList, AvatarItemId, IsBaseAvatarItem) lists owned items. Anything worn but
  not listed is a "trial item": `AvatarItemSelection.GOIDPEKBFOE` strips it during
  `PlayerAvatar.Awake`, which then NREs in `PlayerAvatar.MOONKMNCJJJ` and the body never renders.
- Desc string = `<legacy item id>[,<legacy material id>...]` (`GENDMICEHPM`); legacy ids come
  from the `AssetIdMapper_*` JSON text assets in resources.assets (guids or 22-char short ids).
- `tools/extract_avatar_items.py original/RecRoom_Data/resources.assets server/data/avatar_catalog.json`
  scans every serialized `AvatarItem` (AvatarItemData + AvatarItemVisualData) and writes
  1058 items / 3197 color variants; the server serves all of them as owned.
- Friendly names (e.g. "Poindexter") lived on RecNet and aren't in the build. Recreated from archived
  RecNet data and community dumps (`analysis/item_names/SOURCES.md`):
  `python -I tools/build_item_names.py server/data/avatar_catalog.json analysis/item_names server/data/avatar_item_names.json`
  names 1014/1058 items (3150/3197 colour variants). Items no ID-keyed source covers are hand-matched
  in `analysis/item_names/manual_names.json` ("matched" vs "guess"). The rest (Miraculous, Barbie
  Western, Tokyo Machine, team jerseys, UGC bases, internal items) fall back to the derived asset name.

## Rooms (Rec Room Originals)
- `tools/extract_rooms.py original/RecRoom_Data/resources.assets server/data/builtin_rooms.json` parses
  AGRoomRuntimeConfig: 41 Locations (scene ids) and 55 Rooms with their scenes (Paintball has 6 maps).
- Server room ids: Originals 1..55 in config order; sub-room ids 100000 + roomId*100 + scene index;
  dorms 1000 + accountId. `LISTED_RRO` picks what the browser shows; everything is reachable by name.
- Joining: `POST matchmaking/matchmake/room/{roomId}[/{subRoomId}]` -> {ErrorCode, RoomInstance}
  (RoomInstanceType 0 = Public). One shared instance per map; a new one (+1e6 per extra) opens when
  full or when a quest/game marked itself in progress (`PUT roominstance/{id}/inprogress`).
  Also `matchmake/player/{id}` (join a player) and `matchmake/instance/{id}`.
- Room DTOs need `Stats` {CheerCount, FavoriteCount, VisitorCount, VisitCount}; null crashes
  RoomModel.GetCheerCountText and the home page carousels disappear.
- Presence (which instance each player is in) persists in data/presence.json so server restarts
  don't bounce clients out of their room; visits in data/visits.json; leaderboards in
  data/leaderboards.json.
- Verified: Rec Center, Paintball (River), Golden Trophy quest.
