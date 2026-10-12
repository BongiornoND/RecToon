// Photon GpBinaryV16 (Protocol16) serializer: big-endian, one-letter type codes.
// Same interface as protocol18.js (Reader/Writer/parseMessage/operationResponse/eventMessage);
// reading keeps raw bytes so relayed data is forwarded unchanged.
const { keyOf, MSG } = require("./protocol18");

const T = {
  Unknown: 0, Null: 42, Dictionary: 68, StringArray: 97, Byte: 98, Custom: 99, Double: 100,
  EventData: 101, Float: 102, Hashtable: 104, Integer: 105, Short: 107, Long: 108,
  IntegerArray: 110, Boolean: 111, OperationResponse: 112, OperationRequest: 113,
  String: 115, ByteArray: 120, Array: 121, ObjectArray: 122,
};

class Reader {
  constructor(buf, pos = 0) { this.buf = buf; this.pos = pos; }
  byte() { return this.buf[this.pos++]; }
  bytes(n) { const b = this.buf.subarray(this.pos, this.pos + n); this.pos += n; return b; }
  i16() { const v = this.buf.readInt16BE(this.pos); this.pos += 2; return v; }
  i32() { const v = this.buf.readInt32BE(this.pos); this.pos += 4; return v; }
  string() { const n = this.buf.readUInt16BE(this.pos); this.pos += 2; return this.bytes(n).toString("utf8"); }

  typed() {
    const start = this.pos;
    const type = this.byte();
    const value = this.untyped(type);
    return { type, value, raw: this.buf.subarray(start, this.pos) };
  }

  untyped(type) {
    switch (type) {
      case T.Unknown: case T.Null: return null;
      case T.Byte: return this.byte();
      case T.Boolean: return this.byte() !== 0;
      case T.Short: return this.i16();
      case T.Integer: return this.i32();
      case T.Long: { const v = this.buf.readBigInt64BE(this.pos); this.pos += 8; return v; }
      case T.Float: { const v = this.buf.readFloatBE(this.pos); this.pos += 4; return v; }
      case T.Double: { const v = this.buf.readDoubleBE(this.pos); this.pos += 8; return v; }
      case T.String: return this.string();
      case T.ByteArray: { const n = this.i32(); return Buffer.from(this.bytes(n)); }
      case T.IntegerArray: { const n = this.i32(); const a = []; for (let i = 0; i < n; i++) a.push(this.i32()); return a; }
      case T.StringArray: { const n = this.i16(); const a = []; for (let i = 0; i < n; i++) a.push(this.string()); return a; }
      case T.ObjectArray: { const n = this.i16(); const a = []; for (let i = 0; i < n; i++) a.push(this.typed()); return a; }
      case T.Array: {
        const n = this.i16(); const et = this.byte(); const a = [];
        if (et === T.Array) { for (let i = 0; i < n; i++) a.push(this.untyped(T.Array)); return a; }
        if (et === T.Dictionary) { const kt = this.byte(), vt = this.byte(); for (let i = 0; i < n; i++) a.push(this.dictBody(kt, vt)); return a; }
        if (et === T.Custom) { const code = this.byte(); for (let i = 0; i < n; i++) { const len = this.i16(); a.push({ customCode: code, data: Buffer.from(this.bytes(len)) }); } return a; }
        for (let i = 0; i < n; i++) a.push(this.untyped(et));
        return a;
      }
      case T.Hashtable: {
        const n = this.i16(); const m = new Map();
        for (let i = 0; i < n; i++) { const k = this.typed(); const v = this.typed(); m.set(keyOf(k.value), { key: k, value: v }); }
        return m;
      }
      case T.Dictionary: { const kt = this.byte(), vt = this.byte(); return this.dictBody(kt, vt); }
      case T.Custom: { const code = this.byte(); const len = this.i16(); return { customCode: code, data: Buffer.from(this.bytes(len)) }; }
      case T.OperationRequest: { const code = this.byte(); return { code, params: this.paramTable() }; }
      case T.EventData: { const code = this.byte(); return { code, params: this.paramTable() }; }
      case T.OperationResponse: { const code = this.byte(); const rc = this.i16(); const dbg = this.typed(); return { code, returnCode: rc, debug: dbg.value, params: this.paramTable() }; }
      default: throw new Error(`Protocol16: unknown type ${type} at ${this.pos - 1}`);
    }
  }

  dictBody(kt, vt) {
    const n = this.i16(); const m = new Map();
    const anyK = kt === 0 || kt === T.Null, anyV = vt === 0 || vt === T.Null;
    for (let i = 0; i < n; i++) {
      const k = anyK ? this.typed() : { type: kt, value: this.untyped(kt) };
      const v = anyV ? this.typed() : { type: vt, value: this.untyped(vt) };
      m.set(keyOf(k.value), { key: k, value: v });
    }
    return m;
  }

  paramTable() {
    const n = this.i16(); const p = new Map();
    for (let i = 0; i < n; i++) { const k = this.byte(); p.set(k, this.typed()); }
    return p;
  }
}

class Writer {
  constructor() { this.parts = []; }
  byte(b) { this.parts.push(Buffer.from([b & 0xff])); }
  buf(b) { this.parts.push(b); }
  i16(v) { const b = Buffer.alloc(2); b.writeInt16BE(v); this.buf(b); }
  i32(v) { const b = Buffer.alloc(4); b.writeInt32BE(v); this.buf(b); }
  string(s) { const b = Buffer.from(s, "utf8"); this.i16(b.length); this.buf(b); }

  value(v) {
    if (v === null || v === undefined) return this.byte(T.Null);
    if (v.raw) return this.buf(v.raw);
    if (typeof v === "boolean") { this.byte(T.Boolean); return this.byte(v ? 1 : 0); }
    if (typeof v === "bigint") { const b = Buffer.alloc(8); b.writeBigInt64BE(v); this.byte(T.Long); return this.buf(b); }
    if (typeof v === "number") {
      if (Number.isInteger(v)) { this.byte(T.Integer); return this.i32(v); }
      const b = Buffer.alloc(8); b.writeDoubleBE(v); this.byte(T.Double); return this.buf(b);
    }
    if (typeof v === "string") { this.byte(T.String); return this.string(v); }
    if (Buffer.isBuffer(v)) { this.byte(T.ByteArray); this.i32(v.length); return this.buf(v); }
    if (v instanceof Map) return this.hashtable(v);
    if ("byte" in v) { this.byte(T.Byte); return this.byte(v.byte); }
    if ("short" in v) { this.byte(T.Short); return this.i16(v.short); }
    if ("float" in v) { const b = Buffer.alloc(4); b.writeFloatBE(v.float); this.byte(T.Float); return this.buf(b); }
    if ("intArray" in v) { this.byte(T.IntegerArray); this.i32(v.intArray.length); return v.intArray.forEach((i) => this.i32(i)); }
    if ("stringArray" in v) { this.byte(T.StringArray); this.i16(v.stringArray.length); return v.stringArray.forEach((s) => this.string(s)); }
    if ("objectArray" in v) { this.byte(T.ObjectArray); this.i16(v.objectArray.length); return v.objectArray.forEach((x) => this.value(x)); }
    throw new Error(`Protocol16: can't write ${JSON.stringify(v)}`);
  }

  hashtable(m) {
    this.byte(T.Hashtable); this.i16(m.size);
    for (const [k, e] of m) {
      if (e && e.key && e.value && (e.key.raw || "type" in e.key)) { this.typedEntry(e.key); this.typedEntry(e.value); }
      else { this.value(k); this.value(e); }
    }
  }
  typedEntry(t) { if (t.raw) this.buf(t.raw); else this.value(t.value); }

  paramTable(params) {
    const entries = [...params].filter(([, v]) => v !== undefined);
    this.i16(entries.length);
    for (const [k, v] of entries) { this.byte(k); this.value(v); }
  }
  done() { return Buffer.concat(this.parts); }
}

function parseMessage(buf) {
  if (buf[0] !== 0xf3) throw new Error(`bad magic ${buf[0]}`);
  const type = buf[1] & 0x7f;
  const r = new Reader(buf, 2);
  if (type === MSG.Operation || type === MSG.InternalOperation || type === MSG.Event) {
    const code = r.byte();
    return { type, code, params: r.paramTable() };
  }
  return { type, raw: buf.subarray(2) };
}

function operationResponse(code, returnCode, params = new Map(), debug = null, internal = false) {
  const w = new Writer();
  w.byte(0xf3); w.byte(internal ? MSG.InternalOperationResponse : MSG.OperationResponse);
  w.byte(code); w.i16(returnCode); w.value(debug); w.paramTable(params);
  return w.done();
}

function eventMessage(code, params) {
  const w = new Writer();
  w.byte(0xf3); w.byte(MSG.Event); w.byte(code); w.paramTable(params);
  return w.done();
}

module.exports = { T, MSG, Reader, Writer, parseMessage, operationResponse, eventMessage, keyOf };
