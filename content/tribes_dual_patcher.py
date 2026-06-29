#!/usr/bin/env python3
"""
Standalone dual-mode patcher for Tribes2.exe.

Behavior:
- Patches AllocConsole call-sites so dedicated launches from a terminal do not force an extra console window.
- Sets PE subsystem to CUI so stdout/stderr bind to the launching terminal and the process remains console-attached.

No local script dependencies. Uses only Python standard library.

Default target executable:
- Tribes2.exe in the same directory as this script.

Usage examples:
  python patch_tribes2_dual_mode_standalone.py --dry-run
  python patch_tribes2_dual_mode_standalone.py --backup
  python patch_tribes2_dual_mode_standalone.py --exe "C:\\Games\\Tribes2\\Bin\\Tribes2.exe"
  python patch_tribes2_dual_mode_standalone.py --output-exe "C:\\Games\\Tribes2\\Bin\\Tribes2_dualmode.exe"
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
        name_raw = data[off : off + 8]
        name = name_raw.split(b"\x00", 1)[0].decode("ascii", errors="replace")
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


def locate_alloc_console_iat(data: bytes, opt_off: int, sections: list[Section]) -> int:
    dd_off = opt_off + 0x60
    import_rva = u32(data, dd_off + IMAGE_DIRECTORY_ENTRY_IMPORT * 8)
    import_size = u32(data, dd_off + IMAGE_DIRECTORY_ENTRY_IMPORT * 8 + 4)
    if import_rva == 0 or import_size == 0:
        raise PEError("Import directory is empty")

    imp_off = rva_to_off(import_rva, sections)

    while True:
        original_first_thunk = u32(data, imp_off + 0)
        _time_date_stamp = u32(data, imp_off + 4)
        _forwarder_chain = u32(data, imp_off + 8)
        name_rva = u32(data, imp_off + 12)
        first_thunk = u32(data, imp_off + 16)

        if (
            original_first_thunk == 0
            and _time_date_stamp == 0
            and _forwarder_chain == 0
            and name_rva == 0
            and first_thunk == 0
        ):
            break

        dll_name = read_c_string(data, rva_to_off(name_rva, sections)).lower()
        lookup_rva = original_first_thunk or first_thunk

        if "kernel32" in dll_name:
            idx = 0
            while True:
                thunk_rva = lookup_rva + idx * 4
                thunk_off = rva_to_off(thunk_rva, sections)
                thunk_data = u32(data, thunk_off)
                if thunk_data == 0:
                    break

                is_ordinal = (thunk_data & 0x80000000) != 0
                if not is_ordinal:
                    name_off = rva_to_off(thunk_data, sections)
                    func_name = read_c_string(data, name_off + 2)
                    if func_name == "AllocConsole":
                        return first_thunk + idx * 4
                idx += 1

        imp_off += 20

    raise PEError("kernel32!AllocConsole import not found")


def find_alloc_console_call_sites(data: bytes, text_off: int, text_size: int, alloc_iat_va: int) -> list[int]:
    target = struct.pack("<I", alloc_iat_va)
    blob = data[text_off : text_off + text_size]
    hits: list[int] = []

    i = 0
    end = len(blob) - 6
    while i <= end:
        if blob[i] == 0xFF and blob[i + 1] == 0x15 and blob[i + 2 : i + 6] == target:
            hits.append(text_off + i)
            i += 6
        else:
            i += 1

    return hits


def patch_alloc_console_calls(data: bytearray, call_sites: list[int]) -> int:
    for off in call_sites:
        data[off : off + 6] = b"\x90" * 6
    return len(call_sites)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Standalone Tribes2 dual-mode patcher "
            "(AllocConsole patch + subsystem CUI for attached stdio)"
        )
    )
    parser.add_argument(
        "--exe",
        help="Path to Tribes2.exe. Defaults to Tribes2.exe beside this script.",
    )
    parser.add_argument(
        "--output-exe",
        help=(
            "Write patched output to a separate path. "
            "If omitted, patches in-place."
        ),
    )
    parser.add_argument("--backup", action="store_true", help="Write .bak backup when patching in-place")
    parser.add_argument(
        "--keep-subsystem",
        action="store_true",
        help="Do not force subsystem to CUI (not recommended for terminal stdout/stderr behavior).",
    )
    parser.add_argument("--dry-run", action="store_true", help="Inspect only; do not modify file")
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    script_dir = pathlib.Path(__file__).resolve().parent
    exe_path = pathlib.Path(args.exe) if args.exe else (script_dir / "Tribes2.exe")

    if not exe_path.exists():
        print(f"ERROR: executable not found: {exe_path}")
        print("Hint: place this script next to Tribes2.exe or pass --exe.")
        return 2

    output_path = pathlib.Path(args.output_exe) if args.output_exe else exe_path
    in_place = output_path.resolve() == exe_path.resolve()

    raw = exe_path.read_bytes()
    data = bytearray(raw)

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
        print(f"WARNING: machine is 0x{machine:04X}; patcher targets 32-bit Tribes2 builds")

    opt_off = file_hdr_off + 20
    opt_magic = u16(data, opt_off)
    if opt_magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC:
        raise PEError(f"Unsupported optional header magic 0x{opt_magic:04X}")

    sec_off = opt_off + opt_size
    sections = parse_sections(data, num_sections, sec_off)
    text = find_section(sections, ".text")

    image_base = u32(data, opt_off + 0x1C)
    subsystem_off = opt_off + 0x44
    subsystem = u16(data, subsystem_off)

    alloc_iat_rva = locate_alloc_console_iat(data, opt_off, sections)
    alloc_iat_va = image_base + alloc_iat_rva
    call_sites = find_alloc_console_call_sites(data, text.raw_ptr, text.raw_size, alloc_iat_va)

    print(f"Source executable: {exe_path}")
    if in_place:
        print("Output executable: (in-place patch)")
    else:
        print(f"Output executable: {output_path}")
    print(f"ImageBase: 0x{image_base:08X}")
    print(f"AllocConsole IAT RVA: 0x{alloc_iat_rva:08X} (VA 0x{alloc_iat_va:08X})")
    print(f"AllocConsole call sites in .text: {len(call_sites)}")
    for idx, off in enumerate(call_sites, start=1):
        print(f"  [{idx}] file offset 0x{off:08X}")

    if subsystem == SUBSYSTEM_WINDOWS_GUI:
        print("Current subsystem: GUI")
    elif subsystem == SUBSYSTEM_WINDOWS_CUI:
        print("Current subsystem: CUI")
    else:
        print(f"Current subsystem: 0x{subsystem:04X}")

    if args.dry_run:
        print("Dry-run complete; no file changes made.")
        return 0

    if not call_sites:
        print("ERROR: no AllocConsole call-sites found to patch")
        return 3

    if args.backup and in_place:
        backup_path = exe_path.with_suffix(exe_path.suffix + ".bak")
        shutil.copy2(exe_path, backup_path)
        print(f"Backup written: {backup_path}")
    elif args.backup:
        print("Note: --backup ignored with --output-exe because source file is unchanged.")

    patched = patch_alloc_console_calls(data, call_sites)
    print(f"Patched AllocConsole call-sites: {patched}")

    changed_subsystem = False
    if not args.keep_subsystem and subsystem != SUBSYSTEM_WINDOWS_CUI:
        write_u16(data, subsystem_off, SUBSYSTEM_WINDOWS_CUI)
        changed_subsystem = True
        print("Subsystem patched: GUI -> CUI")
    elif args.keep_subsystem:
        print("Subsystem left unchanged due to --keep-subsystem")
    else:
        print("Subsystem already CUI")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(data)
    print("Patch write complete.")

    verify = output_path.read_bytes()
    for off in call_sites:
        if verify[off : off + 6] != b"\x90" * 6:
            print(f"ERROR: verification failed for call-site at 0x{off:08X}")
            return 4

    if not args.keep_subsystem and u16(verify, subsystem_off) != SUBSYSTEM_WINDOWS_CUI:
        print("ERROR: subsystem verification failed")
        return 5

    print("Verification OK.")
    if changed_subsystem:
        print("Dual-mode behavior applied: AllocConsole patched, subsystem set to CUI for attached stdio.")
    elif args.keep_subsystem:
        print("Dual-mode behavior applied: AllocConsole patched, subsystem unchanged by request.")
    else:
        print("Dual-mode behavior applied: AllocConsole patched, subsystem already CUI.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except PEError as exc:
        print(f"ERROR: {exc}")
        raise SystemExit(1)
