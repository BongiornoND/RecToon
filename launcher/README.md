# Rec Room Community Launcher

Windows launcher for the Rec Room 20260323.14 patch, styled after the
"RecToon Launcher v3" design (banner art under a custom title bar, cream and
green palette). The window has two panels:

- **Server status** (left): live status of the local RecToon server on
  `https://localhost:10443`, refreshed every 5 seconds. **Start server** runs
  `Start Server.cmd` when nothing is running; **Stop server** ends the `node`
  process listening on port 10443 (it refuses if another program owns the port).
  An activity log sits underneath.
- **Join** (right): changes with the status. When your server is online it shows
  **Start game & join**, which sets up the local clothing CDN if needed, points
  `2026patch.ini` at localhost, and starts the game. When no server is running it
  asks for someone else's server address and **Join server** checks that server,
  writes its profile to `2026patch.ini`, and starts the game.

**Connection options** expand under the address: port, voice (Tachyon) server and
ID, service domain, and voice public key. The port comes from the address if it
names one (`host:port`), otherwise from the Port box, otherwise RecToon's default
10443. The service domain defaults to the address's host.

Game and server folders live under **Settings**.

The remote server operator must supply the discovery URL and service domain.
If they require a Tachyon override, they must also provide its host, transport
ID if applicable, and RSA public key XML. The launcher never asks for a private
key and does not ship a public-server directory.

Local host mode starts the selected `server.js` on this PC and uses
`https://localhost:10443`. Making a local host reachable to other players also
requires network reachability, DNS/domain and TLS configuration, firewall/router
access, and compatible Tachyon/voice settings. The local one-machine check alone
does not prove those external requirements.

## First-run setup (v7: Referee patch)

On a fresh open (no `install-state.json` under `%LOCALAPPDATA%\RecRoomCommunityLauncher`
and no working patched game folder) the launcher shows **Setup** in place of the two
cards:

1. **Rec Room** — choose an empty install folder (default
   `%LOCALAPPDATA%\RecRoomCommunityLauncher\Game`; folders inside Steam's library are
   refused) and a source:
   - *Download with Steam*: the launcher copies `download_depot 471710 471711
     8867414017562658247` and opens Steam's console; the user's own signed-in Steam
     downloads the pinned build. The launcher watches Steam's depot folder and never
     handles Steam credentials.
   - *Use game files I already have*: copy a complete unmodified folder.
   - *I already have a patched copy*: check and re-patch a folder in place (refused under
     `steamapps\common`, where Steam would restore the renamed `Referee.dll`).
2. **Install and patch** — every one of the 3,298 files is checked against Steam's
   manifest for 20260323.14 (embedded as `assets\build-20260323.14.manifest`), files are
   copied through a staging folder (or Steam's fresh download is moved on the same
   drive), then the **Referee patch** is installed (`outputs/Referee-Phase-Handoff.md`):
   - the game's original `Referee.dll` (SHA-256 checked) is renamed `RefereeOriginal.dll`;
   - the community `Referee.dll`, `RefereeBootstrap.dll`, `CommunityPatchCore.dll` and the
     `2026patch.ini` template are written from `referee\` (SHA-256 checked);
   - an older injector overlay (`2026patch.dll`, `Injector.exe`, `RecRoomScreen.bat`,
     `RecRoomVR.bat`) is moved to `%LOCALAPPDATA%\RecRoomCommunityLauncher\backups\patch-*`,
     and existing `[Network]` settings in `2026patch.ini` are kept;
   - if any step fails, the earlier steps are undone.

   The check step then re-hashes the community files, the original alias, GameAssembly and
   UnityPlayer, and confirms no injector files remain. Nothing is executed for this check:
   loading the community `Referee.dll` would run the original's protected startup. If the
   `RefereeClientApp` service is missing, setup offers to run
   `RefereeClientInstaller.exe -install` (what Steam's install script does; needs UAC).
3. **Server** — install a server package (recommended), use an existing server folder,
   or skip. The page warns that **you cannot play without a server**; skipping asks for
   confirmation and is meant only for players who have another running server's address.

Existing users are migrated silently: a working patched folder in settings (either layout)
is recorded as installed and setup is skipped. A missing game folder or unfinished setup
shows Setup again (resuming at the server step when the game is already done).
**Settings → Run setup again…** or `RecRoomCommunityLauncher-v7.exe --setup` opens it on
demand; choosing *I already have a patched copy* on an injector-patched folder converts it.

**Launching.** The launcher detects the folder's layout. A Referee folder is started as
`RecRoom.exe +forcemode:screen` directly; Windows loads the community `Referee.dll`, so
there is no injector or script. The first window can take a while to appear. A folder that
still has the older injector patch keeps launching through its `RecRoomScreen.bat`, and the
checklist suggests running setup again. The launcher no longer embeds or installs the
injector; `outputs\RecRoomCommunityLauncher-v6.exe` is the last injector build.

The Referee patch still needs the original `Referee.dll` (as `RefereeOriginal.dll`) and is
the forwarding prototype, not a standalone replacement; see `referee\README.md` for the
exact payload hashes and the handoff for what has and has not been verified.

Regenerate the build manifest with
`python tools\make_build_manifest.py <depotcache manifest> assets\build-20260323.14.manifest`.

Tests: `obj\build-harness.ps1` builds `tests\SetupHarness.cs`; `SetupHarness.exe
render-main <dir>` renders the real window's setup pages, and `pipeline <game> <dest>` runs
verify → copy → Referee patch → preflight → full re-verify through the alias → injector
conversion → forced-failure rollback into a scratch folder.

## Server packages

`tools/package_server.py` builds the packages setup installs:

```
python tools/package_server.py --root "C:/Users/bongi/RecToon 2026" --out ../../outputs/server-packages --version 2026.10.16
```

| Package | Contents |
| --- | --- |
| `…-core.zip` (required) | `server/*.js` (no `-before-` backups), reference data (room list, catalogs, game configs, Tachyon config), room/catalog pictures, a generated `Start Server.cmd` and README |
| `…-community-rooms.zip` | `data/ugc` (Make It to Midnight, My Little Monsters, Run the Block, Showdown, including their shared cloud-variable ledgers) and `data/room-inventory` |
| `…-clothing.zip` | the 7,426 registered clothing bundles, `downloadable-clothing.json` and their store pictures (~16 GB, stored uncompressed) |
| `…-archive.zip` | the archived Addressables tree under `cdn/archive` |

Every ZIP has `recToon-package.json` listing each file's size and SHA-256. The packager refuses
player data and secrets (accounts, chat, outfits, saved images/uploads, presence, per-player
settings and room data, host roles, logs, certificates, keys, the JWT secret). Setup verifies
each file while extracting, rejects unlisted or escaping paths, installs core plus the chosen
packs through a staging folder, then generates a new self-signed HTTPS certificate
(`localhost`, `cdn.localhost`, `127.0.0.1`). The server creates its own JWT secret and Tachyon
keys on first start.

Setup looks for `server-packages\RecToon-Server-*-core.zip` beside the launcher EXE and offers
the packs found next to the chosen core package. Node.js can be bundled with
`--node <node.exe> --node-license <LICENSE>` (it lands in `runtime
ode.exe`, which
`Start Server.cmd` and the launcher prefer); otherwise Node.js must be installed. The 2026.10.16
core package bundles Node.js 24.18.0 from the official `node-v24.18.0-win-x64.zip` (kept in
`node-runtime\`):

```
python tools/package_server.py --root "C:/Users/bongi/RecToon 2026" --out ../../outputs/server-packages --version 2026.10.16 --packs core --node node-runtime/node.exe --node-license node-runtime/LICENSE
```

## Server settings (add-ons)

The **Server settings** button on the server page opens `ServerSettingsDialog.cs` for the
configured server folder. `ServerAddons.cs` lists what that server has and switches it without
deleting anything:

| Add-on | Off means | Takes effect |
| --- | --- | --- |
| Clothing library | `"enabled": false` in `data/downloadable-clothing.json` (only that value is rewritten) | immediately; the server re-reads the manifest on each clothing request |
| Each community room | `data/ugc/<Room>` moved to `data/ugc-disabled/<Room>` | next server start (the launcher offers to restart); rooms keep their fixed `LocalRoomId` |
| Archived asset catalog | `cdn/archive` renamed to `cdn/archive-disabled` | immediately; the CDN route checks files per request |

Packs the server does not have (for example the clothing library on a server set up without it)
are offered as **Install …**. They are downloaded from the release (resuming, SHA-256 checked),
extracted through `<server root>\.addon-install` with every file verified, and merged in; an
existing file of a different size stops the merge. Free space and the 260-character path limit
are checked first. With clothing turned off, store entries that point at archived clothing still
appear but have nothing to load.

Harness checks: `obj\SetupHarness.exe addons-detect <server>`, `addons-apply <server> clothing=off room:Showdown=off archive=off`,
`addons-install <server> archive` (uses a verified ZIP already in the download folder) and `render-server-settings <server>`.

## Build

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. This uses the Windows
.NET Framework C# compiler and writes `outputs\RecRoomCommunityLauncher-v6.exe` (pass `-OutFile <path>` to build
elsewhere). Images and the icon are embedded from `assets\`. To use the design's
Ubuntu typeface, put `Ubuntu-Regular.ttf`, `Ubuntu-Medium.ttf` and
`Ubuntu-Bold.ttf` in a `fonts\` folder before building; otherwise Segoe UI is used.

