"""Extract built-in (Rec Room Originals) rooms from AGRoomRuntimeConfig in resources.assets.

usage: python -I tools/extract_rooms.py <RecRoom_Data/resources.assets> <out.json>

AGRoomRuntimeConfig (ScriptableObject):
  Locations[]: Name, ReplicationId (scene/location id), SceneName, RequiredSubSceneNames[],
               LevelRoomSubSceneNames[], MaxPlayers, ReleaseStatus, EmptyOnSandboxClone,
               CustomizationAllowed, SupportedGameMode, GameTeamColorSettings (3 ints),
               RewardGiftContext, LocationEnum
  Rooms[]:     Name, ReplicationId, ReleaseStatus,
               Scenes[]: Name, ReplicationId, RoomSceneLocationId, MaxPlayers, ReleaseStatus
"""
import json
import struct
import sys

data = open(sys.argv[1], "rb").read()
pos = data.find(b"AGRoomRuntimeConfig\x00", data.find(b"AGRoomBuildConfig", 100_000_000) + 1)
assert pos > 0, "AGRoomRuntimeConfig not found"
pos += len("AGRoomRuntimeConfig") + 1  # m_Name, padded to 4
pos += -pos % 4


def u32():
    global pos
    v = struct.unpack_from("<i", data, pos)[0]
    pos += 4
    return v


def string():
    global pos
    n = u32()
    assert 0 <= n < 1000, f"bad string length {n} at {pos - 4}"
    s = data[pos:pos + n].decode("utf8")
    pos += n + (-n % 4)
    return s


def strings():
    return [string() for _ in range(u32())]


locations = []
for _ in range(u32()):
    loc = {"Name": string(), "LocationId": string(), "SceneName": string()}
    strings(), strings()
    loc["MaxPlayers"], loc["ReleaseStatus"], _sandbox, _custom, loc["GameMode"] = (u32() for _ in range(5))
    # GameTeamColorSettings: int, float, then {int team, Color, Color} per team.
    u32(), u32()
    teams = u32()
    pos += teams * 36
    _gift, loc["LocationEnum"] = u32(), u32()
    locations.append(loc)

rooms = []
for _ in range(u32()):
    room = {"Name": string(), "RoomId": string(), "ReleaseStatus": u32(), "Scenes": []}
    for _ in range(u32()):
        room["Scenes"].append({
            "Name": string(), "RoomSceneId": string(), "LocationId": string(),
            "MaxPlayers": u32(), "ReleaseStatus": u32(),
        })
    rooms.append(room)

json.dump({"Locations": locations, "Rooms": rooms}, open(sys.argv[2], "w"), indent=1)
print(f"{len(locations)} locations, {len(rooms)} rooms")
for r in rooms:
    print(f"  {r['Name']:<28} status {r['ReleaseStatus']}  scenes: {', '.join(s['Name'] for s in r['Scenes'])}")
