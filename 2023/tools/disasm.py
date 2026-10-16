"""Disassemble a GameAssembly.dll method with symbol annotations.

Usage: python tools/disasm.py <hex RVA | Class.Method substring> [max instructions]
Annotates call targets (method names), string literals, type/metadata and
method-usage slots using analysis/symbols.tsv (built by build_index.py).
"""
import bisect
import struct
import sys
from pathlib import Path

import capstone

root = Path(__file__).resolve().parent.parent
dll = (root / "original" / "GameAssembly.dll").read_bytes()
pe = struct.unpack_from("<I", dll, 0x3C)[0]
nsec = struct.unpack_from("<H", dll, pe + 6)[0]
opt = struct.unpack_from("<H", dll, pe + 20)[0]
sections = []
for i in range(nsec):
    o = pe + 24 + opt + i * 40
    vs, va, rs, ra = struct.unpack_from("<4I", dll, o + 8)
    sections.append((va, max(vs, rs), ra))


def rva_to_off(rva):
    for va, size, raw in sections:
        if va <= rva < va + size:
            return raw + rva - va
    return None


syms, methods = {}, []
for line in (root / "analysis" / "symbols.tsv").read_text(encoding="utf-8").splitlines():
    a, k, n = line.split("\t", 2)
    a = int(a, 16)
    syms.setdefault(a, (k, n))
    if k == "M":
        methods.append((a, n))
methods.sort()
starts = [m[0] for m in methods]


def method_at(rva):
    i = bisect.bisect_right(starts, rva) - 1
    return methods[i] if i >= 0 else (0, "?")


arg = sys.argv[1]
if all(c in "0123456789abcdefABCDEFx" for c in arg):
    start = int(arg, 16)
else:
    hits = [m for m in methods if arg in m[1]]
    for h in hits[:20]:
        print(f"{h[0]:x}  {h[1]}")
    if len(hits) != 1:
        sys.exit(0)
    start = hits[0][0]

limit = int(sys.argv[2]) if len(sys.argv) > 2 else 400
i = bisect.bisect_right(starts, start)
end = starts[i] if i < len(starts) else start + 0x2000
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
off = rva_to_off(start)
print(f"== {method_at(start)[1]} @ {start:x}")
for n, ins in enumerate(md.disasm(dll[off:off + (end - start)], start)):
    if n >= limit:
        break
    note = ""
    for op in ins.operands:
        target = None
        if op.type == capstone.x86.X86_OP_IMM and ins.mnemonic in ("call", "jmp") :
            target = op.imm
        elif op.type == capstone.x86.X86_OP_MEM and op.mem.base == capstone.x86.X86_REG_RIP:
            target = ins.address + ins.size + op.mem.disp
        if target is not None:
            if target in syms:
                k, name = syms[target]
                note = f"{k}: {name}"
            elif ins.mnemonic == "call":
                note = f"-> {method_at(target)[1]}"
    print(f"  {ins.address:8x}  {ins.mnemonic:6} {ins.op_str:45} {('; ' + note) if note else ''}")
