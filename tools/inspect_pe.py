"""Static PE inspection; never loads or executes vendor binaries."""
import argparse
import hashlib
import json
import re
from pathlib import Path

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32, CS_MODE_64


def inspect(path):
    data = path.read_bytes()
    pe = pefile.PE(data=data)
    strings = []
    for pattern, encoding in ((rb"[\x20-\x7e]{5,}", "ascii"),
                              (rb"(?:[\x20-\x7e]\x00){5,}", "utf-16le")):
        for match in re.finditer(pattern, data):
            value = match.group().decode(encoding)
            if re.search(r"asus|elan|touch|gesture|device|ioctl|shared|mapping|event|packet|finger|zoom|ctrl|pipe|mutex", value, re.I):
                try:
                    rva = pe.get_rva_from_offset(match.start())
                except pefile.PEFormatError:
                    rva = None
                strings.append(dict(offset=match.start(), rva=rva, text=value))
    imports = {}
    for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
        imports[entry.dll.decode()] = [dict(name=(i.name or b"").decode(), ordinal=i.ordinal,
                                           address=i.address) for i in entry.imports]
    exports = [dict(name=(s.name or b"").decode(), ordinal=s.ordinal, rva=s.address)
               for s in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]
    return dict(path=str(path), sha256=hashlib.sha256(data).hexdigest(),
                image_base=pe.OPTIONAL_HEADER.ImageBase, machine=pe.FILE_HEADER.Machine,
                imports=imports, exports=exports, strings=strings)


def disassemble(path, rva, size):
    pe = pefile.PE(str(path))
    md = Cs(CS_ARCH_X86, CS_MODE_64 if pe.FILE_HEADER.Machine == 0x8664 else CS_MODE_32)
    md.detail = True
    from capstone.x86 import X86_OP_MEM, X86_REG_RIP
    imports = {i.address: (i.name or b"?").decode() for e in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []) for i in e.imports}
    for ins in md.disasm(pe.get_data(rva, size), pe.OPTIONAL_HEADER.ImageBase + rva):
        annotation = ""
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                target = ins.address + ins.size + op.mem.disp
                if target in imports:
                    annotation = " ; " + imports[target]
        print(f"{ins.address:016x}  {ins.mnemonic:8} {ins.op_str}{annotation}")


def xrefs(path, targets, literal=False, displacement=False):
    from capstone.x86 import X86_OP_MEM, X86_OP_IMM, X86_REG_RIP
    pe = pefile.PE(str(path))
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_64 if pe.FILE_HEADER.Machine == 0x8664 else CS_MODE_32)
    md.detail = True
    wanted = {target if literal else base + target for target in targets}
    ranges = [(entry.struct.BeginAddress, entry.struct.EndAddress) for entry in getattr(pe, "DIRECTORY_ENTRY_EXCEPTION", [])]
    if not ranges:
        ranges = [(s.VirtualAddress, s.VirtualAddress + s.SizeOfRawData) for s in pe.sections if s.Characteristics & 0x20000000]
    for start, end in ranges:
        instructions = list(md.disasm(pe.get_data(start, end - start), base + start))
        for index, ins in enumerate(instructions):
            for op in ins.operands:
                target = None
                if op.type == X86_OP_IMM:
                    target = op.imm
                elif op.type == X86_OP_MEM:
                    target = op.mem.disp if displacement else (ins.address + ins.size + op.mem.disp if op.mem.base == X86_REG_RIP else op.mem.disp)
                if target in wanted:
                    print(f"XREF -> {'VALUE' if literal else 'RVA'} {target if literal else target - base:#x}")
                    for ctx in instructions[max(0, index - 10): index + 8]:
                        print(f"{ctx.address:016x}  {ctx.mnemonic:8} {ctx.op_str}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+", type=Path)
    parser.add_argument("--rva", type=lambda s: int(s, 0))
    parser.add_argument("--size", type=lambda s: int(s, 0), default=512)
    parser.add_argument("--xref", nargs="+", type=lambda s: int(s, 0))
    parser.add_argument("--brief", action="store_true")
    parser.add_argument("--literal", action="store_true")
    parser.add_argument("--displacement", action="store_true")
    args = parser.parse_args()
    for path in args.paths:
        if args.xref:
            xrefs(path, args.xref, args.literal or args.displacement, args.displacement)
        elif args.rva is not None:
            disassemble(path, args.rva, args.size)
        else:
            result = inspect(path)
            if args.brief:
                result["imports"] = {dll: [i["name"] for i in entries] for dll, entries in result["imports"].items()}
            print(json.dumps(result, indent=2))
