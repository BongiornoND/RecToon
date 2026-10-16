"""Parse a Steam depot manifest (depotcache/*.manifest) into a file list with sizes and SHA-1s.

Layout: [magic 0x71F617D0][u32 len][ContentManifestPayload]
        [magic 0x1F4812BE][u32 len][ContentManifestMetadata]
        [magic 0x1B81B817][u32 len][ContentManifestSignature] [end 0x32C415AB]
FileMapping: 1 filename, 2 size, 3 flags, 4 sha_filename, 5 sha_content, 6 chunks, 7 linktarget.
"""
import struct

PAYLOAD, METADATA, SIGNATURE = 0x71F617D0, 0x1F4812BE, 0x1B81B817
DIRECTORY_FLAG = 64


def _varint(buf, pos):
    shift = value = 0
    while True:
        b = buf[pos]; pos += 1
        value |= (b & 0x7F) << shift
        if b < 0x80:
            return value, pos
        shift += 7


def _fields(buf):
    pos = 0
    while pos < len(buf):
        key, pos = _varint(buf, pos)
        number, wire = key >> 3, key & 7
        if wire == 0:
            value, pos = _varint(buf, pos)
        elif wire == 1:
            value = struct.unpack_from('<Q', buf, pos)[0]; pos += 8
        elif wire == 2:
            length, pos = _varint(buf, pos)
            value = buf[pos:pos + length]; pos += length
        elif wire == 5:
            value = struct.unpack_from('<I', buf, pos)[0]; pos += 4
        else:
            raise ValueError('unsupported wire type %d' % wire)
        yield number, value


def read(path):
    data = open(path, 'rb').read()
    sections, pos = {}, 0
    while pos + 8 <= len(data):
        magic, length = struct.unpack_from('<II', data, pos)
        if magic not in (PAYLOAD, METADATA, SIGNATURE):
            break
        sections[magic] = data[pos + 8:pos + 8 + length]
        pos += 8 + length
    meta = {}
    for number, value in _fields(sections[METADATA]):
        meta[{1: 'depotId', 2: 'manifestId', 3: 'creationTime', 4: 'filenamesEncrypted',
              5: 'bytesOriginal', 6: 'bytesCompressed', 7: 'uniqueChunks'}.get(number, number)] = value
    files = []
    for number, value in _fields(sections[PAYLOAD]):
        if number != 1:
            continue
        entry = {}
        for n, v in _fields(value):
            if n == 1: entry['name'] = v.decode('utf-8', 'replace')
            elif n == 2: entry['size'] = v
            elif n == 3: entry['flags'] = v
            elif n == 4: entry['shaName'] = v.hex()
            elif n == 5: entry['sha1'] = v.hex()
            elif n == 7: entry['link'] = v.decode('utf-8', 'replace')
        files.append(entry)
    return meta, files
