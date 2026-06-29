#!/usr/bin/env python3
"""
Headless-console patcher for Tribes2.exe (Dynamix "V12" engine).

Flips the PE subsystem GUI -> CUI so the dedicated server is a console app: its
console output goes to the inherited stdout/stderr and its console *input* is read
from the inherited stdin. The supervisor launches the game on a PTY (a real TTY,
no display) so the engine's ReadConsoleInput-based console works head-less -- that
is what lets the panel both capture the feed (stdout) and send commands (stdin),
without xvfb and without the in-engine telnet console.

No AllocConsole NOP and no console-input neutering are needed with the PTY: those
were workarounds for running on a plain pipe, where ReadConsoleInput fails and the
server crashes at "starting mission countdown".

Standard library only.

Usage:
  python tribes_dual_patcher.py --exe "C:\\Dynamix\\Tribes2\\GameData\\Tribes2.exe" --backup
  python tribes_dual_patcher.py --exe Tribes2.exe --dry-run
"""

from __future__ import annotations

import argparse
import pathlib
import shutil
import struct

IMAGE_DOS_SIGNATURE = 0x5A4D       # MZ
IMAGE_NT_SIGNATURE = 0x00004550    # PE\0\0
IMAGE_FILE_MACHINE_I386 = 0x014C
IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10B
SUBSYSTEM_WINDOWS_GUI = 2
SUBSYSTEM_WINDOWS_CUI = 3


class PEError(RuntimeError):
    pass


def u16(buf: bytes, off: int) -> int:
    return struct.unpack_from("<H", buf, off)[0]


def u32(buf: bytes, off: int) -> int:
    return struct.unpack_from("<I", buf, off)[0]


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Tribes2 headless patcher (PE subsystem GUI->CUI)")
    p.add_argument("--exe", help="Path to Tribes2.exe (default: beside this script).")
    p.add_argument("--output-exe", help="Write to a separate path (default: in-place).")
    p.add_argument("--backup", action="store_true", help="Write a .bak backup when patching in-place")
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
    if machine != IMAGE_FILE_MACHINE_I386:
        print(f"WARNING: machine is 0x{machine:04X}; this patcher targets 32-bit Tribes2 builds")
    opt_off = file_hdr_off + 20
    if u16(data, opt_off) != IMAGE_NT_OPTIONAL_HDR32_MAGIC:
        raise PEError("Unsupported optional header magic")
    subsystem_off = opt_off + 0x44
    subsystem = u16(data, subsystem_off)

    print(f"Executable: {exe_path}")
    print(f"Output: {'(in-place)' if in_place else output_path}")
    print(f"Current subsystem: {'GUI' if subsystem == SUBSYSTEM_WINDOWS_GUI else 'CUI' if subsystem == SUBSYSTEM_WINDOWS_CUI else hex(subsystem)}")

    if args.dry_run:
        print("Dry-run complete; no changes made.")
        return 0

    if subsystem == SUBSYSTEM_WINDOWS_CUI:
        print("Already CUI; nothing to do.")
        return 0

    if args.backup and in_place:
        backup = exe_path.with_suffix(exe_path.suffix + ".bak")
        shutil.copy2(exe_path, backup)
        print(f"Backup written: {backup}")

    struct.pack_into("<H", data, subsystem_off, SUBSYSTEM_WINDOWS_CUI)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(data)

    if u16(output_path.read_bytes(), subsystem_off) != SUBSYSTEM_WINDOWS_CUI:
        print("ERROR: subsystem verification failed")
        return 4
    print("Patched: subsystem GUI -> CUI. Run on a PTY for head-less console I/O.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except PEError as exc:
        print(f"ERROR: {exc}")
        raise SystemExit(1)
