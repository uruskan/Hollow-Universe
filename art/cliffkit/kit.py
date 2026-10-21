"""The cliff kit's pieces (docs/WORLD_TERRAIN_PLAN.md §4.3, iteration 4): which owned scan each is cut from and how.

Every scan lies in `SOURCES` (D:/GameDev/Assets) as Megascans GLBs bought on Fab (game/assets/THIRD_PARTY.md). A piece
is described in its scan's frame turned so that its face looks along +X (Blender's axes, Z up, metres):

- `yaw`: the direction the face looks in the scan (degrees about Z, 0 = +X, 90 = +Y), turned to +X;
- `span`: where the piece runs along the face after that turn (Blender Y: the left end as you face the rock is -Y);
  None takes the whole rock;
- `ends`: (left, right), each "cut" (a diagonal fracture), "taper" (a slope end: the rock's height squashed toward that
  end, its strata dipping into the ground, then a small fracture) or "natural" (the scan's own end);
- `angles`: optional (left, right) outward directions of the ends in that frame (degrees; -90 and 90 by default), for a
  corner whose wings run off at an angle;
- `scale`: the authored size against the scan; `mirror`: mirrored across the face's length;
- `align`: turn the piece about Z so that the foot of its face runs along Y (straight pieces);
- `fill`: a cliff scanned open behind, its top trimmed `back` metres behind the lip; False for a rock scanned all
  round (only its bottom is open);
- `back`: metres of the top kept behind the lip; `apron`: flat ground further than this from the rock is dropped;
- `lod0`: LOD0's triangle budget; `range`: the placement's uniform scale range;
- `role`: what the placement does with it: straight, corner, end, ledge, jut or rubble.
"""

SCANS = {
    "desert_corner": "desert_western_cliff_layered_corner_xl_01_ulfucbn_high.glb",
    "desert_xl04": "desert_western_cliff_layered_xl_04_ulcofaf_high.glb",
    "desert_large01": "desert_western_cliff_layered_large_01_venqdcc_high.glb",
    "desert_ledge_large": "desert_western_ledge_rock_large_03_vbvqbbofa_high.glb",
    "desert_ledge_medium": "desert_western_ledge_rock_medium_01_xgkneh0_high.glb",
    "desert_rock11": "desert_western_rock_large_11_ulbpfjj_high.glb",
    "desert_rock13": "desert_western_rock_large_13_ulmfceq_high.glb",
    "nordic_massive": "massive_nordic_coastal_cliff_vfmjadufa_high.glb",
    "nordic_huge_v": "huge_nordic_coastal_cliff_vefkehbva_high.glb",
    "nordic_huge_u": "huge_nordic_coastal_cliff_ullqbb1ga_high.glb",
    "nordic_forest": "nordic_forest_cliff_large_xibjbg3_high.glb",
    "nordic_boulder": "nordic_beach_boulder_vcyqebjfa_high.glb",
    "tundra_large": "large_tundra_rock_formation_wdysbfoga_high.glb",
    "tundra_massive": "massive_tundra_rock_formation_wd4icipcb_high.glb",
    "tundra_formation": "tundra_rock_formation_vi5nachmw_high.glb",
}

# The shared tiling detail of each family (Megascans surfaces, 4K folders in SOURCES), where the family goes, and its
# materials' default cover (moss, sand or grass where the rock faces up; a: how much) and ground colour (sRGB).
FAMILIES = {
    "desert": {"surface": "canyon_sandstone_rock_vimldgeg_4k", "stem": "Canyon_Sandstone_Rock_vimldgeg_4K",
               "places": ["tableland", "mesa", "desert"], "cover": (0.60, 0.47, 0.32, 0.8),
               "ground": (0.56, 0.41, 0.29, 1.0)},
    "nordic": {"surface": "cracked_nordic_coastal_rock_ukohaj0mw_4k", "stem": "Cracked_Nordic_Coastal_Rock_ukohaj0mw_4K",
               "places": ["coast"], "cover": (0.24, 0.29, 0.13, 1.0), "ground": (0.33, 0.31, 0.26, 1.0)},
    "tundra": {"surface": "tundra_lichened_rock_vjbode1mw_4k", "stem": "Tundra_Lichened_Rock_vjbode1mw_4K",
               "places": ["hills", "mountains"], "cover": (0.34, 0.36, 0.19, 1.0),
               "ground": (0.34, 0.33, 0.27, 1.0)},
}


def piece(key, family, role, scan, yaw, span=None, ends=("cut", "cut"), **options):
    spec = {"key": key, "family": family, "role": role, "scan": scan, "yaw": yaw, "span": span, "ends": ends,
            "angles": (-90.0, 90.0), "scale": 1.0, "mirror": False, "align": span is not None, "fill": True,
            "back": 6.0, "apron": 2.5, "lod0": 12000, "range": (0.6, 1.1)}
    spec.update(options)
    return spec


PIECES = [
    # Desert Western layered cliffs: tablelands, mesas and deserts.
    piece("desert-xl-a", "desert", "straight", "desert_xl04", -88, (-19.5, 0.5)),
    piece("desert-xl-b", "desert", "straight", "desert_xl04", -88, (-0.5, 18.0)),
    piece("desert-corner-xl", "desert", "corner", "desert_corner", -95, (-15.0, 15.0), angles=(-144.0, 145.0),
          align=False, lod0=20000, back=7.0),
    piece("desert-large-a", "desert", "straight", "desert_large01", -90, (-7.5, 7.0), lod0=5000, back=3.0,
          range=(0.8, 2.0)),
    piece("desert-large-b", "desert", "straight", "desert_ledge_large", -87, (-10.5, 1.5), lod0=5000, back=4.0,
          range=(0.8, 2.0)),
    piece("desert-ledge-a", "desert", "ledge", "desert_ledge_large", -87, (0.5, 10.5), lod0=5000, back=4.0,
          range=(0.8, 1.6)),
    piece("desert-ledge-b", "desert", "ledge", "desert_ledge_medium", -95, None, ("natural", "natural"), scale=2.0,
          fill=False, lod0=5000, range=(0.7, 1.5)),
    piece("desert-end-left", "desert", "end", "desert_xl04", -88, (-20.5, -4.0), ("taper", "cut"), lod0=10000),
    piece("desert-end-right", "desert", "end", "desert_xl04", -88, (4.0, 19.5), ("cut", "taper"), lod0=10000),
    # Nordic coastal cliffs: coasts.
    piece("nordic-tall-a", "nordic", "straight", "nordic_massive", -105, (-10.5, 2.5), scale=1.6, lod0=15000, fill=False,
          range=(0.7, 1.3)),
    piece("nordic-tall-b", "nordic", "straight", "nordic_huge_v", 160, (0.0, 9.5), scale=1.6, lod0=12000,
          range=(0.7, 1.3)),
    piece("nordic-tall-c", "nordic", "straight", "nordic_forest", -88, (-9.0, 8.5), scale=1.6, lod0=10000,
          range=(0.7, 1.3), back=4.0),
    piece("nordic-end-left", "nordic", "end", "nordic_huge_u", -95, (-8.0, 5.5), ("taper", "cut"), scale=1.6, fill=False,
          lod0=10000, range=(0.7, 1.3)),
    piece("nordic-end-right", "nordic", "end", "nordic_massive", -105, (1.0, 9.5), ("cut", "natural"), scale=1.6,
          fill=False,
          lod0=10000, range=(0.7, 1.3)),
    # Tundra formations: hills and mountains.
    piece("tundra-jut-a", "tundra", "jut", "tundra_large", -79, (-18.0, 0.0), lod0=10000, range=(0.7, 1.4)),
    piece("tundra-jut-b", "tundra", "jut", "tundra_large", -79, (-1.0, 18.5), lod0=10000, range=(0.7, 1.4)),
    piece("tundra-jut-c", "tundra", "jut", "tundra_massive", -100, None, ("natural", "natural"), lod0=10000,
          range=(0.7, 1.4)),
    piece("tundra-jut-d", "tundra", "jut", "tundra_formation", -95, None, ("natural", "natural"),
          lod0=5000, range=(0.8, 2.0)),
    # Rubble that fills the feet and the joints.
    piece("rubble-desert-a", "desert", "rubble", "desert_rock11", 0, None, ("natural", "natural"), fill=False, apron=0.6,
          lod0=3000, range=(0.6, 2.5)),
    piece("rubble-desert-b", "desert", "rubble", "desert_rock13", 0, None, ("natural", "natural"), fill=False, apron=0.6,
          lod0=3000, range=(0.6, 2.5)),
    piece("rubble-nordic", "nordic", "rubble", "nordic_boulder", 0, None, ("natural", "natural"), scale=2.5,
          fill=False, apron=0.6, lod0=3000, range=(0.6, 2.5)),
]


def find(key):
    return next(p for p in PIECES if p["key"] == key)
