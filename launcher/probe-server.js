const http = require('http');
const https = require('https');

const origin = process.argv[2];
if (!origin) {
  process.stdout.write(JSON.stringify({ ok: false, error: 'No server URL supplied' }));
  process.exit(2);
}

let target;
try {
  target = new URL('/econ/api/avatar/v4/items', origin);
} catch (error) {
  process.stdout.write(JSON.stringify({ ok: false, error: 'Invalid server URL' }));
  process.exit(2);
}

const client = target.protocol === 'https:' ? https : target.protocol === 'http:' ? http : null;
if (!client) {
  process.stdout.write(JSON.stringify({ ok: false, error: 'Only HTTP and HTTPS URLs are supported' }));
  process.exit(2);
}

// The local RecToon host uses a self-signed localhost certificate. For a remote
// server, keep Node's normal certificate validation so a bad TLS certificate is
// visible to the user before they launch the game.
const loopback = target.hostname === 'localhost' || target.hostname === '127.0.0.1';
const request = client.get(target, { timeout: 4500, rejectUnauthorized: !loopback }, (response) => {
  const chunks = [];
  let size = 0;
  const tlsAuthorized = response.socket ? response.socket.authorized : false;
  response.on('data', (chunk) => {
    size += chunk.length;
    if (size > 8 * 1024 * 1024) request.destroy(new Error('API response was too large'));
    else chunks.push(chunk);
  });
  response.on('end', () => {
    if (response.statusCode !== 200) {
      process.stdout.write(JSON.stringify({ ok: false, status: response.statusCode, error: 'Avatar API returned a non-200 status' }));
      return;
    }
    try {
      const items = JSON.parse(Buffer.concat(chunks).toString('utf8'));
      const count = Array.isArray(items) ? items.length : null;
      process.stdout.write(JSON.stringify({ ok: count !== null, status: response.statusCode, count, authorized: tlsAuthorized }));
      if (count === null) process.exitCode = 1;
    } catch (error) {
      process.stdout.write(JSON.stringify({ ok: false, status: response.statusCode, error: 'Avatar API response was not JSON' }));
      process.exitCode = 1;
    }
  });
});

request.on('timeout', () => request.destroy(new Error('HTTPS/API request timed out')));
request.on('error', (error) => {
  process.stdout.write(JSON.stringify({ ok: false, error: error.code ? error.code + ': ' + error.message : error.message }));
  process.exitCode = 1;
});
