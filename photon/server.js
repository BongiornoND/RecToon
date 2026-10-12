// Self-hosted Photon server for the Rec Room 2023 client (Photon over WebSocket,
// GpBinaryV18). Implements the LoadBalancing flow the client uses:
//   name server (9093) -> master (9090) -> game server (9091)
// Rooms, actors, properties, cached events and event relay are handled here;
// event payloads are forwarded byte-for-byte.
const http = require("http");
const crypto = require("crypto");
const P = require("./protocol18");
const P16 = require("./protocol16");

const PORTS = { nameserver: 9093, master: 9090, game: 9091 };
const VERBOSE = process.env.PHOTON_VERBOSE === "1";
const log = (...a) => console.log(new Date().toISOString().slice(11, 23), ...a);

// Photon codes
const OP = { Authenticate: 230, AuthenticateOnce: 231, JoinLobby: 229, LeaveLobby: 228, CreateGame: 227, JoinGame: 226, JoinRandomGame: 225, Leave: 254, RaiseEvent: 253, SetProperties: 252, GetProperties: 251, ChangeGroups: 248, FindFriends: 222, GetLobbyStats: 221, GetRegions: 220, WebRpc: 219, ServerSettings: 218, GetGameList: 217 };
const PC = { Address: 230, UserId: 225, Secret: 221, Region: 210, RoomName: 255, ActorNr: 254, TargetActorNr: 253, ActorList: 252, Properties: 251, Broadcast: 250, PlayerProperties: 249, GameProperties: 248, Cache: 247, ReceiverGroup: 246, Data: 245, Code: 244, Group: 240, Add: 238, Remove: 239, ExpectedValues: 231, IsInactive: 233, JoinMode: 215, MasterClientId: 203, NickName: 202, Cluster: 196 };
const EV = { Join: 255, Leave: 254, PropertiesChanged: 253 };
const GPK = { MaxPlayers: 255, IsVisible: 254, IsOpen: 253, PlayerCount: 252, Removed: 251, CleanupCacheOnLeave: 249, MasterClientId: 248 };
const ACTOR = { NickName: 255, IsInactive: 254, UserId: 253 };
const ERR = { GameDoesNotExist: 32758, GameFull: 32765, GameClosed: 32764 };
const CACHE = { DoNotCache: 0, MergeCache: 1, ReplaceCache: 2, RemoveCache: 3, AddToRoomCache: 4, AddToRoomCacheGlobal: 5, RemoveFromRoomCache: 6, RemoveFromCacheForActorsLeft: 7 };

// ------------------------------------------------------------ encoding helpers
const enc = (C, v) => { const w = new C.Writer(); w.value(v); return w.done(); };
const entry = (C, k, v) => ({ key: { raw: enc(C, k) }, value: { raw: enc(C, v) } });
const keyOfRaw = (C, raw) => C.keyOf(new C.Reader(raw).typed().value);
function setEntry(C, map, k, v) { const e = entry(C, k, v); map.set(keyOfRaw(C, e.key.raw), e); }

// ------------------------------------------------------------ minimal WebSocket
function wsAccept(req, socket) {
  const accept = crypto.createHash("sha1").update(req.headers["sec-websocket-key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
  const proto = (req.headers["sec-websocket-protocol"] || "").split(",")[0].trim();
  socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n${proto ? `Sec-WebSocket-Protocol: ${proto}\r\n` : ""}\r\n`);
  socket.setNoDelay(true);
}
function wsFrame(payload) {
  const len = payload.length;
  let head;
  if (len < 126) head = Buffer.from([0x82, len]);
  else if (len < 65536) { head = Buffer.alloc(4); head[0] = 0x82; head[1] = 126; head.writeUInt16BE(len, 2); }
  else { head = Buffer.alloc(10); head[0] = 0x82; head[1] = 127; head.writeBigUInt64BE(BigInt(len), 2); }
  return Buffer.concat([head, payload]);
}
function wsParse(buf) {
  const out = [];
  while (buf.length >= 2) {
    const fin = buf[0] & 0x80, opcode = buf[0] & 0x0f;
    let len = buf[1] & 0x7f, off = 2;
    if (len === 126) { if (buf.length < 4) break; len = buf.readUInt16BE(2); off = 4; }
    else if (len === 127) { if (buf.length < 10) break; len = Number(buf.readBigUInt64BE(2)); off = 10; }
    const masked = buf[1] & 0x80;
    const need = off + (masked ? 4 : 0) + len;
    if (buf.length < need) break;
    const data = Buffer.from(buf.subarray(off + (masked ? 4 : 0), need));
    if (masked) { const m = buf.subarray(off, off + 4); for (let i = 0; i < data.length; i++) data[i] ^= m[i & 3]; }
    out.push({ fin, opcode, data });
    buf = buf.subarray(need);
  }
  return [out, buf];
}

// ------------------------------------------------------------ peers
let nextPeerId = 1;
class Peer {
  constructor(role, socket, req) {
    this.id = nextPeerId++;
    this.role = role;
    this.socket = socket;
    this.host = (req.headers.host || "127.0.0.1").split(":")[0];
    this.url = req.url;
    this.app = new URL(req.url, "http://x").searchParams.get("app") || "";
    this.C = /GpBinaryV16/i.test(req.headers["sec-websocket-protocol"] || "") ? P16 : P;
    this.userId = null;
    this.room = null;
    this.actorNr = 0;
    this.groups = new Set();
    this.fragments = [];
    this.closed = false;
    this.pings = 0;
  }
  send(buf) { if (!this.closed) this.socket.write(wsFrame(buf)); }
  respond(code, rc = 0, params = new Map(), debug = null) {
    if (VERBOSE) log(`  [${this.role}#${this.id}] -> resp op ${code} rc ${rc}`);
    this.send(this.C.operationResponse(code, rc, params, debug));
  }
  event(code, params) { this.send(this.C.eventMessage(code, params)); }
  addr(role) { return `ws://${this.host}:${PORTS[role]}`; }
  tag() { return `[${this.role}#${this.id}${this.C === P16 ? " v16" : ""}${this.userId ? " " + this.userId : ""}]`; }
}

// ------------------------------------------------------------ rooms
const rooms = new Map();
class Room {
  constructor(name, key, C) {
    this.name = name;
    this.key = key;
    this.C = C;
    this.actors = new Map();      // actorNr -> Peer
    this.actorProps = new Map();  // actorNr -> Map(props)
    this.props = new Map();       // room props (raw entries)
    this.nextActor = 1;
    this.master = 0;
    this.cache = [];              // { actorNr, code, params }
    this.maxPlayers = 0;
    this.cleanupCacheOnLeave = true;
  }
  others(nr) { return [...this.actors.values()].filter((p) => p.actorNr !== nr); }
  actorList() { return [...this.actors.keys()]; }
  gameProps() {
    const m = new Map(this.props);
    setEntry(this.C, m, { byte: GPK.PlayerCount }, { byte: this.actors.size });
    setEntry(this.C, m, { byte: GPK.MasterClientId }, this.master);
    return m;
  }
  allActorProps() {
    const m = new Map();
    for (const nr of this.actors.keys()) m.set(nr, this.actorProps.get(nr) || new Map());
    return m;
  }
}

function applyProps(target, incoming) {
  for (const [k, e] of incoming) {
    if (e.value.value === null) target.delete(k);
    else target.set(k, e);
  }
}

// ------------------------------------------------------------ operation handlers
function handleOperation(peer, msg) {
  const p = msg.params;
  const val = (k) => (p.has(k) ? p.get(k).value : undefined);
  if (VERBOSE) log(`${peer.tag()} op ${msg.code} keys=[${[...p.keys()].join(",")}]`);

  switch (msg.code) {
    case OP.Authenticate:
    case OP.AuthenticateOnce: {
      const secret = val(PC.Secret);
      const fromToken = typeof secret === "string" && secret.startsWith("secret-") ? secret.slice(7) : null;
      peer.userId = val(PC.UserId) || fromToken || peer.userId || `user-${peer.id}`;
      const out = new Map([[PC.UserId, peer.userId], [PC.Secret, `secret-${peer.userId}`]]);
      if (peer.role === "nameserver") { out.set(PC.Address, peer.addr("master")); out.set(PC.Cluster, "default"); }
      log(`${peer.tag()} authenticated (${peer.role})`);
      return peer.respond(msg.code, 0, out);
    }
    case OP.GetRegions:
      return peer.respond(msg.code, 0, new Map([
        [PC.Region, { stringArray: ["us"] }],
        [PC.Address, { stringArray: [`${peer.host}:${PORTS.master}`] }],
      ]));
    case OP.JoinLobby: case OP.LeaveLobby: case OP.ChangeGroups:
      if (msg.code === OP.ChangeGroups) {
        const rem = val(PC.Remove), add = val(PC.Add);
        if (Array.isArray(rem)) rem.forEach((g) => peer.groups.delete(g)); else if (rem !== undefined) peer.groups.clear();
        if (Array.isArray(add)) add.forEach((g) => peer.groups.add(g));
      }
      return peer.respond(msg.code, 0);
    case OP.FindFriends: case OP.GetLobbyStats: case OP.WebRpc: case OP.ServerSettings: case OP.GetGameList:
      return peer.respond(msg.code, 0);
    case OP.CreateGame: case OP.JoinGame: case OP.JoinRandomGame:
      return peer.role === "game" ? joinRoom(peer, msg) : matchmake(peer, msg);
    case OP.Leave: leaveRoom(peer, true); return peer.respond(msg.code, 0);
    case OP.RaiseEvent: return raiseEvent(peer, msg);
    case OP.SetProperties: return setProperties(peer, msg);
    case OP.GetProperties: {
      const room = peer.room;
      if (!room) return peer.respond(msg.code, -1, new Map(), "not in a room");
      return peer.respond(msg.code, 0, new Map([[PC.GameProperties, room.gameProps()], [PC.PlayerProperties, room.allActorProps()]]));
    }
    default:
      log(`${peer.tag()} unhandled op ${msg.code} -> ok`);
      return peer.respond(msg.code, 0);
  }
}

// Master: pick/confirm a room name and send the client to the game server.
function matchmake(peer, msg) {
  let name = msg.params.has(PC.RoomName) ? msg.params.get(PC.RoomName).value : null;
  if (msg.code === OP.JoinRandomGame) {
    const r = [...rooms.values()].find((x) => x.key.startsWith(`${peer.app}|`));
    name = r ? r.name : null;
  }
  if (!name) name = crypto.randomUUID();
  log(`${peer.tag()} matchmake op ${msg.code} -> room "${name}"`);
  peer.respond(msg.code, 0, new Map([[PC.Address, peer.addr("game")], [PC.RoomName, name]]));
}

// Game server: create or join the room for real.
function joinRoom(peer, msg) {
  const p = msg.params;
  const name = p.has(PC.RoomName) ? p.get(PC.RoomName).value : crypto.randomUUID();
  const key = `${peer.app}|${peer.C === P16 ? 16 : 18}|${name}`;
  let room = rooms.get(key);
  const creating = !room;
  if (!room) {
    if (msg.code === OP.JoinGame && !(p.has(PC.JoinMode) && p.get(PC.JoinMode).value)) {
      // JoinMode 0 = only join existing; still create so offline-first flows work.
    }
    room = new Room(name, key, peer.C);
    rooms.set(key, room);
    if (p.has(PC.GameProperties)) {
      const gp = p.get(PC.GameProperties).value;
      if (gp instanceof Map) applyProps(room.props, gp);
    }
  }
  if (room.maxPlayers && room.actors.size >= room.maxPlayers) return peer.respond(msg.code, ERR.GameFull, new Map(), "Game full");

  if (peer.room && peer.room !== room) leaveRoom(peer, false);
  const nr = room.nextActor++;
  peer.room = room;
  peer.actorNr = nr;
  room.actors.set(nr, peer);
  const props = new Map();
  if (p.has(PC.PlayerProperties)) {
    const pp = p.get(PC.PlayerProperties).value;
    if (pp instanceof Map) applyProps(props, pp);
  }
  if (peer.userId) setEntry(peer.C, props, { byte: ACTOR.UserId }, peer.userId);
  room.actorProps.set(nr, props);
  if (!room.master) room.master = nr;
  log(`${peer.tag()} ${creating ? "created" : "joined"} room "${name}" as actor ${nr} (${room.actors.size} in room)`);

  peer.respond(msg.code, 0, new Map([
    [PC.ActorNr, nr],
    [PC.GameProperties, room.gameProps()],
    [PC.PlayerProperties, room.allActorProps()],
    [PC.ActorList, { intArray: room.actorList() }],
    [PC.RoomName, name],
  ]));

  // Everyone (including the joiner) gets EvJoin.
  const join = new Map([[PC.ActorNr, nr], [PC.PlayerProperties, props], [PC.ActorList, { intArray: room.actorList() }]]);
  for (const other of room.actors.values()) other.event(EV.Join, join);

  // Replay cached events to the newcomer.
  for (const c of room.cache) peer.event(c.code, c.params);
}

function leaveRoom(peer, explicit) {
  const room = peer.room;
  if (!room) return;
  const nr = peer.actorNr;
  room.actors.delete(nr);
  room.actorProps.delete(nr);
  peer.room = null;
  peer.actorNr = 0;
  if (room.cleanupCacheOnLeave) room.cache = room.cache.filter((c) => c.actorNr !== nr);
  if (room.master === nr) room.master = room.actorList()[0] || 0;
  log(`${peer.tag()} left room "${room.name}" (actor ${nr}, ${room.actors.size} left)${explicit ? "" : " [disconnect]"}`);
  const ev = new Map([[PC.ActorNr, nr], [PC.ActorList, { intArray: room.actorList() }], [PC.MasterClientId, room.master]]);
  for (const other of room.actors.values()) other.event(EV.Leave, ev);
  if (room.actors.size === 0) { rooms.delete(room.key); log(`room "${room.name}" closed`); }
}

function raiseEvent(peer, msg) {
  const p = msg.params;
  const room = peer.room;
  const code = p.has(PC.Code) ? Number(p.get(PC.Code).value) : 0;
  const cache = p.has(PC.Cache) ? Number(p.get(PC.Cache).value) : 0;
  if (!room) return; // RaiseEvent has no response unless it fails; drop silently.

  const evParams = new Map([[PC.ActorNr, peer.actorNr]]);
  if (p.has(PC.Data)) evParams.set(PC.Data, p.get(PC.Data));

  if (cache === CACHE.RemoveFromRoomCache) {
    const actors = p.has(PC.ActorList) ? p.get(PC.ActorList).value : null;
    const data = p.has(PC.Data) ? p.get(PC.Data).value : null;
    room.cache = room.cache.filter((c) => {
      if (code && c.code !== code) return true;
      if (Array.isArray(actors) && actors.length && !actors.includes(c.actorNr)) return true;
      if (data instanceof Map && data.size) {
        const cd = c.params.get(PC.Data) && c.params.get(PC.Data).value;
        if (!(cd instanceof Map)) return true;
        for (const [k, e] of data) { const ce = cd.get(k); if (!ce || !ce.value.raw || !e.value.raw || !ce.value.raw.equals(e.value.raw)) return true; }
      }
      return false;
    });
    return;
  }
  if (cache === CACHE.RemoveFromCacheForActorsLeft) {
    room.cache = room.cache.filter((c) => c.actorNr === 0 || room.actors.has(c.actorNr));
    return;
  }
  if (cache === CACHE.AddToRoomCache) room.cache.push({ actorNr: peer.actorNr, code, params: evParams });
  if (cache === CACHE.AddToRoomCacheGlobal) room.cache.push({ actorNr: 0, code, params: evParams });

  // Receivers: explicit actor list, else receiver group (0 others, 1 all, 2 master).
  let targets;
  if (p.has(PC.ActorList) && Array.isArray(p.get(PC.ActorList).value)) {
    const list = p.get(PC.ActorList).value;
    targets = list.map((nr) => room.actors.get(nr)).filter(Boolean);
  } else {
    const group = p.has(PC.ReceiverGroup) ? Number(p.get(PC.ReceiverGroup).value) : 0;
    if (group === 1) targets = [...room.actors.values()];
    else if (group === 2) targets = [room.actors.get(room.master)].filter(Boolean);
    else targets = room.others(peer.actorNr);
  }
  const interest = p.has(PC.Group) ? Number(p.get(PC.Group).value) : 0;
  if (interest) targets = targets.filter((t) => t.groups.has(interest));
  for (const t of targets) t.event(code, evParams);
}

function setProperties(peer, msg) {
  const p = msg.params;
  const room = peer.room;
  if (!room) return peer.respond(msg.code, -1, new Map(), "not in a room");
  const props = p.has(PC.Properties) ? p.get(PC.Properties).value : new Map();
  const target = p.has(PC.ActorNr) ? Number(p.get(PC.ActorNr).value) : 0;
  if (target) {
    const ap = room.actorProps.get(target);
    if (ap && props instanceof Map) applyProps(ap, props);
  } else if (props instanceof Map) {
    applyProps(room.props, props);
  }
  peer.respond(msg.code, 0);
  const ev = new Map([[PC.TargetActorNr, target], [PC.Properties, props instanceof Map ? props : new Map()], [PC.ActorNr, peer.actorNr]]);
  for (const t of room.actors.values()) t.event(EV.PropertiesChanged, ev);
}

// ------------------------------------------------------------ encryption
// Photon's DiffieHellmanCryptoProvider: Oakley group 1 (768-bit), generator 22, public
// keys as big-endian bytes; AES key = SHA256(shared secret, big-endian, no leading zeros);
// AES-256-CBC with an all-zero IV and PKCS7 padding.
const OAKLEY_768 = Buffer.from(
  "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22" +
  "514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6" +
  "F44C42E9A63A3620FFFFFFFFFFFFFFFF", "hex");
const stripZeros = (b) => { let i = 0; while (i < b.length - 1 && b[i] === 0) i++; return b.subarray(i); };

function exchangeKeys(peer, clientKey) {
  const dh = crypto.createDiffieHellman(OAKLEY_768, Buffer.from([22]));
  dh.generateKeys();
  const shared = stripZeros(dh.computeSecret(stripZeros(Buffer.from(clientKey))));
  peer.aesKey = crypto.createHash("sha256").update(shared).digest();
  return stripZeros(dh.getPublicKey());
}
const ZERO_IV = Buffer.alloc(16);
function decrypt(peer, data) {
  const d = crypto.createDecipheriv("aes-256-cbc", peer.aesKey, ZERO_IV);
  return Buffer.concat([d.update(data), d.final()]);
}

// ------------------------------------------------------------ internal ops (ping / time)
function handleInternal(peer, msg) {
  if (msg.code === 0) {
    // InitEncryption / key exchange: param 1 = client public key.
    const clientKey = msg.params.has(1) ? msg.params.get(1).value : null;
    if (!Buffer.isBuffer(clientKey)) { log(`${peer.tag()} key exchange without key`); return; }
    const serverKey = exchangeKeys(peer, clientKey);
    log(`${peer.tag()} encryption established`);
    peer.send(peer.C.operationResponse(0, 0, new Map([[1, serverKey]]), null, true));
    return;
  }
  if (msg.code === 1) {
    // Ping: echo client time (1) and send server time (2). For WebSocket peers the
    // first response also completes the connection on the client.
    const clientTime = msg.params.has(1) ? msg.params.get(1).value : 0;
    const serverTime = Number(process.hrtime.bigint() / 1000000n) & 0x7fffffff;
    peer.send(peer.C.operationResponse(1, 0, new Map([[1, Number(clientTime)], [2, serverTime]]), null, true));
    return;
  }
  log(`${peer.tag()} unhandled internal op ${msg.code}`);
  peer.send(peer.C.operationResponse(msg.code, 0, new Map(), null, true));
}

// ------------------------------------------------------------ servers
for (const [role, port] of Object.entries(PORTS)) {
  const srv = http.createServer((req, res) => { res.writeHead(426); res.end("Photon WebSocket endpoint"); });
  srv.on("upgrade", (req, socket) => {
    wsAccept(req, socket);
    const peer = new Peer(role, socket, req);
    log(`${peer.tag()} connected ${req.url} (${req.headers["sec-websocket-protocol"]})`);
    let pending = Buffer.alloc(0);
    socket.on("data", (chunk) => {
      if (VERBOSE && peer.pings === 0 && !peer.sawData) { peer.sawData = true; log(`${peer.tag()} first bytes ${chunk.subarray(0, 32).toString("hex")}`); }
      let frames;
      [frames, pending] = wsParse(Buffer.concat([pending, chunk]));
      for (const f of frames) {
        if (f.opcode === 0x8) { socket.end(); return; }
        if (f.opcode === 0x9) { socket.write(Buffer.concat([Buffer.from([0x8a, f.data.length]), f.data])); continue; }
        if (f.opcode === 0xa) continue;
        peer.fragments.push(f.data);
        if (!f.fin) continue;
        let data = Buffer.concat(peer.fragments);
        peer.fragments = [];
        try {
          if (data[0] === 0xf3 && (data[1] & 0x80)) {
            if (!peer.aesKey) throw new Error("encrypted message before key exchange");
            data = Buffer.concat([Buffer.from([0xf3, data[1] & 0x7f]), decrypt(peer, data.subarray(2))]);
          }
          const msg = peer.C.parseMessage(data);
          if (VERBOSE && !(msg.type === P.MSG.InternalOperation && msg.code === 1 && peer.pings++ > 2)) log(`${peer.tag()} <- type ${msg.type} code ${msg.code} ${data.length}b ${data.subarray(0, 24).toString("hex")}`);
          if (msg.type === P.MSG.Operation) handleOperation(peer, msg);
          else if (msg.type === P.MSG.InternalOperation) handleInternal(peer, msg);
          else log(`${peer.tag()} unhandled message type ${msg.type}: ${data.subarray(0, 40).toString("hex")}`);
        } catch (e) {
          log(`${peer.tag()} error: ${e.message} in ${data.subarray(0, 64).toString("hex")}`);
        }
      }
    });
    const close = () => {
      if (peer.closed) return;
      peer.closed = true;
      leaveRoom(peer, false);
      log(`${peer.tag()} disconnected`);
    };
    socket.on("close", close);
    socket.on("error", close);
  });
  srv.on("error", (e) => log(`${role} port ${port}: ${e.message}`));
  srv.listen(port, "0.0.0.0", () => log(`photon ${role} listening on ws://0.0.0.0:${port}`));
}
