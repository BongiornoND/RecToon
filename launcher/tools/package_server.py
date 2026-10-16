"""Build RecToon server packages for the launcher's "Install from a server package" setup step.

Usage:
  python package_server.py --root "C:/Users/bongi/RecToon 2026" --out <dir> --version 2026.10.16
         [--packs core,community-rooms,clothing,archive] [--node <node.exe> --node-license <LICENSE>]

Each package is a ZIP with recToon-package.json at its root:
  {format, product, version, id, title, description, requires, totalBytes, files:[{path,size,sha256}]}
Paths are relative to the install root (which contains "Start Server.cmd" and "server/").
The launcher verifies every file's SHA-256 while extracting and refuses anything not listed.

Never packaged: player accounts and progress, chat, outfits, saved images/uploads, presence, per-player
settings and room data, host roles, logs/captures, TLS certificates and keys, the JWT secret, Tachyon
keys, and "-before-" backups. The server regenerates its secret and Tachyon keys on first start; the
launcher generates a fresh TLS certificate during setup.
"""
import argparse, hashlib, json, os, sys, zipfile

PRODUCT = 'RecToon server'
FORMAT = 1
STORE_EXT = {'.bundle', '.png', '.jpg', '.jpeg', '.assetbundle', '.room', '.zip', '.exe'}

CORE_DATA = ['api_endpoints_2026.txt', 'avatar_catalog.json', 'avatar_item_names.json', 'builtin_rooms.json',
             'store-clothing-ids.json', 'gameconfigs.json', 'tachyon.json']
FORBIDDEN_NAMES = {'accounts.json', 'chat.json', 'outfits.json', 'savedoutfits.json', 'savedimages.json', 'presence.json',
                   'settings.json', 'roomplayerdata.json', 'player-room-inventory.json', 'rooms.json', 'visits.json',
                   'leaderboards.json', 'hosts.json', 'secret.key', 'clothing-verified.json', 'localhost.key', 'localhost.crt'}
FORBIDDEN_EXT = {'.pem', '.key', '.jsonl', '.log', '.err'}

START_SERVER = r'''@echo off
rem RecToon 2026 server. Keep this window open while playing.
cd /d "%~dp0server"
set "NODE=node"
if exist "%~dp0runtime\node.exe" (
    set "NODE=%~dp0runtime\node.exe"
) else (
    where node >nul 2>nul || (echo Node.js is not installed. Install it from https://nodejs.org or use a package with the bundled runtime. & pause & exit /b 1)
)
if not exist "certs\localhost.crt" (
    echo The server certificate is missing. Run setup again from the Rec Room Community Launcher.
    pause
    exit /b 1
)
netstat -ano | findstr /r /c:":10443 .*LISTENING" >nul && (
    echo The RecToon 2026 server is already running on https://localhost:10443.
    pause
    exit /b 0
)
title RecToon 2026 server
"%NODE%" server.js
pause
'''

README = '''RecToon 2026 server ({version})
==============================

Installed by the Rec Room Community Launcher. Start the server from the launcher, or
run "Start Server.cmd" and keep its window open while playing.

Packages installed: {packs}

This folder holds your own server's data (accounts, saves, chat) once you play.
The certificate in server/certs and the keys in server/data were generated on this PC
and must not be shared.
'''


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def check_allowed(rel):
    name = rel.rsplit('/', 1)[-1].lower()
    ext = os.path.splitext(name)[1]
    top_level_data = rel.startswith('server/data/') and rel.count('/') == 2
    if (name in FORBIDDEN_NAMES and (top_level_data or rel.count('/') <= 1)) or ext in FORBIDDEN_EXT or '-before-' in name or '/uploads/' in rel or rel.startswith('server/certs/'):
        raise SystemExit('refusing to package private or development file: ' + rel)


def walk(root, sub):
    base = os.path.join(root, sub)
    for dirpath, _, names in os.walk(base):
        for n in sorted(names):
            full = os.path.join(dirpath, n)
            yield os.path.relpath(full, root).replace('\\', '/'), full


def collect(args):
    root = args.root
    server = os.path.join(root, 'server')
    manifest = json.load(open(os.path.join(server, 'data', 'downloadable-clothing.json'), encoding='utf-8'))
    manifest_text = json.dumps(manifest)
    images = sorted(n for n in os.listdir(os.path.join(server, 'data', 'images')) if os.path.isfile(os.path.join(server, 'data', 'images', n)))
    clothing_images = {n for n in images if n in manifest_text}

    packs = {}
    core = []
    for n in sorted(os.listdir(server)):
        full = os.path.join(server, n)
        if os.path.isfile(full) and n.endswith('.js') and '-before-' not in n:
            core.append(('server/' + n, full))
    for n in CORE_DATA:
        core.append(('server/data/' + n, os.path.join(server, 'data', n)))
    for n in images:
        if n not in clothing_images:
            core.append(('server/data/images/' + n, os.path.join(server, 'data', 'images', n)))
    if args.node:
        if not args.node_license:
            raise SystemExit('--node needs --node-license (Node.js must ship with its license text)')
        core.append(('runtime/node.exe', args.node))
        core.append(('runtime/LICENSE.txt', args.node_license))
    packs['core'] = ('RecToon server core', 'Server code, room list and settings. Required.', core)

    community = list(walk(root, 'server/data/ugc')) + list(walk(root, 'server/data/room-inventory'))
    packs['community-rooms'] = ('Community rooms', 'Make It to Midnight, My Little Monsters, Run the Block and Showdown, with their shared room data and inventory items.', community)

    clothing = [('server/data/downloadable-clothing.json', os.path.join(server, 'data', 'downloadable-clothing.json'))]
    clothing += [('server/cdn/clothing/' + f['filename'], os.path.join(server, 'cdn', 'clothing', f['filename'])) for f in manifest['files']]
    clothing += [('server/data/images/' + n, os.path.join(server, 'data', 'images', n)) for n in sorted(clothing_images)]
    packs['clothing'] = ('Clothing library', 'Thousands of archived avatar items with their store pictures. Large download.', clothing)

    packs['archive'] = ('Archived Addressables', 'Archived Rec Room asset catalog (Dec 2025) served from the local CDN.', list(walk(root, 'server/cdn/archive')))
    return packs


PART_LIMIT = 1900 * 1000 * 1000  # GitHub release assets must stay under 2 GiB


def split_parts(files):
    parts, current, size = [], [], 0
    for rel, full in files:
        n = os.path.getsize(full)
        if current and size + n > PART_LIMIT:
            parts.append(current)
            current, size = [], 0
        current.append((rel, full))
        size += n
    if current:
        parts.append(current)
    return parts


def build_pack(pack_id, title, description, files, args):
    parts = split_parts(files)
    if len(parts) == 1:
        return [build(pack_id, title, description, files, args, None, None)]
    return [build('%s-%d' % (pack_id, i + 1), '%s (part %d of %d)' % (title, i + 1, len(parts)), description, part, args,
                  pack_id, len(parts)) for i, part in enumerate(parts)]


def build(pack_id, title, description, files, args, group=None, group_parts=None):
    entries = []
    for rel, full in files:
        check_allowed(rel)
        if not os.path.isfile(full):
            raise SystemExit('missing source file: ' + full)
        entries.append({'path': rel, 'size': os.path.getsize(full), 'sha256': sha256(full)})
    package = {'format': FORMAT, 'product': PRODUCT, 'version': args.version, 'id': pack_id, 'title': title,
               'description': description, 'requires': None if pack_id == 'core' else 'core',
               'group': group or pack_id, 'groupParts': group_parts or 1,
               'totalBytes': sum(e['size'] for e in entries), 'files': entries}
    name = 'RecToon-Server-%s-%s.zip' % (args.version, pack_id)
    out = os.path.join(args.out, name)
    tmp = out + '.partial'
    with zipfile.ZipFile(tmp, 'w', allowZip64=True) as z:
        z.writestr('recToon-package.json', json.dumps(package, indent=1), compress_type=zipfile.ZIP_DEFLATED)
        if pack_id == 'core':
            z.writestr('Start Server.cmd', START_SERVER.replace('\n', '\r\n'), compress_type=zipfile.ZIP_DEFLATED)
            z.writestr('README.txt', README.format(version=args.version, packs='core (+ any packs chosen in setup)'), compress_type=zipfile.ZIP_DEFLATED)
        for (rel, full), entry in zip(files, entries):
            ext = os.path.splitext(rel)[1].lower()
            z.write(full, rel, compress_type=zipfile.ZIP_STORED if ext in STORE_EXT else zipfile.ZIP_DEFLATED)
    os.replace(tmp, out)
    print('%-16s %6d files %10.1f MB -> %s (%.1f MB)' % (pack_id, len(entries), package['totalBytes'] / 2 ** 20, name, os.path.getsize(out) / 2 ** 20), flush=True)
    return {'id': pack_id, 'group': package['group'], 'file': name, 'title': title, 'description': description, 'bytes': os.path.getsize(out),
            'installedBytes': package['totalBytes'], 'files': len(entries), 'sha256': sha256(out)}


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--root', required=True)
    p.add_argument('--out', required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--packs', default='core,community-rooms,clothing,archive')
    p.add_argument('--node')
    p.add_argument('--node-license')
    p.add_argument('--index-only', action='store_true', help='rebuild the package index from existing ZIPs')
    args = p.parse_args()
    os.makedirs(args.out, exist_ok=True)
    if args.index_only:
        prefix = 'RecToon-Server-%s-' % args.version
        ids = sorted(n[len(prefix):-4] for n in os.listdir(args.out) if n.startswith(prefix) and n.endswith('.zip'))
        write_index(args, [describe_zip(args, pid) for pid in ids], replace=True)
        return
    packs = collect(args)
    index = []
    for pack_id in args.packs.split(','):
        title, description, files = packs[pack_id]
        index.extend(build_pack(pack_id, title, description, files, args))
    write_index(args, index)


def describe_zip(args, pack_id):
    name = 'RecToon-Server-%s-%s.zip' % (args.version, pack_id)
    path = os.path.join(args.out, name)
    with zipfile.ZipFile(path) as z:
        package = json.loads(z.read('recToon-package.json'))
    return {'id': pack_id, 'group': package.get('group', pack_id), 'file': name, 'title': package['title'],
            'description': package.get('description', ''), 'bytes': os.path.getsize(path),
            'installedBytes': package['totalBytes'], 'files': len(package['files']), 'sha256': sha256(path)}


GROUP_ORDER = ['core', 'community-rooms', 'clothing', 'archive']


def write_index(args, index, replace=False):
    index_path = os.path.join(args.out, 'RecToon-Server-%s-packages.json' % args.version)
    existing = json.load(open(index_path)) if os.path.exists(index_path) and not replace else {'version': args.version, 'packages': []}
    merged = {e['id']: e for e in existing['packages']}
    for e in index:
        # A rebuilt group replaces all of its previous parts.
        for old_id in [k for k, v in merged.items() if v.get('group', k) == e['group']]:
            if old_id not in {x['id'] for x in index}:
                del merged[old_id]
        merged[e['id']] = e
    def key(e):
        g = e.get('group', e['id'])
        return (GROUP_ORDER.index(g) if g in GROUP_ORDER else 99, e['id'].rsplit('-', 1)[-1].zfill(4) if e['id'] != g else '')
    existing['packages'] = sorted(merged.values(), key=key)
    json.dump(existing, open(index_path, 'w'), indent=1)
    print('index', index_path)


if __name__ == '__main__':
    main()
