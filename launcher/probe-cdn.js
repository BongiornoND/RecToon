const https = require('https');
const dns = require('dns').promises;

function fetch(url) {
  return new Promise((resolve, reject) => {
    const request = https.get(url, {rejectUnauthorized:false, timeout:4500}, response => {
      const chunks=[]; let bytes=0;
      response.on('data', chunk => {
        bytes += chunk.length;
        if (bytes > 1024*1024) request.destroy(new Error('CDN readiness response is too large'));
        else chunks.push(chunk);
      });
      response.on('error', reject);
      response.on('end', () => {
        if (response.statusCode !== 200) reject(new Error('HTTP ' + response.statusCode));
        else resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));
      });
    });
    request.on('error', reject);
    request.on('timeout', () => request.destroy(new Error('CDN readiness check timed out')));
  });
}

(async () => {
  const discovery = await fetch('https://localhost:10443/');
  const target = new URL(discovery.CDN);
  if (target.protocol !== 'https:' || target.hostname !== 'cdn.localhost')
    throw new Error('The local server must advertise its CDN on https://cdn.localhost');
  const addresses = await dns.lookup(target.hostname, {all:true});
  if (!addresses.length || addresses.some(row => row.address !== '127.0.0.1'))
    throw new Error('cdn.localhost must resolve only to 127.0.0.1');
  const tips = await fetch(target.href.replace(/\/$/, '') + '/config/LoadingScreenTipData');
  if (!Array.isArray(tips)) throw new Error('Invalid CDN readiness response');
  process.stdout.write('HTTPS clothing CDN ready');
})().catch(error => { process.stderr.write(error.message); process.exitCode=1; });
