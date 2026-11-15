"""Converte o campus_maua.obj (Z-up, coordenadas do CARLA) para Y-up centralizado,
pronto para importar na Unity.

(x, y, z) Z-up destro  ->  (x - cx, z - zmin, -(y - cy)) Y-up destro
A Unity depois espelha X ao importar (destro -> canhoto), entao o mapa nao fica invertido.
"""
import sys, shutil, pathlib

src = pathlib.Path(sys.argv[1])
dst_dir = pathlib.Path(sys.argv[2])
dst_dir.mkdir(parents=True, exist_ok=True)

xs, ys, zs = [], [], []
with src.open() as f:
    for line in f:
        if line.startswith("v "):
            _, x, y, z = line.split()[:4]
            xs.append(float(x)); ys.append(float(y)); zs.append(float(z))

cx = (min(xs) + max(xs)) / 2
cy = (min(ys) + max(ys)) / 2
zmin = min(zs)
print(f"centro=({cx:.3f}, {cy:.3f})  zmin={zmin:.3f}  vertices={len(xs)}")

with src.open() as f, (dst_dir / "campus_maua.obj").open("w", newline="\n") as out:
    out.write(f"# campus IMT Maua - convertido para Y-up (offset x={cx:.4f} y={cy:.4f} z={zmin:.4f})\n")
    for line in f:
        if line.startswith("v "):
            _, x, y, z = line.split()[:4]
            out.write(f"v {float(x)-cx:.4f} {float(z)-zmin:.4f} {-(float(y)-cy):.4f}\n")
        elif line.startswith("#"):
            continue
        else:
            out.write("g " + line[2:] if line.startswith("o ") else line)

shutil.copy(src.with_suffix(".mtl"), dst_dir / "campus_maua.mtl")
print("ok ->", dst_dir)
