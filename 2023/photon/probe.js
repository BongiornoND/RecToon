// Log the first traffic the client sends to any Photon port, to learn its transport.
const dgram = require("dgram");
const net = require("net");

for (const port of [5055, 5056, 5057, 5058]) {
  const s = dgram.createSocket("udp4");
  s.on("message", (msg, rinfo) => console.log(`UDP ${port} <- ${rinfo.address}:${rinfo.port} ${msg.length}b ${msg.subarray(0, 64).toString("hex")}`));
  s.bind(port, () => console.log(`udp ${port}`));
}
for (const port of [4530, 4531, 4532, 4533, 9090, 9091, 9092, 9093, 19090, 19093]) {
  net.createServer((sock) => {
    sock.once("data", (d) => console.log(`TCP ${port} <- ${d.length}b ${d.subarray(0, 300).toString("latin1").replace(/[^\x20-\x7e\r\n]/g, ".")}`));
  }).listen(port, () => console.log(`tcp ${port}`));
}
