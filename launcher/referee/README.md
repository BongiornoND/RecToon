# Referee patch payload

`build.ps1` embeds these four files into the launcher. Setup writes them beside `RecRoom.exe` after
renaming the game's original `Referee.dll` to `RefereeOriginal.dll` (see
`outputs/Referee-Phase-Handoff.md`).

| File | Bytes | SHA-256 | Source |
| --- | ---: | --- | --- |
| `Referee.dll` | 171,008 | `d9c05afd93ba10604752d4ddf4d6a2196a0aabb372988e84f54e5df5559bb7ae` | `work/referee-proxy/out/x64/Referee.dll` |
| `RefereeBootstrap.dll` | 109,056 | `1437605baf94fc0e2e50f92addbdff1864a7640272f970fc6473ae715f6186bc` | `work/referee-proxy/out/x64/RefereeBootstrap.dll` |
| `CommunityPatchCore.dll` | 350,720 | `922cc5758d20aeff485fb7e04f8ab2b72de982c8235d9e877250970f75494ba2` | `work/referee-proxy/bin/CommunityPatchCore.dll` |
| `2026patch.ini` | | `90a409aa0f01d17dbe2f86cf00cc39b33b6d6ac612e0db9b37ea7794d334fc56` | `patch/2026patch.ini` (blank `[Network]` defaults; the launcher fills them before each launch) |

These are the development builds recorded in the handoff's section 11. To ship a newer build,
rebuild with `work/referee-proxy/build-core.ps1` and `build-proxy.ps1`, copy the outputs here and
update this table. The community `Referee.dll` still needs the original `Referee.dll`
(SHA-256 `5868c90a…2ead`), which setup keeps as `RefereeOriginal.dll`.
