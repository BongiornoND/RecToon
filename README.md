# Rec Room offline (build 20231207)

Rec Room's servers shut down on 2026-06-01. This project keeps the December 2023 PC build playable:
a local replacement for RecNet (the REST/SignalR backend), a self-hosted Photon server for rooms and
voice, client patches that point the game at them, and a launcher to host a game or join a friend.

What works: login without passwords (accounts are local, you pick your username), the full avatar
catalog unlocked with outfits saved, your dorm, and the Rec Room Originals (Rec Center, Paintball,
Laser Tag, quests, ...) with other players over LAN or a VPN such as Tailscale.

**No game files are included.** You need your own copy of the build from Steam.

## Setup

1. **Download the build.** In the Steam console (`steam://open/console`):

   ```
   download_depot 471710 471711 9010071090979861071
   ```

   Copy the downloaded files into `original/`, then check them:

   ```bash
   cd original && sha256sum -c ../original.sha256
   ```

2. **Make the patched copy** (`work/` is what the launcher runs):

   ```bash
   cp -r original work
   python -I tools/patch_code.py original work
   python -I tools/patch_privacy.py original work
   python -I tools/patch_hosts.py original work localhost
   ```

3. **Server data** (generated from your copy of the build):

   ```bash
   mkdir -p server/data server/certs
   python -I tools/extract_avatar_items.py original/RecRoom_Data/resources.assets server/data/avatar_catalog.json
   python -I tools/extract_rooms.py original/RecRoom_Data/resources.assets server/data/builtin_rooms.json
   openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj /CN=localhost \
     -keyout server/certs/localhost.key -out server/certs/localhost.crt
   ```

   The certificate is only for the local HTTPS server; the patched client accepts it without it being
   installed anywhere.

4. **Launcher art and metadata (optional).** The launcher looks for `launcher/assets/hero.jpg`,
   `logo.png` and `icon.ico` (Steam library art and the game icon) and falls back to plain colours
   without them. Copying `original/RecRoom_Data/il2cpp_data/Metadata/global-metadata.dat` to
   `launcher/global-metadata.pristine.dat` lets a join-only copy work without `original/`.

## Playing

Double-click **Rec Room Launcher.cmd**.

- **Host & Play** starts the servers on this PC (needs [Node.js](https://nodejs.org)) and launches the
  game. The launcher shows the addresses friends can use. Windows asks whether to let Node.js on the
  network the first time: allow it so others can join.
- **Join & Play** checks the host is reachable, points the game at the host's address and launches it.
  Joining needs only Windows and Steam.

Same network: use the host's LAN address. Over the internet: put both PCs on the same
[Tailscale](https://tailscale.com) (or ZeroTier/Radmin) network and use that address. Every player
needs Steam running; each Steam account gets its own local account on the host.

## Layout

| Path | What |
| --- | --- |
| `server/server.js` | RecNet replacement: name server, auth, accounts, avatars, rooms, matchmaking, SignalR hub (HTTPS on 9443) |
| `photon/` | Photon LoadBalancing server over WebSocket (name server 9093, master 9090, game 9091), Protocol16/18, encryption |
| `launcher/` | Host/Join launcher (`Launcher.ps1`) and the host patcher (`MetaPatcher.cs`) |
| `tools/patch_*.py` | Client patches: server addresses, code patches (TLS, EAC, integrity), privacy (no outside connections) |
| `tools/extract_*.py` | Pull the avatar catalog and built-in rooms out of `resources.assets` |
| `tools/xref.js`, `disasm.py`, `build_index.py`, `fix_metadata.py` | Reverse-engineering helpers (need `analysis/` from Il2CppDumper) |
| `tools/*.ps1` | Launch, screenshot, click and key helpers used while testing |
| `NOTES.md` | Technical notes: client patches, protocols, DTO shapes, findings |

Rec Room is a trademark of Rec Room Inc. This is an unofficial, non-commercial preservation project.
