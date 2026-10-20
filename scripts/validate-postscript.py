#!/usr/bin/env python3
"""Independently verify embedded sfnt checksums and metadata/code/GID correspondence."""
import argparse
import importlib.util
import json
import re
import struct
from pathlib import Path

spec = importlib.util.spec_from_file_location("ps_text", Path(__file__).with_name("decode-postscript-text.py"))
metadata = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metadata)


def validate(path):
    ps = path.read_text(encoding="ascii")
    records = metadata.decode(path)
    resources = {}
    fonts = []
    for name, body in re.findall(r"%%BeginResource: font (\w+)\n(.*?)%%EndResource", ps, re.S):
        chunks = re.search(r"/sfnts \[(.*?)\] def", body, re.S)
        if chunks:
            data = bytearray()
            for chunk in re.findall(r"<([0-9A-F\s]+)>", chunks[1]):
                value = bytes.fromhex(chunk)
                assert 1 <= len(value) <= 65535 and value[-1] == 0, "Invalid Type 42 sfnts chunk"
                assert len(value) % 2 == 1, "TrueType data chunks must have even length"
                data.extend(value[:-1])
            assert len(data) % 4 == 0
            assert sum(struct.unpack(">" + "I" * (len(data) // 4), data)) & 0xffffffff == 0xB1B0AFBA
            count = struct.unpack_from(">H", data, 4)[0]
            for index in range(count):
                tag, checksum, start, length = struct.unpack_from(">4sIII", data, 12 + index * 16)
                assert start % 4 == 0 and start + length <= len(data)
                table = bytearray(data[start:start + length])
                if tag == b"head":
                    table[8:12] = b"\0" * 4
                table.extend(b"\0" * (-len(table) % 4))
                assert sum(struct.unpack(">" + "I" * (len(table) // 4), table)) & 0xffffffff == checksum
            fonts.append({"name": name, "bytes": len(data), "chunks": len(re.findall("<", chunks[1]))})
        else:
            encoding, charstrings = body.split("/CharStrings", 1)
            codes = {int(code): glyph for code, glyph in re.findall(r"dup (\d+) /(\w+) put", encoding)}
            gids = {glyph: int(gid) for glyph, gid in re.findall(r"/(\w+) (\d+) def", charstrings)}
            resources[name] = {code: gids[glyph] for code, glyph in codes.items()}
    glyphs = [glyph for record in records if record["kind"] == 1 for glyph in record["glyphs"]]
    for glyph in glyphs:
        assert resources[glyph["font"]][glyph["code"]] == glyph["gid"], "Metadata and CharStrings differ"
    shown = re.findall(r"/(ERF\d+B\d+) findfont[^\n]+\n[^\n]+moveto <([0-9A-F]{2})> show", ps)
    assert shown == [(glyph["font"], f'{glyph["code"]:02X}') for glyph in glyphs], "Metadata and visible show operators differ"
    pages = len(re.findall(r"^%%Page: ", ps, re.M))
    assert len(re.findall(r"^grestore showpage$", ps, re.M)) == pages
    assert int(re.findall(r"^%%Pages: (\d+)$", ps, re.M)[-1]) == pages
    return {"pages": pages, "fonts": fonts, "glyphs": len(glyphs), "commands": sum(record["kind"] == 0 for record in records), "passed": True}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", type=Path, nargs="+")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = {str(path): validate(path) for path in args.inputs}
    text = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    else:
        print(text)
