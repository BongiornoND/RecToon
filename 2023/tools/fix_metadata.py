"""Rebuild a standard v29 header for Rec Room's global-metadata.dat.

Rec Room shuffles the 64 header fields and the order of sections in the file,
and overwrites the sanity/version values. The record layouts themselves are
standard v29, so writing a correct header is enough for Il2CppDumper.

Each section below was identified by content/cross-reference (see NOTES.md).
Output is for analysis only; the game keeps using its own file.

Usage: python -I fix_metadata.py <in global-metadata.dat> <out file>
"""
import struct
import sys

SANITY, VERSION = 0xFAB11BAF, 29

# (offset, size) in v29 header order
SECTIONS = [
    (2644112, 448088),    # stringLiteral
    (3092200, 1801476),   # stringLiteralData
    (8937388, 4940372),   # string
    (24771064, 38472),    # events
    (33295448, 1146880),  # properties
    (13877760, 9494784),  # methods
    (24677680, 93384),    # parameterDefaultValues
    (29602808, 464100),   # fieldDefaultValues
    (256, 367520),        # fieldAndParameterDefaultValueData
    (23581076, 144156),   # fieldMarshaledSizes
    (30066908, 3228540),  # parameters
    (509288, 2134824),    # fields
    (23383972, 197104),   # genericParameters
    (23725232, 15016),    # genericParameterConstraints
    (26679280, 111888),   # genericContainers
    (27157812, 75540),    # nestedTypes
    (4893676, 74912),     # interfaces
    (27246392, 2335552),  # vtableMethods
    (26799308, 358504),   # interfaceOffsets
    (4968588, 3968800),   # typeDefinitions
    (27233352, 13040),    # images
    (29581944, 20864),    # assemblies
    (34614096, 11656),    # fieldRefs
    (23372544, 11428),    # referencedAssemblies
    (24809536, 1869744),  # attributeData
    (23740248, 937432),   # attributeDataRange
    (34442328, 171768),   # unresolvedVirtualCallParameterTypes
    (367776, 141512),     # unresolvedVirtualCallParameterRanges
    (34614096, 0),        # windowsRuntimeTypeNames
    (34614096, 0),        # windowsRuntimeStrings
    (26791168, 8140),     # exportedTypeDefinitions
]

data = bytearray(open(sys.argv[1], "rb").read())
header = [SANITY, VERSION] + [v for pair in SECTIONS for v in pair]
assert len(header) == 64
struct.pack_into("<64I", data, 0, *header)

# Each image's <Module> type has a junk genericContainerIndex (anti-dump trap;
# the runtime never reads it). Reset out-of-range values to -1.
TYPEDEFS, GENERIC_CONTAINERS = SECTIONS[19], SECTIONS[14]
container_count = GENERIC_CONTAINERS[1] // 16
fixed = 0
for pos in range(TYPEDEFS[0], TYPEDEFS[0] + TYPEDEFS[1], 88):
    gc = struct.unpack_from("<i", data, pos + 24)[0]
    if gc != -1 and not 0 <= gc < container_count:
        struct.pack_into("<i", data, pos + 24, -1)
        fixed += 1
print(f"reset {fixed} junk genericContainerIndex values")

open(sys.argv[2], "wb").write(data)
print(f"wrote {sys.argv[2]}")
