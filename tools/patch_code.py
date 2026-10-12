"""Apply code patches to GameAssembly.dll.

Always starts from the pristine copy so it is safe to re-run. Each patch checks
the original bytes first, so it fails loudly if pointed at a different build.
Addresses come from analysis/dump/dump.cs (file "Offset" column).

Usage: python -I patch_code.py <original build folder> <work build folder>
"""
import sys
from pathlib import Path

PATCHES = [
    # CBGDHMKNIMI.GIPJGEOHBCA.IsValid (Rec Room's BestHTTP ICertificateVerifyer)
    # -> mov al, 1; ret  so the local server's self-signed cert is accepted.
    ("accept any TLS certificate", 0x5C356A0,
     bytes.fromhex("48 89 5c 24 08 48 89 74"), bytes.fromhex("b0 01 c3")),
    # RecRoom.AntiCheat.EACManager.GenerateChallengeResponse(string)
    # -> mov rax, rcx; ret  (echo the challenge back; EAC isn't running).
    ("skip EAC challenge signing", 0x6455B00,
     bytes.fromhex("48 89 5c 24 08 57 48 83"), bytes.fromhex("48 89 c8 c3")),
    # IKLPFGACPII.JMCKLNABHHJ (file-integrity check manager init, called from BootSequence)
    # -> ret. The check needs a signature from Rec Room's CDN (cdn/sigs/<id>); offline it
    # fails, reports "hile" type 311 and quits the game right after pressing PLAY.
    ("disable file integrity check", 0x1B569B0,
     bytes.fromhex("40 53 48 83 ec 30 80 3d"), bytes.fromhex("c3")),
    # Mono.Unity.UnityTlsContext.VerifyCallback (both overloads) -> 0 (UNITYTLS_X509VERIFY_SUCCESS).
    # .NET HttpClient/WebRequest (room data, metadata, CDN) uses UnityTls, not BestHTTP,
    # and otherwise rejects the server's self-signed cert ("GetRoomData" fails).
    ("accept any TLS certificate (UnityTls, static)", 0x68009C0,
     bytes.fromhex("48 89 5c 24 10 48 89 74"), bytes.fromhex("31 c0 c3")),
    ("accept any TLS certificate (UnityTls)", 0x6800700,
     bytes.fromhex("48 89 5c 24 18 48 89 4c"), bytes.fromhex("31 c0 c3")),
    # Photon.Realtime.AppSettings ctor: Rec Room never sets Protocol, so it stays UDP (0).
    # Make it WebSocket (4) with protocol fallback off, so our Photon server only needs
    # to speak Photon-over-WebSocket. Body: xor edx,edx; [0x30]=1 (UseNameServer);
    # [0x61]=0 (EnableProtocolFallback); [0x69]=1; [0x60]=4 (Protocol); ret
    ("Photon over WebSocket", 0x554D120,
     bytes.fromhex("33 d2 c6 41 30 01 c6 41 61 01 c6 41 69 01 e9"),
     bytes.fromhex("33 d2 c6 41 30 01 c6 41 61 00 c6 41 69 01 c6 41 60 04 c3")),
    # BEBJGBOPJEM.CCELHOLFBPP: count of unacknowledged reliable commands, read by
    # reflection from EnetPeer.sentReliableCommands (UDP only). Over WebSocket the peer
    # isn't an EnetPeer and room load throws; TCP has none pending -> xor eax,eax; ret
    ("no pending reliable commands (WebSocket peer)", 0x559C430,
     bytes.fromhex("48 89 5c 24 08 57 48 83"), bytes.fromhex("31 c0 c3")),
    # DIAGNOSTIC: FBINGFGLKGB.HIMGEMADDLB(Exception) normally hides unknown room-load
    # errors as "Unknown error loading room"; return exception.ToString() (type, message,
    # stack trace) instead. mov rcx,rdx; xor edx,edx; jmp System.Exception::ToString()
    ("show room-load exception details", 0x64002B0,
     bytes.fromhex("48 89 5c 24 18 57 48 83"),
     bytes.fromhex("48 89 d1 31 d2 e9") + (0x52D04C0 - (0x64016B0 + 10)).to_bytes(4, "little", signed=True)),
]

original, work = Path(sys.argv[1]), Path(sys.argv[2])
data = bytearray((original / "GameAssembly.dll").read_bytes())

for name, offset, expect, new in PATCHES:
    actual = bytes(data[offset:offset + len(expect)])
    assert actual == expect, f"{name}: unexpected bytes {actual.hex(' ')} at {offset:#x}"
    data[offset:offset + len(new)] = new
    print(f"{name} @ {offset:#x}")

(work / "GameAssembly.dll").write_bytes(data)
print(f"wrote {work / 'GameAssembly.dll'}")
