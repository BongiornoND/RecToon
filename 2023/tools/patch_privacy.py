"""Stop telemetry that bypasses the game's own (redirected) hosts.

- Unity engine analytics/remote-config hosts live in globalgamemanagers
  (UnityConnectSettings). Rewrite ".cloud.unity3d.com" -> ".cloud.unity3d.inv"
  (same length, so the serialized string lengths stay valid; .inv never resolves).
- Firebase (Google analytics) reads StreamingAssets/google-services-desktop.json.
  Without it Firebase can't initialise, so it sends nothing.

Always works from the pristine copy so it is safe to re-run.

Usage: python -I patch_privacy.py <original build folder> <work build folder>
"""
import sys
from pathlib import Path

original, work = Path(sys.argv[1]), Path(sys.argv[2])

ggm = Path("RecRoom_Data") / "globalgamemanagers"
data = (original / ggm).read_bytes()
old, new = b".cloud.unity3d.com", b".cloud.unity3d.inv"
print(f"globalgamemanagers: {data.count(old)} Unity cloud host(s) -> {new.decode()}")
(work / ggm).write_bytes(data.replace(old, new))

firebase = work / "RecRoom_Data" / "StreamingAssets" / "google-services-desktop.json"
if firebase.exists():
    firebase.rename(firebase.with_name(firebase.name + ".disabled"))
    print("Firebase config disabled")
else:
    print("Firebase config already disabled")
