"""Render measured atmospheric fields with shared scales across each seed's variants.

Uses only Python's standard library. Run after --climate-matrix:
python tests/GeneratorChecks/render_climate_diagnostics.py results/climate-matrix/1155827384
"""

import csv
import json
from pathlib import Path
import struct
import sys
import zlib


def write_png(path, width, height, pixels):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    raw = b"".join(b"\0" + pixels[y * width * 3:(y + 1) * width * 3] for y in range(height))
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def color(value):
    stops = [(12, 20, 50), (24, 82, 127), (39, 145, 155), (119, 198, 157), (245, 246, 196)]
    p = max(0, min(1, value)) * (len(stops) - 1)
    i = min(int(p), len(stops) - 2)
    return bytes(round(a + (b - a) * (p - i)) for a, b in zip(stops[i], stops[i + 1]))


def render(folder, mapped=False):
    sources = sorted(folder.glob("*-hex-rain.csv" if mapped else "*-cells.csv"))
    if not sources:
        raise ValueError(f"No measured fields in {folder}")
    fields = ["rain"] if mapped else ["rain", "vapor", "cloud", "soil", "rainReturn", "aquaticEvaporation", "soilEvaporation"]
    for source in sources:
        with source.open(newline="") as stream:
            available = next(csv.reader(stream))
        fields = [field for field in fields if field in available]
    limits = {field: 0.0 for field in fields}
    # The same physical value has the same color in every variant. No per-image normalization.
    for source in sources:
        with source.open(newline="") as stream:
            for row in csv.DictReader(stream):
                for field in fields:
                    limits[field] = max(limits[field], float(row[field]))
    for source in sources:
        with source.open(newline="") as stream:
            rows = list(csv.DictReader(stream))
        width = max(int(row["x"]) for row in rows) + 1
        height = max(int(row["y"]) for row in rows) + 1
        if len(rows) != width * height or len({(row["x"], row["y"]) for row in rows}) != len(rows):
            raise ValueError(f"Incomplete or duplicated grid in {source}; wait for the simulation to finish")
        for field in fields:
            pixels = bytearray(width * height * 3)
            for row in rows:
                i = int(row["y"]) * width + int(row["x"])
                pixels[i * 3:i * 3 + 3] = color(float(row[field]) / (limits[field] or 1))
            name = source.stem.removesuffix('-hex-rain') + '-mapped' if mapped else source.stem.removesuffix('-cells')
            write_png(folder / f"{name}-{field}.png", width, height, pixels)
    (folder / ("mapped-scales.json" if mapped else "scales.json")).write_text(json.dumps({
        "range": "linear, zero to shared maximum; dark to light",
        "projection": "atmospheric grid, row 0 at top, not the hex/Atlas projection",
        "units": "rain: mean of generated hex rainfall after cap/offset" if mapped else "rain: generator rain units before display cap; remaining fields: normalized solver units",
        "time": "generated map field, aggregated to the atmospheric grid for display" if mapped else "rain/vapor/cloud/return/evaporation: sampling-window means or rates; soil: final state",
        "maxima": limits,
    }, indent=2), encoding="utf-8")
    print(f"Rendered {len(sources) * len(fields)} maps in {folder}")


if __name__ == "__main__":
    for argument in sys.argv[1:]:
        if argument != "--mapped":
            render(Path(argument), mapped="--mapped" in sys.argv[1:])
