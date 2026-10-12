// Photon GpBinaryV18 (Protocol18) serializer, enough for a LoadBalancing server.
//
// Reading keeps each value's raw bytes (`raw`) next to its decoded form, so data we
// only relay (RaiseEvent payloads, custom types) is forwarded byte-for-byte.

const T = {
  Unknown: 0, Boolean: 2, Byte: 3, Short: 4, Float: 5, Double: 6, String: 7, Null: 8,
  CompressedInt: 9, CompressedLong: 10, Int1: 11, Int1_: 12, Int2: 13, Int2_: 14,
  L1: 15, L1_: 16, L2: 17, L2_: 18, Custom: 19, Dictionary: 20, Hashtable: 21,
  ObjectArray: 23, OperationRequest: 24, OperationResponse: 25, EventData: 26,
  BooleanFalse: 27, BooleanTrue: 28, ShortZero: 29, IntZero: 30, LongZero: 31,
  FloatZero: 32, DoubleZero: 33, ByteZero: 34, Array: 64, BooleanArray: 66,
  ByteArray: 67, ShortArray: 68, FloatArray: 69, DoubleArray: 70, StringArray: 71,
  CompressedIntArray: 73, CompressedLongArray: 74, CustomTypeArray: 83,
  DictionaryArray: 84, HashtableArray: 85, CustomTypeSlim: 128,
};

// ---------------------------------------------------------------- reading

class Reader {
  constructor(buf, pos = 0) { this.buf = buf; this.pos = pos; }
  byte() { return this.buf[this.pos++]; }
  bytes(n) { const b = this.buf.subarray(this.pos, this.pos + n); this.pos += n; return b; }
  varuint() {
    let result = 0n, shift = 0n;
    for (;;) {
      const b = this.byte();
      result |= BigInt(b & 0x7f) << shift;
      if (!(b & 0x80)) return result;
      shift += 7n;
    }
  }
  varint32() { const v = Number(this.varuint()); return (v >>> 1) ^ -(v & 1); }
  varint64() { const v = this.varuint(); return (v >> 1n) ^ -(v & 1n); }
  count() { return Number(this.varuint()); }
  string() { const n = this.count(); return this.bytes(n).toString("utf8"); }

  // Typed value: returns { type, value, raw }
  typed() {
    const start = this.pos;
    const type = this.byte();
    const value = this.untyped(type);
    return { type, value, raw: this.buf.subarray(start, this.pos) };
  }

  untyped(type) {
    if (type >= T.CustomTypeSlim) return this.custom(type - T.CustomTypeSlim);
    switch (type) {
      case T.Unknown: case T.Null: return null;
      case T.Boolean: return this.byte() !== 0;
      case T.Byte: return this.byte();
      case T.Short: { const v = this.buf.readInt16LE(this.pos); this.pos += 2; return v; }
      case T.Float: { const v = this.buf.readFloatLE(this.pos); this.pos += 4; return v; }
      case T.Double: { const v = this.buf.readDoubleLE(this.pos); this.pos += 8; return v; }
      case T.String: return this.string();
      case T.CompressedInt: return this.varint32();
      case T.CompressedLong: return this.varint64();
      case T.Int1: return this.byte();
      case T.Int1_: return -this.byte();
      case T.Int2: { const v = this.buf.readUInt16LE(this.pos); this.pos += 2; return v; }
      case T.Int2_: { const v = this.buf.readUInt16LE(this.pos); this.pos += 2; return -v; }
      case T.L1: return BigInt(this.byte());
      case T.L1_: return -BigInt(this.byte());
      case T.L2: { const v = this.buf.readUInt16LE(this.pos); this.pos += 2; return BigInt(v); }
      case T.L2_: { const v = this.buf.readUInt16LE(this.pos); this.pos += 2; return -BigInt(v); }
      case T.Custom: return this.custom(this.byte());
      case T.Dictionary: return this.dictionary();
      case T.Hashtable: return this.hashtable();
      case T.ObjectArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) a.push(this.typed()); return a; }
      case T.OperationRequest: { const code = this.byte(); return { code, params: this.paramTable() }; }
      case T.OperationResponse: {
        const code = this.byte(); const rc = this.buf.readInt16LE(this.pos); this.pos += 2;
        const dbg = this.typed(); return { code, returnCode: rc, debug: dbg.value, params: this.paramTable() };
      }
      case T.EventData: { const code = this.byte(); return { code, params: this.paramTable() }; }
      case T.BooleanFalse: return false;
      case T.BooleanTrue: return true;
      case T.ShortZero: case T.IntZero: case T.FloatZero: case T.DoubleZero: case T.ByteZero: return 0;
      case T.LongZero: return 0n;
      case T.Array: {
        const n = this.count(); const et = this.byte(); const a = [];
        for (let i = 0; i < n; i++) a.push(this.untyped(et));
        return a;
      }
      case T.BooleanArray: {
        const n = this.count(); const bytes = this.bytes(Math.ceil(n / 8)); const a = [];
        for (let i = 0; i < n; i++) a.push((bytes[i >> 3] & (1 << (i & 7))) !== 0);
        return a;
      }
      case T.ByteArray: { const n = this.count(); return Buffer.from(this.bytes(n)); }
      case T.ShortArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) { a.push(this.buf.readInt16LE(this.pos)); this.pos += 2; } return a; }
      case T.FloatArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) { a.push(this.buf.readFloatLE(this.pos)); this.pos += 4; } return a; }
      case T.DoubleArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) { a.push(this.buf.readDoubleLE(this.pos)); this.pos += 8; } return a; }
      case T.StringArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) a.push(this.string()); return a; }
      case T.CompressedIntArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) a.push(this.varint32()); return a; }
      case T.CompressedLongArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) a.push(this.varint64()); return a; }
      case T.CustomTypeArray: {
        const n = this.count(); const code = this.byte(); const a = [];
        for (let i = 0; i < n; i++) { const size = this.count(); a.push({ customCode: code, data: Buffer.from(this.bytes(size)) }); }
        return a;
      }
      case T.DictionaryArray: {
        const n = this.count(); const kt = this.byte(); const vt = this.byte(); const a = [];
        for (let i = 0; i < n; i++) a.push(this.dictionaryBody(kt, vt));
        return a;
      }
      case T.HashtableArray: { const n = this.count(); const a = []; for (let i = 0; i < n; i++) a.push(this.hashtable()); return a; }
      default:
        throw new Error(`Protocol18: unknown type ${type} at ${this.pos - 1}`);
    }
  }

  custom(code) { const size = this.count(); return { customCode: code, data: Buffer.from(this.bytes(size)) }; }

  hashtable() {
    const n = this.count(); const m = new Map();
    for (let i = 0; i < n; i++) { const k = this.typed(); const v = this.typed(); m.set(keyOf(k.value), { key: k, value: v }); }
    return m;
  }

  dictionary() { const kt = this.byte(); const vt = this.byte(); return this.dictionaryBody(kt, vt); }
  dictionaryBody(kt, vt) {
    const n = this.count(); const m = new Map();
    for (let i = 0; i < n; i++) {
      const k = kt === 0 ? this.typed() : { type: kt, value: this.untyped(kt) };
      const v = vt === 0 ? this.typed() : { type: vt, value: this.untyped(vt) };
      m.set(keyOf(k.value), { key: k, value: v });
    }
    return m;
  }

  // Parameter table: count byte, then (key byte, typed value)*
  paramTable() {
    const n = this.byte(); const p = new Map();
    for (let i = 0; i < n; i++) { const k = this.byte(); p.set(k, this.typed()); }
    return p;
  }
}

const keyOf = (v) => (typeof v === "bigint" ? `L${v}` : typeof v === "number" ? `N${v}` : typeof v === "string" ? `S${v}` : `?${String(v)}`);

// ---------------------------------------------------------------- writing

class Writer {
  constructor() { this.parts = []; }
  byte(b) { this.parts.push(Buffer.from([b & 0xff])); }
  buf(b) { this.parts.push(b); }
  varuint(v) {
    v = BigInt(v); const out = [];
    do { let b = Number(v & 0x7fn); v >>= 7n; if (v) b |= 0x80; out.push(b); } while (v);
    this.parts.push(Buffer.from(out));
  }
  varint32(v) { this.varuint(((v << 1) ^ (v >> 31)) >>> 0); }
  varint64(v) { v = BigInt(v); this.varuint(BigInt.asUintN(64, (v << 1n) ^ (v >> 63n))); }
  string(s) { const b = Buffer.from(s, "utf8"); this.varuint(b.length); this.buf(b); }
  short(v) { const b = Buffer.alloc(2); b.writeInt16LE(v); this.buf(b); }

  // Write a JS value with its type code. Supported JS shapes:
  //   null/undefined, boolean, number (int -> CompressedInt, else Double), bigint (long),
  //   string, Buffer (byte[]), Map (Hashtable), {raw} (pre-encoded typed value),
  //   tagged: {byte}, {short}, {float}, {intArray}, {stringArray}, {objectArray}, {dict}
  value(v) {
    if (v === null || v === undefined) return this.byte(T.Null);
    if (v.raw) return this.buf(v.raw);
    if (typeof v === "boolean") return this.byte(v ? T.BooleanTrue : T.BooleanFalse);
    if (typeof v === "bigint") { this.byte(T.CompressedLong); return this.varint64(v); }
    if (typeof v === "number") {
      if (Number.isInteger(v)) { this.byte(T.CompressedInt); return this.varint32(v); }
      const b = Buffer.alloc(8); b.writeDoubleLE(v); this.byte(T.Double); return this.buf(b);
    }
    if (typeof v === "string") { this.byte(T.String); return this.string(v); }
    if (Buffer.isBuffer(v)) { this.byte(T.ByteArray); this.varuint(v.length); return this.buf(v); }
    if (v instanceof Map) return this.hashtable(v);
    if ("byte" in v) { this.byte(T.Byte); return this.byte(v.byte); }
    if ("short" in v) { this.byte(T.Short); return this.short(v.short); }
    if ("float" in v) { const b = Buffer.alloc(4); b.writeFloatLE(v.float); this.byte(T.Float); return this.buf(b); }
    if ("intArray" in v) { this.byte(T.CompressedIntArray); this.varuint(v.intArray.length); return v.intArray.forEach((i) => this.varint32(i)); }
    if ("stringArray" in v) { this.byte(T.StringArray); this.varuint(v.stringArray.length); return v.stringArray.forEach((s) => this.string(s)); }
    if ("objectArray" in v) { this.byte(T.ObjectArray); this.varuint(v.objectArray.length); return v.objectArray.forEach((x) => this.value(x)); }
    throw new Error(`Protocol18: can't write ${JSON.stringify(v)}`);
  }

  // Hashtable from a Map whose entries are either {key, value} (as read) or plain key -> value.
  hashtable(m) {
    this.byte(T.Hashtable); this.varuint(m.size);
    for (const [k, e] of m) {
      if (e && e.key && e.value && (e.key.raw || "type" in e.key)) {
        this.typedEntry(e.key); this.typedEntry(e.value);
      } else {
        this.value(k); this.value(e);
      }
    }
  }
  typedEntry(t) { if (t.raw) this.buf(t.raw); else this.value(t.value); }

  paramTable(params) {
    const entries = [...params].filter(([, v]) => v !== undefined);
    this.byte(entries.length);
    for (const [k, v] of entries) { this.byte(k); this.value(v); }
  }
  done() { return Buffer.concat(this.parts); }
}

// ---------------------------------------------------------------- messages

const MSG = { Init: 0, InitResponse: 1, Operation: 2, OperationResponse: 3, Event: 4, Disconnect: 5, InternalOperation: 6, InternalOperationResponse: 7, Message: 8, RawMessage: 9 };

function parseMessage(buf) {
  if (buf[0] !== 0xf3) throw new Error(`bad magic ${buf[0]}`);
  const type = buf[1] & 0x7f;
  const r = new Reader(buf, 2);
  if (type === MSG.Operation || type === MSG.InternalOperation) {
    const code = r.byte();
    return { type, code, params: r.paramTable() };
  }
  if (type === MSG.Event) { const code = r.byte(); return { type, code, params: r.paramTable() }; }
  return { type, raw: buf.subarray(2) };
}

function operationResponse(code, returnCode, params = new Map(), debug = null, internal = false) {
  const w = new Writer();
  w.byte(0xf3); w.byte(internal ? MSG.InternalOperationResponse : MSG.OperationResponse);
  w.byte(code); w.short(returnCode);
  w.value(debug);
  w.paramTable(params);
  return w.done();
}

function eventMessage(code, params) {
  const w = new Writer();
  w.byte(0xf3); w.byte(MSG.Event); w.byte(code);
  w.paramTable(params);
  return w.done();
}

module.exports = { T, MSG, Reader, Writer, parseMessage, operationResponse, eventMessage, keyOf };
