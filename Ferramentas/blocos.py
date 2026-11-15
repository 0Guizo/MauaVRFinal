"""Identifica cada predio da malha 'edificacoes' do campus_maua.obj e associa o nome do bloco
(letras do mapa oficial, mesmo registro foto->local do pipeline rodada8/edificacoes.py).
Saida: JSON com posicao (coordenadas Unity do OBJ convertido) de cada bloco.
"""
import json, math, sys
import numpy as np
from pyproj import Transformer

OBJ, SAIDA = sys.argv[1], sys.argv[2]

# --- constantes do pipeline (config.py / edificacoes.py)
X0, Y0, UTM_EPSG = 339197.0, 7383586.0, 31983
TM = "+proj=tmerc +lat_0=-23.6504405863 +lon_0=-46.5766127053 +k=1 +x_0=0 +y_0=0 +ellps=GRS80 +towgs84=0,0,0,0,0,0,0 +units=m +no_defs"
PHOTO_ANCHOR_PX, PHOTO_ANCHOR_LOCAL, PHOTO_ROT_DEG, PHOTO_SCALE = (405.0, 100.0), (415.0, 403.0), -80.0, 0.534
BLOCOS_PX = {
    "A": (967, 387), "B": (942, 353), "C": (921, 325), "D": (868, 315), "E": (845, 283),
    "F": (760, 200), "G": (680, 195), "H": (740, 515), "I": (356, 378), "J": (348, 288),
    "L": (968, 243), "M": (613, 80), "N": (947, 258), "P": (690, 322), "Q": (632, 297),
    "R": (357, 473), "S": (940, 540), "U": (700, 450), "V": (606, 462),
    "Ginasio": (355, 210), "Centro de Vivencia": (842, 190), "Portaria 1": (418, 720),
}
DIST_MAX = 12.0  # m: letra ate a pegada

def photo_to_local(uv):
    d = np.array([uv[0] - PHOTO_ANCHOR_PX[0], -(uv[1] - PHOTO_ANCHOR_PX[1])])
    a = math.radians(PHOTO_ROT_DEG)
    R = np.array([[math.cos(a), -math.sin(a)], [math.sin(a), math.cos(a)]])
    return R @ d * PHOTO_SCALE + np.array(PHOTO_ANCHOR_LOCAL)

tr = Transformer.from_crs(f"EPSG:{UTM_EPSG}", TM, always_xy=True)
letras = {}
for k, uv in BLOCOS_PX.items():
    p = photo_to_local(uv)
    letras[k] = np.array(tr.transform(p[0] + X0, p[1] + Y0))

# --- le OBJ original (Z-up, coordenadas tm)
V, F, grupo = [], [], None
with open(OBJ) as f:
    for ln in f:
        if ln.startswith("v "):
            V.append([float(t) for t in ln.split()[1:4]])
        elif ln[:2] in ("o ", "g "):
            grupo = ln.split()[1]
        elif ln.startswith("f ") and grupo == "edificacoes":
            F.append([int(t.split("/")[0]) - 1 for t in ln.split()[1:4]])
V, F = np.array(V), np.array(F)
xs, ys, zs = V[:, 0], V[:, 1], V[:, 2]
cx, cy, zmin = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2, zs.min()

# componentes conectados (union-find por vertice) + uniao por pegada sobreposta
pai = list(range(len(V)))
def raiz(i):
    while pai[i] != i:
        pai[i] = pai[pai[i]]; i = pai[i]
    return i
for a, b, c in F:
    ra, rb, rc = raiz(a), raiz(b), raiz(c)
    pai[rb] = ra; pai[raiz(rc)] = ra
comp = {}
for t, (a, b, c) in enumerate(F):
    comp.setdefault(raiz(a), []).append(t)

predios = []
for tris in comp.values():
    T = V[F[tris]]                      # (n,3,3)
    e1, e2 = T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]
    n = np.cross(e1, e2)
    area_xy = 0.5 * np.abs(n[:, 2])
    teto = n[:, 2] / (np.linalg.norm(n, axis=1) + 1e-12) > 0.7
    P = T.reshape(-1, 3)
    predios.append(dict(tris=T[teto] if teto.any() else T, area=float(area_xy[teto].sum()),
                        xmin=P[:, 0].min(), xmax=P[:, 0].max(), ymin=P[:, 1].min(), ymax=P[:, 1].max(),
                        zmin=P[:, 2].min(), zmax=P[:, 2].max()))
predios = [p for p in predios if p["area"] > 15]
predios.sort(key=lambda p: -p["area"])

def dist_ponto_tris(pt, T):
    """distancia 2D do ponto a uniao dos triangulos (0 se dentro)."""
    a, b, c = T[:, 0, :2], T[:, 1, :2], T[:, 2, :2]
    def cr(o, p, q): return (p[:, 0]-o[:, 0])*(q[1]-o[:, 1]) - (p[:, 1]-o[:, 1])*(q[0]-o[:, 0])
    d1, d2, d3 = cr(a, b, pt), cr(b, c, pt), cr(c, a, pt)
    dentro = ((d1 >= 0) & (d2 >= 0) & (d3 >= 0)) | ((d1 <= 0) & (d2 <= 0) & (d3 <= 0))
    if dentro.any(): return 0.0
    def seg(p, q):
        pq = q - p; t = np.clip(((pt - p) * pq).sum(1) / ((pq * pq).sum(1) + 1e-12), 0, 1)
        return np.linalg.norm(p + pq * t[:, None] - pt, axis=1)
    return float(min(seg(a, b).min(), seg(b, c).min(), seg(c, a).min()))

for p in predios:
    d = {k: dist_ponto_tris(v, p["tris"]) for k, v in letras.items()}
    k = min(d, key=d.get)
    p["bloco"], p["dist"], p["d"] = (k if d[k] <= DIST_MAX else "-"), d[k], d

# letras que ficaram sem predio: pega o predio livre mais proximo ate 25 m
usadas = {p["bloco"] for p in predios}
for k in letras:
    if k in usadas: continue
    livres = [p for p in predios if p["bloco"] == "-" and p["d"][k] <= 25.0]
    if livres:
        q = min(livres, key=lambda p: p["d"][k]); q["bloco"], q["dist"] = k, q["d"][k]

# um rotulo por bloco: o maior predio que recebeu aquela letra
saida = []
vistos = set()
for p in predios:
    print(f"area={p['area']:7.1f}  h={p['zmax']-p['zmin']:5.1f}  bloco={p['bloco']:20s} dist={p['dist']:.1f}")
    if p["bloco"] == "-" or p["bloco"] in vistos: continue
    vistos.add(p["bloco"])
    # para Unity: (x - cx, z - zmin, -(y - cy)); bbox em XZ Unity
    saida.append(dict(
        nome=p["bloco"], area=round(p["area"], 1),
        pegada=[round(float(v), 3) for t in p["tris"] for q in t for v in (q[0] - cx, -(q[1] - cy))],
        xmin=p["xmin"] - cx, xmax=p["xmax"] - cx,
        zmin=-(p["ymax"] - cy), zmax=-(p["ymin"] - cy),
        chao=p["zmin"] - zmin, topo=p["zmax"] - zmin,
        letraX=float(letras[p["bloco"]][0] - cx), letraZ=float(-(letras[p["bloco"]][1] - cy)),
    ))
print("sem predio:", sorted(set(letras) - vistos))
json.dump({"blocos": saida}, open(SAIDA, "w"), indent=1)
print("gravado", SAIDA, len(saida))
