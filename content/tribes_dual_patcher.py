#!/usr/bin/env python3
"""
Headless-server patcher for Tribes2.exe (Dynamix "V12" engine).

Makes the dedicated server run head-less (no X / no real console, no xvfb) without
crashing, and bind stdout to the launching terminal so a supervisor can capture the
console feed:

1. Subsystem GUI -> CUI, so console output goes to the inherited stdout/stderr
   instead of a GUI console window.
2. Neuter the per-tick console-INPUT poller. That routine calls
   GetNumberOfConsoleInputEvents + ReadConsoleInput to read keystrokes typed at the
   server console; with no real console attached the event count is left
   uninitialized and the subsequent INPUT_RECORD loop over-reads the stack, causing
   an access violation right after "starting mission countdown...". We force the
   routine's existing early-out (its `cmp byte ptr [ebx],0 ; je <epilogue>`) to be
   taken unconditionally, so it returns cleanly every tick. Server-console keyboard
   input is not needed -- commands are issued over the engine's telnet console.

   (The earlier approach of NOP-ing AllocConsole did NOT help: it removed the
   console entirely, which made the very same input poller over-read and crash.)

No third-party dependencies. Standard library only.

Usage:
  python tribes_dual_patcher.py --exe "C:\\Dynamix\\Tribes2\\GameData\\Tribes2.exe" --backup
  python tribes_dual_patcher.py --exe Tribes2.exe --dry-run
"""

from __future__ import annotations

import argparse
import pathlib
import shutil
import struct
import sys
from dataclasses import dataclass

IMAGE_DOS_SIGNATURE = 0x5A4D  # MZ
IMAGE_NT_SIGNATURE = 0x00004550  # PE\0\0
IMAGE_FILE_MACHINE_I386 = 0x014C
IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10B
IMAGE_DIRECTORY_ENTRY_IMPORT = 1

SUBSYSTEM_WINDOWS_GUI = 2
SUBSYSTEM_WINDOWS_CUI = 3

# Anchor import: single-name (no A/W variant), used only by the console-input poller.
ANCHOR_IMPORT = "GetNumberOfConsoleInputEvents"
# cmp byte ptr [ebx], 0 ; je rel32   ->  the poller's early-out to its epilogue
JE_PATTERN = b"\x80\x3b\x00\x0f\x84"
BACK_SCAN = 0x40  # bytes to look back from the anchor call for the early-out je


@dataclass
class Section:
    name: str
    va: int
    vsz: int
    raw_ptr: int
    raw_size: int


class PEError(RuntimeError):
    pass


def u16(buf: bytes, off: int) -> int:
    return struct.unpack_from("<H", buf, off)[0]


def u32(buf: bytes, off: int) -> int:
    return struct.unpack_from("<I", buf, off)[0]


def write_u16(buf: bytearray, off: int, value: int) -> None:
    struct.pack_into("<H", buf, off, value)


def parse_sections(data: bytes, num_sections: int, sec_off: int) -> list[Section]:
    sections: list[Section] = []
    for i in range(num_sections):
        off = sec_off + i * 40
        name = data[off : off + 8].split(b"\x00", 1)[0].decode("ascii", errors="replace")
        vsz = u32(data, off + 8)
        va = u32(data, off + 12)
        raw_size = u32(data, off + 16)
        raw_ptr = u32(data, off + 20)
        sections.append(Section(name=name, va=va, vsz=vsz, raw_ptr=raw_ptr, raw_size=raw_size))
    return sections


def rva_to_off(rva: int, sections: list[Section]) -> int:
    for s in sections:
        span = max(s.vsz, s.raw_size)
        if s.va <= rva < s.va + span:
            return s.raw_ptr + (rva - s.va)
    raise PEError(f"RVA 0x{rva:08X} does not map to a file offset")


def find_section(sections: list[Section], name: str) -> Section:
    for s in sections:
        if s.name == name:
            return s
    raise PEError(f"Section {name} not found")


def read_c_string(data: bytes, off: int) -> str:
    end = data.find(b"\x00", off)
    if end == -1:
        raise PEError("Unterminated C string in PE data")
    return data[off:end].decode("ascii", errors="replace")


def locate_import_iat(data: bytes, opt_off: int, sections: list[Section], func_name: str) -> int:
    """Return the VA of the IAT slot for kernel32!<func_name>."""
    dd_off = opt_off + 0x60
    import_rva = u32(data, dd_off + IMAGE_DIRECTORY_ENTRY_IMPORT * 8)
    image_base = u32(data, opt_off + 0x1C)
    if import_rva == 0:
        raise PEError("Import directory is empty")
    imp_off = rva_to_off(import_rva, sections)
    while True:
        original_first_thunk = u32(data, imp_off + 0)
        name_rva = u32(data, imp_off + 12)
        first_thunk = u32(data, imp_off + 16)
        if original_first_thunk == 0 and name_rva == 0 and first_thunk == 0:
            break
        dll_name = read_c_string(data, rva_to_off(name_rva, sections)).lower()
        lookup_rva = original_first_thunk or first_thunk
        if "kernel32" in dll_name:
            idx = 0
            while True:
                thunk_data = u32(data, rva_to_off(lookup_rva + idx * 4, sections))
                if thunk_data == 0:
                    break
                if (thunk_data & 0x80000000) == 0:  # by name
                    if read_c_string(data, rva_to_off(thunk_data, sections) + 2) == func_name:
                        return image_base + first_thunk + idx * 4
                idx += 1
        imp_off += 20
    raise PEError(f"kernel32!{func_name} import not found")


def find_indirect_call_sites(data: bytes, text: Section, iat_va: int) -> list[int]:
    target = struct.pack("<I", iat_va)
    blob = data[text.raw_ptr : text.raw_ptr + text.raw_size]
    hits: list[int] = []
    i, end = 0, len(blob) - 6
    while i <= end:
        if blob[i] == 0xFF and blob[i + 1] == 0x15 and blob[i + 2 : i + 6] == target:
            hits.append(text.raw_ptr + i)
            i += 6
        else:
            i += 1
    return hits


def find_poller_je(data: bytes, call_off: int) -> int:
    """Offset of the poller's early-out branch (the 0F 84 je, or E9 jmp if already
    patched) just before the anchor call: `cmp byte ptr [ebx],0 ; je/jmp <epilogue>`."""
    base = call_off - BACK_SCAN
    window = data[base:call_off]
    for pat in (JE_PATTERN, b"\x80\x3b\x00\xe9"):  # cmp;je (unpatched) | cmp;jmp (patched)
        pos = window.rfind(pat)
        if pos != -1:
            return base + pos + 3  # offset of the branch opcode (0F 84 or E9)
    raise PEError("console-poll early-out (cmp byte[ebx],0 ; je) not found near anchor call")


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Headless Tribes2 dedicated-server patcher (CUI + neuter console-input poller)")
    p.add_argument("--exe", help="Path to Tribes2.exe (default: beside this script).")
    p.add_argument("--output-exe", help="Write patched output to a separate path (default: in-place).")
    p.add_argument("--backup", action="store_true", help="Write a .bak backup when patching in-place")
    p.add_argument("--keep-subsystem", action="store_true", help="Do not flip subsystem to CUI")
    p.add_argument("--dry-run", action="store_true", help="Inspect only; do not modify the file")
    return p.parse_args()


def main() -> int:
    args = parse_args()
    script_dir = pathlib.Path(__file__).resolve().parent
    exe_path = pathlib.Path(args.exe) if args.exe else (script_dir / "Tribes2.exe")
    if not exe_path.exists():
        print(f"ERROR: executable not found: {exe_path}")
        return 2
    output_path = pathlib.Path(args.output_exe) if args.output_exe else exe_path
    in_place = output_path.resolve() == exe_path.resolve()

    data = bytearray(exe_path.read_bytes())
    if u16(data, 0) != IMAGE_DOS_SIGNATURE:
        raise PEError("Not a valid MZ executable")
    pe_off = u32(data, 0x3C)
    if u32(data, pe_off) != IMAGE_NT_SIGNATURE:
        raise PEError("PE signature not found")
    file_hdr_off = pe_off + 4
    machine = u16(data, file_hdr_off + 0)
    num_sections = u16(data, file_hdr_off + 2)
    opt_size = u16(data, file_hdr_off + 16)
    if machine != IMAGE_FILE_MACHINE_I386:
        print(f"WARNING: machine is 0x{machine:04X}; this patcher targets 32-bit Tribes2 builds")
    opt_off = file_hdr_off + 20
    if u16(data, opt_off) != IMAGE_NT_OPTIONAL_HDR32_MAGIC:
        raise PEError("Unsupported optional header magic")
    sections = parse_sections(data, num_sections, opt_off + opt_size)
    text = find_section(sections, ".text")
    subsystem_off = opt_off + 0x44
    subsystem = u16(data, subsystem_off)

    anchor_va = locate_import_iat(data, opt_off, sections, ANCHOR_IMPORT)
    call_sites = find_indirect_call_sites(data, text, anchor_va)
    if not call_sites:
        raise PEError(f"no call site for kernel32!{ANCHOR_IMPORT} found")
    je_off = find_poller_je(data, call_sites[0])

    print(f"Executable: {exe_path}")
    print(f"Output: {'(in-place)' if in_place else output_path}")
    print(f"Subsystem: {'GUI' if subsystem == SUBSYSTEM_WINDOWS_GUI else 'CUI' if subsystem == SUBSYSTEM_WINDOWS_CUI else hex(subsystem)}")
    print(f"{ANCHOR_IMPORT} IAT VA: 0x{anchor_va:08X}; call site file offset: 0x{call_sites[0]:08X}")
    je_bytes = bytes(data[je_off : je_off + 6])
    print(f"console-poll je at file offset 0x{je_off:08X}: {je_bytes.hex()}")

    already_patched = je_bytes[0] == 0xE9
    if args.dry_run:
        print("Dry-run complete; no changes made.")
        return 0

    if je_bytes[:2] != b"\x0f\x84" and not already_patched:
        raise PEError(f"unexpected bytes at console-poll je: {je_bytes.hex()}")

    if args.backup and in_place:
        backup = exe_path.with_suffix(exe_path.suffix + ".bak")
        shutil.copy2(exe_path, backup)
        print(f"Backup written: {backup}")

    changed = []
    # 1) subsystem -> CUI
    if not args.keep_subsystem and subsystem != SUBSYSTEM_WINDOWS_CUI:
        write_u16(data, subsystem_off, SUBSYSTEM_WINDOWS_CUI)
        changed.append("subsystem GUI->CUI")
    # 2) neuter console-input poller: je rel32 (0F 84) -> jmp rel32 (E9), pad with NOP
    if not already_patched:
        rel = struct.unpack_from("<i", data, je_off + 2)[0]
        data[je_off] = 0xE9
        struct.pack_into("<i", data, je_off + 1, rel + 1)  # account for 5-byte vs 6-byte form
        data[je_off + 5] = 0x90
        changed.append("console-input poller neutered (je->jmp)")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(data)

    verify = output_path.read_bytes()
    if not args.keep_subsystem and u16(verify, subsystem_off) != SUBSYSTEM_WINDOWS_CUI:
        print("ERROR: subsystem verification failed")
        return 4
    if verify[je_off] != 0xE9:
        print("ERROR: console-poll patch verification failed")
        return 5

    print("Patched: " + (", ".join(changed) if changed else "nothing (already patched)"))
    print("Verification OK. Dedicated server will run headless and write to stdout.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except PEError as exc:
        print(f"ERROR: {exc}")
        raise SystemExit(1)
