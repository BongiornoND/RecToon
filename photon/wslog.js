// Accept Photon WebSocket connections on likely ports and hex-dump client messages.
const http = require("http");
const crypto = require("crypto");

function parseFrames(buf) {
  const out = [];
  while (buf.length >= 2) {
    const opcode = buf[0] & 0x0f;
    let len = buf[1] & 0x7f, off = 2;
    if (len === 126) { if (buf.length < 4) break; len = buf.readUInt16BE(2); off = 4; }
    else if (len === 127) { if (buf.length < 10) break; len = Number(buf.readBigUInt64BE(2)); off = 10; }
    const masked = buf[1] & 0x80;
    const need = off + (masked ? 4 : 0) + len;
    if (buf.length < need) break;
    const data = Buffer.from(buf.subarray(off + (masked ? 4 : 0), need));
    if (masked) { const m = buf.subarray(off, off + 4); for (let i = 0; i < data.length; i++) data[i] ^= m[i & 3]; }
    out.push({ opcode, data });
    buf = buf.subarray(need);
  }
  return [out, buf];
}

for (const port of [80, 9090, 9091, 9093, 19090, 19093]) {
  const srv = http.createServer((req, res) => { console.log(`HTTP ${port} ${req.method} ${req.url}`); res.end(); });
  srv.on("upgrade", (req, socket) => {
    console.log(`WS ${port} ${req.url} proto=${req.headers["sec-websocket-protocol"]}`);
    const accept = crypto.createHash("sha1").update(req.headers["sec-websocket-key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
    const proto = (req.headers["sec-websocket-protocol"] || "").split(",")[0].trim();
    socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n${proto ? `Sec-WebSocket-Protocol: ${proto}\r\n` : ""}\r\n`);
    let pending = Buffer.alloc(0);
    socket.on("data", (c) => {
      let frames;
      [frames, pending] = parseFrames(Buffer.concat([pending, c]));
      for (const f of frames) console.log(`  ${port} <- op${f.opcode} ${f.data.length}b ${f.data.subarray(0, 200).toString("hex")}`);
    });
    socket.on("error", () => {});
  });
  srv.on("error", (e) => console.log(`port ${port}: ${e.message}`));
  srv.listen(port, "0.0.0.0", () => console.log(`listening ${port}`));
}
