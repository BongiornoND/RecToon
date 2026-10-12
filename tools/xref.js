// Find which methods reference a given string literal.
//
// IL2CPP code loads string literals through a pointer slot (address listed in
// analysis/dump/stringliteral.json) using RIP-relative instructions. We scan the
// code sections for any disp32 that resolves to that slot, then map each hit to
// the enclosing method using the RVAs in analysis/dump/dump.cs.
//
// Usage: node xref.js "<exact literal>" [more literals...]
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const dll = fs.readFileSync(path.join(root, "original", "GameAssembly.dll"));
const literals = JSON.parse(fs.readFileSync(path.join(root, "analysis", "dump", "stringliteral.json"), "utf8"));
const IMAGE_BASE = 0x180000000;

// PE sections
const pe = dll.readUInt32LE(0x3c);
const nsec = dll.readUInt16LE(pe + 6);
const optSize = dll.readUInt16LE(pe + 20);
const sections = [];
for (let i = 0; i < nsec; i++) {
  const o = pe + 24 + optSize + i * 40;
  sections.push({
    name: dll.toString("latin1", o, o + 8).replace(/\0+$/, ""),
    va: dll.readUInt32LE(o + 12), rawSize: dll.readUInt32LE(o + 16), raw: dll.readUInt32LE(o + 20),
  });
}
const code = sections.filter((s) => s.name === ".text" || s.name === "il2cpp");

// Method start RVAs from dump.cs, with "Class.Method" names.
const methods = [];
let cls = "";
const lines = fs.readFileSync(path.join(root, "analysis", "dump", "dump.cs"), "utf8").split("\n");
for (let i = 0; i < lines.length; i++) {
  const line = lines[i];
  const c = line.match(/^(?:public |internal |private |protected )?(?:static |sealed |abstract )*(?:class|struct|interface|enum) (\S+)/);
  if (c) cls = c[1];
  const r = line.match(/^\t\/\/ RVA: 0x([0-9A-F]+) /);
  if (r) {
    const sig = (lines[i + 1] || "").trim().match(/([^\s(]+)\(/);
    methods.push({ rva: parseInt(r[1], 16), name: `${cls}.${sig ? sig[1] : "?"}` });
  }
}
methods.sort((a, b) => a.rva - b.rva);
const methodAt = (rva) => {
  let lo = 0, hi = methods.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (methods[mid].rva <= rva) lo = mid; else hi = mid - 1; }
  return methods[lo];
};

// "--json <Class> [depth]": print a DTO's JSON field names (from its generated
// formatter CBFHJMOMLDG<Class>) next to the C# field types, recursing into nested DTOs.
if (process.argv[2] === "--json") {
  const bySlot = new Map(literals.map((l) => {
    const a = Number(BigInt(l.address));
    return [a >= IMAGE_BASE ? a - IMAGE_BASE : a, l.value];
  }));
  const literalsIn = (rva) => {
    const idx = methods.findIndex((m) => m.rva === rva);
    const end = methods.slice(idx + 1).find((m) => m.rva > rva).rva;
    const s = code.find((c) => rva >= c.va && rva < c.va + c.rawSize);
    const out = [];
    for (let v = rva; v < end - 4; v++) {
      const t = v + 4 + dll.readInt32LE(s.raw + (v - s.va));
      if (bySlot.has(t)) out.push(bySlot.get(t));
    }
    return out;
  };
  const classFields = (name) => {
    const i = lines.findIndex((l) => new RegExp(`(class|struct) ${name.replace(/[.$]/g, "\\$&")} `).test(l));
    if (i < 0) return [];
    const fields = [];
    for (let j = i + 1; j < lines.length && !lines[j].startsWith("}"); j++) {
      const f = lines[j].match(/^\t(?:private|public|internal|protected) (?!const|static)(?:readonly )?(.+?) <?\w+>?(?:k__BackingField)?; \/\/ 0x/);
      if (f) fields.push(f[1]);
    }
    return fields;
  };
  const seen = new Set();
  const show = (name, depth, indent) => {
    if (seen.has(name) || depth < 0) return;
    seen.add(name);
    const fi = lines.findIndex((l) => l.includes(`CBFHJMOMLDG<${name}>`));
    if (fi < 0) { console.log(`${indent}${name}: no formatter`); return; }
    const rva = parseInt(lines.slice(fi, fi + 20).join("\n").match(/RVA: 0x([0-9A-F]+)/)[1], 16);
    const names = [...new Set(literalsIn(rva).filter((v) => /^[A-Z]/.test(v)))];
    const types = classFields(name);
    console.log(`${indent}${name} {`);
    names.forEach((n, k) => console.log(`${indent}  ${n}: ${types[k] || "?"}`));
    console.log(`${indent}}`);
    for (const t of types) for (const m of t.matchAll(/\b([A-Z]{11}(?:\.[A-Z]{11})?)\b/g)) show(m[1], depth - 1, indent + "  ");
  };
  show(process.argv[3], Number(process.argv[4] || 2), "");
  process.exit(0);
}

// "--slot <hex RVA>...": methods with a RIP-relative reference to a data slot
// (e.g. a type's TypeInfo pointer from analysis/symbols.tsv).
if (process.argv[2] === "--slot") {
  for (const hex of process.argv.slice(3)) {
    const target = parseInt(hex, 16);
    const found = new Set();
    for (const s of code) {
      for (let p = s.raw; p < s.raw + s.rawSize - 4; p++) {
        const v = s.va + (p - s.raw);
        if (v + 4 + dll.readInt32LE(p) === target) {
          const m = methodAt(v);
          found.add(`${m.name} @0x${m.rva.toString(16)}`);
        }
      }
    }
    console.log(`== references to slot 0x${target.toString(16)}`);
    for (const f of found) console.log("   " + f);
  }
  process.exit(0);
}

// "--calls <hex RVA>...": list methods containing a direct call (E8 rel32) to each RVA.
if (process.argv[2] === "--calls") {
  for (const hex of process.argv.slice(3)) {
    const target = parseInt(hex, 16);
    const found = new Set();
    for (const s of code) {
      for (let p = s.raw; p < s.raw + s.rawSize - 5; p++) {
        if (dll[p] !== 0xe8) continue;
        const v = s.va + (p - s.raw);
        if (v + 5 + dll.readInt32LE(p + 1) === target) {
          const m = methodAt(v);
          found.add(`${m.name} @0x${m.rva.toString(16)} (+0x${(v - m.rva).toString(16)})`);
        }
      }
    }
    console.log(`== callers of 0x${target.toString(16)} (${methodAt(target).name})`);
    for (const f of found) console.log("   " + f);
  }
  process.exit(0);
}

// "--method <hex RVA>": list every string literal the method at that RVA references.
if (process.argv[2] === "--method") {
  const rva = parseInt(process.argv[3], 16);
  const idx = methods.findIndex((m) => m.rva === rva);
  const end = methods.slice(idx + 1).find((m) => m.rva > rva).rva;
  const bySlot = new Map(literals.map((l) => {
    const a = Number(BigInt(l.address));
    return [a >= IMAGE_BASE ? a - IMAGE_BASE : a, l.value];
  }));
  const s = code.find((c) => rva >= c.va && rva < c.va + c.rawSize);
  console.log(`== ${methods[idx].name} 0x${rva.toString(16)}-0x${end.toString(16)}`);
  for (let v = rva; v < end - 4; v++) {
    const p = s.raw + (v - s.va);
    const target = v + 4 + dll.readInt32LE(p);
    if (bySlot.has(target)) console.log(`   +0x${(v - rva).toString(16)}  ${JSON.stringify(bySlot.get(target))}`);
  }
  process.exit(0);
}

for (const wanted of process.argv.slice(2)) {
  // stringliteral.json addresses are RVAs already; accept VAs too.
  const slots = literals.filter((l) => l.value === wanted).map((l) => {
    const a = Number(BigInt(l.address));
    return a >= IMAGE_BASE ? a - IMAGE_BASE : a;
  });
  console.log(`== "${wanted}" slots: ${slots.map((s) => "0x" + s.toString(16)).join(", ") || "none"}`);
  const targets = new Set(slots);
  const found = new Map();
  for (const s of code) {
    for (let p = s.raw; p < s.raw + s.rawSize - 4; p++) {
      const ripNext = s.va + (p - s.raw) + 4;
      if (targets.has(ripNext + dll.readInt32LE(p))) {
        const m = methodAt(s.va + (p - s.raw));
        found.set(m.name + " @0x" + m.rva.toString(16), true);
      }
    }
  }
  for (const k of found.keys()) console.log("   " + k);
}
