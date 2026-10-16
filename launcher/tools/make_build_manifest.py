"""Write assets/build-20260323.14.manifest (embedded in the launcher) from Steam's depot manifest.

Usage: python make_build_manifest.py <depotcache .manifest> <out file> [--check <game folder>]

Output is UTF-8 text, one line per entry (paths use backslashes, relative to the game folder):
  # header lines
  D<TAB>path
  F<TAB>size<TAB>sha1<TAB>path
--check hashes every listed file in the folder and reports missing/changed files and extra files.
"""
import hashlib, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import steam_manifest

src, out = sys.argv[1], sys.argv[2]
meta, entries = steam_manifest.read(src)
if meta.get('filenamesEncrypted'):
    raise SystemExit('Manifest filenames are encrypted; cannot build a file list')
files = sorted((e for e in entries if not e.get('flags', 0) & steam_manifest.DIRECTORY_FLAG), key=lambda e: e['name'].lower())
dirs = sorted((e['name'] for e in entries if e.get('flags', 0) & steam_manifest.DIRECTORY_FLAG), key=str.lower)
lines = ['# Rec Room client 20260323.14 (Steam app 471710, depot %d, manifest %d)' % (meta['depotId'], meta['manifestId']),
         '# files=%d bytes=%d download=%d' % (len(files), sum(e['size'] for e in files), meta['bytesCompressed'])]
lines += ['D\t' + d for d in dirs]
lines += ['F\t%d\t%s\t%s' % (e['size'], e['sha1'], e['name']) for e in files]
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(lines) + '\n')
print('wrote', out, len(files), 'files')

if '--check' in sys.argv:
    root = sys.argv[sys.argv.index('--check') + 1]
    missing, changed, listed = [], [], set()
    for e in files:
        path = os.path.join(root, e['name']); listed.add(e['name'].lower())
        if not os.path.isfile(path):
            missing.append(e['name']); continue
        h = hashlib.sha1()
        with open(path, 'rb') as f:
            for block in iter(lambda: f.read(1 << 20), b''):
                h.update(block)
        if os.path.getsize(path) != e['size'] or h.hexdigest() != e['sha1']:
            changed.append(e['name'])
    extra = []
    for dirpath, _, names in os.walk(root):
        for n in names:
            rel = os.path.relpath(os.path.join(dirpath, n), root)
            if rel.lower() not in listed:
                extra.append(rel)
    print('missing', len(missing), missing[:20])
    print('changed', len(changed), changed[:20])
    print('extra', len(extra), extra[:40])
