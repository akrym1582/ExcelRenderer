#!/usr/bin/env python3
"""Decode ExcelRenderer's version 1 nonexecuting PostScript text metadata."""
import argparse
import base64
import io
import json
import re
import struct
from pathlib import Path


def decode(path):
    records = {}
    pattern = re.compile(r"^%ExcelRenderer-Text-v1: (\d+) (\d+)/(\d+) ([A-Za-z0-9+/=]+)$")
    for line in Path(path).read_text(encoding="ascii").splitlines():
        match = pattern.fullmatch(line)
        if not match:
            continue
        identifier, part, count = map(int, match.group(1, 2, 3))
        entry = records.setdefault(identifier, {"count": count, "parts": {}})
        if count != entry["count"] or not 1 <= part <= count or part in entry["parts"]:
            raise ValueError("Invalid or duplicated metadata chunk")
        entry["parts"][part] = match[4]
    output = []
    for identifier, entry in records.items():
        if sorted(entry["parts"]) != list(range(1, entry["count"] + 1)):
            raise ValueError("Missing metadata chunks")
        encoded = "".join(entry["parts"][i] for i in range(1, entry["count"] + 1))
        stream = io.BytesIO(base64.b64decode(encoded, validate=True))

        def number(code):
            size = struct.calcsize("<" + code)
            return struct.unpack("<" + code, stream.read(size))[0]

        def string():
            size = 0
            for shift in range(0, 35, 7):
                byte = number("B")
                size |= (byte & 127) << shift
                if byte < 128:
                    value = stream.read(size)
                    if len(value) != size:
                        raise ValueError("Truncated string")
                    return value.decode("utf-8", errors="strict")
            raise ValueError("Invalid string length")

        record = {"version": 1, "record": identifier, "kind": number("B"), "page": number("i"), "command": number("i"), "text": string()}
        if record["kind"] == 1:
            record.update(font_identity=string(), x=number("d"), baseline=number("d"), size=number("d"))
            count = number("i")
            if count < 0 or count > len(stream.getvalue()):
                raise ValueError("Invalid glyph count")
            record["glyphs"] = [{"font": string(), "code": number("B"), "gid": number("H"), "cluster_utf8": number("I"), "advance_at_one_point": number("d")} for _ in range(count)]
        elif record["kind"] != 0:
            raise ValueError("Unknown record kind")
        if stream.read(1):
            raise ValueError("Trailing metadata bytes")
        output.append(record)
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = json.dumps(decode(args.input), ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(result + "\n", encoding="utf-8")
    else:
        print(result)


if __name__ == "__main__":
    main()
