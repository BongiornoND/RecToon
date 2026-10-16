# RecToon

Play the archived **Rec Room 20260323.14** client again, on a server you host yourself.

RecToon is a Windows launcher with a built-in installer. From one EXE it:

1. downloads the exact supported Rec Room build through **your own Steam client**,
2. checks every game file and applies the community patch,
3. downloads and installs the **RecToon server** (with Node.js, community rooms and the
   archived clothing library) from this repository's release, and
4. starts the server and the game for you.

> Not affiliated with, endorsed by, or supported by Rec Room Inc. Rec Room's official
> services are retired; this project exists for preservation. The server packages contain
> archived Rec Room and community-creator content.

## Requirements

- Windows 10 or 11 (64-bit)
- [Steam](https://store.steampowered.com/), signed in, with Rec Room in your library (it is free)
- About **50 GB** free disk space (game 6.3 GB, server ~19 GB, plus room for downloads while
  installing; the downloaded packages are deleted afterwards)
- An internet connection for the first setup

## Install and play

1. Download **`RecToon-Launcher.exe`** from the [latest release](../../releases/latest) and run it.
2. **Step 1 – Rec Room.** Choose an install folder (the default is fine) and keep
   *Download with Steam* selected. Click **Open Steam console**: Steam opens its console and
   the command is copied for you. In Steam's console, type or paste

   ```
   download_depot 471710 471711 8867414017562658247
   ```

   and press Enter. Keep Steam open; setup continues by itself when the download finishes.
   (If the console does not open, paste `steam://open/console` into your browser's address bar.)
3. **Step 2 – Install and patch.** Every file is checked against Steam's list for this build,
   then the patch is installed. If Windows asks, allow the *Referee* component (Steam normally
   installs it with the game).
4. **Step 3 – Server.** Keep *Install the RecToon server* selected with *Download from the
   RecToon release*, leave all content ticked and click **Finish setup**.
   **You cannot play without a server** – only skip this if you have the address of someone
   else's running RecToon server.
5. In the launcher, click **Start server**, wait for it to come online, then **Start game & join**.
   The first time, Windows asks for administrator access once to add `cdn.localhost` (the local
   clothing download address).

Steam must be running and signed in whenever you play.

## What is in a release

| Asset | Purpose |
| --- | --- |
| `RecToon-Launcher.exe` | Launcher + installer. Contains the patch and the checksums of everything it downloads. |
| `RecToon-Server-<version>-core.zip` | Server code, settings, room list and Node.js runtime (required). |
| `…-community-rooms.zip` | Make It to Midnight, My Little Monsters, Run the Block, Showdown. |
| `…-clothing-1.zip` … `…-clothing-N.zip` | The archived clothing library, split into parts under GitHub's 2 GB limit (installed together). |
| `…-archive.zip` | Archived Rec Room asset catalog served by the local CDN. |
| `RecToon-Server-<version>-packages.json` | Sizes and SHA-256 checksums of the packages. |

You normally only download the launcher; it fetches the packages itself. To install offline,
put the package ZIPs in a `server-packages` folder next to the launcher and choose
*Use server package files already on this PC*.

Every package lists each file's SHA-256, and setup checks them while unpacking. Packages never
contain accounts, saves, logs, certificates or keys: setup creates a new HTTPS certificate for
your PC and the server creates its own secrets on first start.

## Building from source

The launcher is a single .NET Framework 4.x WinForms app compiled with the C# compiler that
ships with Windows:

```
cd launcher
powershell -ExecutionPolicy Bypass -File build.ps1 -OutFile ..\dist\RecToon-Launcher.exe
```

See [`launcher/README.md`](launcher/README.md) for the setup flow, the embedded game-file
manifest (`tools/make_build_manifest.py`) and server packaging (`tools/package_server.py`).

## Credits and licenses

- Rec Room 2026 patch (`launcher/patch/`) by **dukedotdev**, MIT License – see
  [`licenses/patch-2026-MIT.txt`](licenses/patch-2026-MIT.txt).
- Node.js runtime bundled in the server core package – MIT and third-party licenses in
  `runtime/LICENSE.txt` inside that package.
- Rec Room is a trademark of Rec Room Inc. Game files are downloaded from Steam by your own
  Steam client and are not redistributed here.

## Rec Room 2023 build (older project)

The earlier preservation project for the **December 2023** PC build (client 20231207) — a
PowerShell launcher, local RecNet server, Photon server and client patch scripts — now lives in
[`2023/`](2023/README.md). It is independent of the 2026 launcher above.
