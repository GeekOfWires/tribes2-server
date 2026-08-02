#!/usr/bin/env python3
"""Patch PE header *flags* on the Tribes 2 binaries (build-time, like tribes_dual_patcher.py).

Unlike an opcode/offset patch, this only flips well-known bits in the PE headers, located by
parsing the header at runtime. That makes it version-independent: it re-applies cleanly to a
newer QoL patch build (IFC22.dll is re-downloaded on every image build) and never touches a
single byte of anyone's code.

Supported patches:
  --set-laa      set IMAGE_FILE_LARGE_ADDRESS_AWARE in FileCharacteristics.
                 A 32-bit process on a 64-bit host gets ~4 GB of user address space instead of
                 2 GB, so a long-running server is far less likely to die on allocation failure.
  --clear-aslr   clear IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE in DllCharacteristics.
                 The module then loads at its preferred image base every run, so the fault
                 addresses recorded in the panel's crash reports are stable and comparable
                 between restarts (crash grouping/fingerprinting works). Relocations are left
                 intact, so the loader can still relocate if the base is taken.

Usage:
  pe_flags_patch.py --file <path> [--set-laa] [--clear-aslr] [--backup] [--dry-run]
Exit status is non-zero if a requested patch could not be applied.
"""
import argparse
import shutil
import struct
import sys

LARGE_ADDRESS_AWARE = 0x0020        # IMAGE_FILE_LARGE_ADDRESS_AWARE
DYNAMIC_BASE = 0x0040               # IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE


class PeHeader:
    """Locates the two header fields we patch. Read-only until write_back()."""

    def __init__(self, path):
        self.path = path
        with open(path, "rb") as fh:
            self.data = bytearray(fh.read())
        if self.data[:2] != b"MZ":
            raise ValueError(f"{path}: not a PE image (no MZ)")
        pe = struct.unpack_from("<I", self.data, 0x3C)[0]
        if self.data[pe:pe + 4] != b"PE\0\0":
            raise ValueError(f"{path}: bad PE signature")
        # COFF header follows the 4-byte signature; Characteristics is its last field.
        self.file_chars_off = pe + 4 + 18
        opt = pe + 24
        magic = struct.unpack_from("<H", self.data, opt)[0]
        if magic not in (0x10B, 0x20B):
            raise ValueError(f"{path}: unknown optional-header magic 0x{magic:04x}")
        # DllCharacteristics sits at a fixed offset in both PE32 and PE32+ optional headers.
        self.dll_chars_off = opt + 70

    @property
    def file_chars(self):
        return struct.unpack_from("<H", self.data, self.file_chars_off)[0]

    @property
    def dll_chars(self):
        return struct.unpack_from("<H", self.data, self.dll_chars_off)[0]

    def set_file_chars(self, value):
        struct.pack_into("<H", self.data, self.file_chars_off, value)

    def set_dll_chars(self, value):
        struct.pack_into("<H", self.data, self.dll_chars_off, value)

    def write_back(self):
        with open(self.path, "r+b") as fh:
            fh.write(self.data)


def main():
    ap = argparse.ArgumentParser(description="Patch PE header flags on a Tribes 2 binary.")
    ap.add_argument("--file", required=True, help="path to the .exe/.dll to patch")
    ap.add_argument("--set-laa", action="store_true", help="set LARGE_ADDRESS_AWARE")
    ap.add_argument("--clear-aslr", action="store_true", help="clear DYNAMIC_BASE (ASLR)")
    ap.add_argument("--backup", action="store_true", help="write <file>.preflags.bak first")
    ap.add_argument("--dry-run", action="store_true", help="report state, change nothing")
    args = ap.parse_args()

    if not (args.set_laa or args.clear_aslr):
        ap.error("nothing to do: pass --set-laa and/or --clear-aslr")

    pe = PeHeader(args.file)
    print(f"PE flags: {args.file}")
    print(f"  FileCharacteristics=0x{pe.file_chars:04x}  DllCharacteristics=0x{pe.dll_chars:04x}")

    changes = []
    if args.set_laa:
        if pe.file_chars & LARGE_ADDRESS_AWARE:
            print("  LARGE_ADDRESS_AWARE: already set")
        else:
            changes.append(("LARGE_ADDRESS_AWARE", "set"))
    if args.clear_aslr:
        if pe.dll_chars & DYNAMIC_BASE:
            changes.append(("DYNAMIC_BASE(ASLR)", "cleared"))
        else:
            print("  DYNAMIC_BASE(ASLR): already clear")

    if not changes:
        print("  -> nothing to change (already patched)")
        return 0
    if args.dry_run:
        for name, what in changes:
            print(f"  -> would be {what}: {name}")
        return 0

    if args.backup:
        shutil.copy2(args.file, args.file + ".preflags.bak")
        print(f"  backup: {args.file}.preflags.bak")

    if args.set_laa and not pe.file_chars & LARGE_ADDRESS_AWARE:
        pe.set_file_chars(pe.file_chars | LARGE_ADDRESS_AWARE)
    if args.clear_aslr and pe.dll_chars & DYNAMIC_BASE:
        pe.set_dll_chars(pe.dll_chars & ~DYNAMIC_BASE)
    pe.write_back()

    # Re-read from disk so the confirmation reflects what was actually written.
    after = PeHeader(args.file)
    for name, what in changes:
        print(f"  -> {what}: {name}")
    print(f"  now: FileCharacteristics=0x{after.file_chars:04x}  "
          f"DllCharacteristics=0x{after.dll_chars:04x}")

    if args.set_laa and not after.file_chars & LARGE_ADDRESS_AWARE:
        print("  ERROR: LARGE_ADDRESS_AWARE did not stick", file=sys.stderr)
        return 1
    if args.clear_aslr and after.dll_chars & DYNAMIC_BASE:
        print("  ERROR: DYNAMIC_BASE still set", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
