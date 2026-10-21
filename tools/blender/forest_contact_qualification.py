"""Bridge a pinned, reviewed closed-module candidate into the runtime geometry contract.

This creates an INACTIVE geometry package with no support/planting permissions and
no recipes. It does not approve materials, collision clearance or terrain fitting.
Original render corner order, normals, UVs and PBR bytes remain untouched. UV/normal
seams use explicit actual-edge references, never approximate positional welding.
Run only under the root's serialized Python guard; fresh artifact output only.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
import importlib.util
from pathlib import Path
import shutil
import sys

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("_contact_qualification_preview", HERE / "forest_contact_preview.py")
preview = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = preview
spec.loader.exec_module(preview)
author, require = preview.author, preview.require


def compact_geometry_descriptors(document):
    """Prune superseded descriptors after additive patches, preserving every referenced byte.

    This restricted static-mesh operation does not rewrite binary buffers, vertices,
    triangles or primitive order. Old, now-unreferenced bytes may remain in a used
    buffer and remain charged as part of that complete dependency. The runtime
    decoder independently validates the resulting layouts and disjoint elements.
    """
    result = deepcopy(document)
    require(len(result.get("meshes", [])) == 1 and not result.get("animations")
            and not result.get("skins") and not result.get("extensions"), "Only a static formation mesh can compact descriptors")
    accessors, views, buffers = (result.get(key, []) for key in ("accessors", "bufferViews", "buffers"))
    require(1 <= len(accessors) <= 256 and 1 <= len(views) <= 256 and 1 <= len(buffers) <= 8,
            "Descriptor compaction input exceeds bounded tables")
    used = set()
    primitives = result["meshes"][0].get("primitives", [])
    require(1 <= len(primitives) <= 32, "Descriptor compaction requires bounded primitives")
    for primitive in primitives:
        require(not primitive.get("targets") and not primitive.get("extensions")
                and set(primitive.get("attributes", {})) <= {"POSITION", "NORMAL", "TEXCOORD_0", "TANGENT"},
                "Unsupported accessor reference in formation primitive")
        references = [primitive["indices"], *primitive["attributes"].values()]
        for value in references:
            require(type(value) is int and 0 <= value < len(accessors), "Accessor reference outside table")
            used.add(value)
    accessor_order = sorted(used)
    view_used = set()
    for index in accessor_order:
        accessor = accessors[index]
        require(not accessor.get("sparse") and not accessor.get("extensions"), "Unsupported accessor storage")
        value = accessor.get("bufferView")
        require(type(value) is int and 0 <= value < len(views), "Buffer view reference outside table")
        view_used.add(value)
    view_order = sorted(view_used)
    buffer_used = set()
    for index in view_order:
        view = views[index]
        require(not view.get("extensions"), "Unsupported buffer view storage")
        value = view.get("buffer")
        require(type(value) is int and 0 <= value < len(buffers), "Buffer reference outside table")
        buffer_used.add(value)
    buffer_order = sorted(buffer_used)
    accessor_map = {old: new for new, old in enumerate(accessor_order)}
    view_map = {old: new for new, old in enumerate(view_order)}
    buffer_map = {old: new for new, old in enumerate(buffer_order)}
    for primitive in primitives:
        primitive["indices"] = accessor_map[primitive["indices"]]
        primitive["attributes"] = {key: accessor_map[value] for key, value in primitive["attributes"].items()}
    result["accessors"] = [accessors[index] for index in accessor_order]
    result["bufferViews"] = [views[index] for index in view_order]
    result["buffers"] = [buffers[index] for index in buffer_order]
    for accessor in result["accessors"]: accessor["bufferView"] = view_map[accessor["bufferView"]]
    for view in result["bufferViews"]: view["buffer"] = buffer_map[view["buffer"]]
    return result


def flatten(rows):
    """Match FormationGltfDecoder's primitive/vertex/triangle declaration order."""
    require(1 <= len(rows) <= 32, "Primitive admission exceeded")
    vertices, triangles = [], []
    for row in rows:
        require(len(row["positions"]) <= 60000 - len(vertices) and
                len(row["triangles"]) <= 50000 - len(triangles), "Aggregate visual geometry admission exceeded")
        offset = len(vertices)
        vertices.extend(tuple(p) for p in row["positions"])
        for face in row["triangles"]:
            require(len(face) == 3 and all(type(v) is int and 0 <= v < len(row["positions"]) for v in face),
                    "Visual index escapes its primitive")
            triangles.append(tuple(offset + v for v in face))
    return vertices, triangles


def bind_loop(vertices, triangles, coordinates):
    """Every segment must be a real triangle edge, with EXACT ordered endpoints."""
    require(3 <= len(coordinates) <= 512 and len(set(coordinates)) == len(coordinates), "Invalid closed contact coordinates")
    wanted = {point: ordinal for ordinal, point in enumerate(coordinates)}
    representatives = [None] * len(coordinates)
    for vertex, point in enumerate(vertices):
        if point in wanted and representatives[wanted[point]] is None:
            representatives[wanted[point]] = vertex
    require(all(v is not None for v in representatives), "Contact coordinate absent from mesh")
    actual = [None] * len(coordinates)
    for face in triangles:
        for j in range(3):
            a, b = face[j], face[(j + 1) % 3]
            ia, ib = wanted.get(vertices[a]), wanted.get(vertices[b])
            if ia is None or ib is None:
                continue
            if (ia + 1) % len(coordinates) == ib:
                segment, endpoints = ia, (a, b)
            elif (ib + 1) % len(coordinates) == ia:
                segment, endpoints = ib, (b, a)
            else:
                continue
            if actual[segment] is None or endpoints < actual[segment]:
                actual[segment] = endpoints
    require(all(edge is not None for edge in actual), "Contact segment is not an actual mesh edge")
    return dict(loopKey="outer-contact", vertices=representatives,
                edgeVertices=[v for edge in actual for v in edge])


def exact_triangle_maps(physical, vertices, triangles):
    """Only equal ordered corners qualify; approximation is never promoted to support."""
    lookup = {}
    for ordinal, face in enumerate(triangles):
        points = tuple(vertices[v] for v in face)
        require(points not in lookup, "Ambiguous duplicate visual triangle")
        lookup[points] = ordinal
    result = []
    for ordinal, face in enumerate(physical["triangles"]):
        points = tuple(tuple(physical["vertices"][v]) for v in face[:3])
        if points in lookup:
            result.append([ordinal, lookup[points]])
    return result


def physical_topology(record):
    """Remove exact coincident authoring fan copies from physical storage only.

    No visual corner is merged. Every physical face retains its ordered coordinates;
    no near-equality threshold, retriangulation, hole filling or triangle reordering.
    """
    require(len(record["vertices"]) <= 8192 and len(record["triangles"]) <= 16384, "Physical runtime count admission exceeded")
    vertices, by_point, remap = [], {}, []
    for p in record["vertices"]:
        point = tuple(p)
        if point not in by_point:
            by_point[point] = len(vertices); vertices.append(p)
        remap.append(by_point[point])
    triangles, edges = [], {}
    for face in record["triangles"]:
        require(len(face) == 3 and all(type(v) is int and 0 <= v < len(remap) for v in face), "Invalid physical input face")
        triangle = [remap[v] for v in face]
        require(len(set(triangle)) == 3, "Exact physical indexing exposed a collapsed face")
        triangles.append(triangle + [0])
        for j in range(3):
            a,b = triangle[j],triangle[(j+1)%3]
            key = min(a,b),max(a,b)
            previous = edges.get(key)
            if previous is None:edges[key]=(a,b,1)
            else:
                require(previous == (b,a,1), "Exact physical edge is nonmanifold or inconsistently wound")
                edges[key]=(a,b,2)
    require(all(edge[2] == 2 for edge in edges.values()), "Exact physical topology still contains an open edge")
    return dict(vertices=vertices, triangles=triangles,
                patches=[dict(key="closed-bare", substrateProfileKey="qualification-bare", capabilities=[])]), remap


def bridge(options):
    destination = preview.safe_path(options.destination, exists=False)
    require(destination.is_relative_to(preview.ROOT / ".artifacts") and not destination.exists() and destination.parent.is_dir(),
            "Qualification destination must be fresh under project artifacts")
    args = deepcopy(options)
    args.view, args.light, args.closure_uv_checker = "front", "neutral", False
    args.lod = "near"
    near = preview.authenticate(args)
    args.lod = "far"
    far = preview.authenticate(args)
    package, data, _, _, pins, work, evidence = near
    require(data == far[1] and pins == far[4], "Candidate changed between LOD reads")
    candidate_receipt = author.strict_json(package / "module-candidate-receipt.json", 1024 * 1024, options.receipt_sha256)
    parent = author.strict_json(preview.ROOT / candidate_receipt["sourcePackage"] / "qualification-receipt.json",
                                1024 * 1024, candidate_receipt["sourceReceiptSha256"])
    record = data["meshes"]["physical"]
    physical, physical_remap = physical_topology(record)
    coordinates = [tuple(data["closure"]["points"][v]) for v in data["closure"]["outerGuide"]]
    physical_binding = bind_loop([tuple(p) for p in physical["vertices"]], [t[:3] for t in physical["triangles"]], coordinates)
    require(physical_binding["edgeVertices"] == [v for i, a in enumerate(physical_binding["vertices"])
            for v in (a, physical_binding["vertices"][(i + 1) % len(coordinates)])],
            "Physical loop must remain one indexed cycle without seam aliases")
    lods, copy_names, proof = [], set(), {}
    for key, prepared in (("near", near), ("far", far)):
        document, rows = prepared[2], prepared[3]
        vertices, triangles = flatten(rows)
        binding = bind_loop(vertices, triangles, coordinates)
        maps = exact_triangle_maps(physical, vertices, triangles)
        if key == "far": require(len(maps) == len(physical["triangles"]), "Far LOD must retain every physical triangle exactly")
        asset = key + ".gltf"
        lods.append(dict(key=key, assetPath=asset, contentSha256=pins[asset]["sha256"],
                         maximumPositionErrorMetres=0, minimumNormalDot=.999999999999,
                         triangleMaps=maps, contactLoops=[binding]))
        copy_names.add(asset)
        for field in ("buffers", "images"):
            for row in document.get(field, []):
                path = author.uri_file(package, row["uri"])
                require(path.name in pins, "Unpinned qualification dependency")
                copy_names.add(path.name)
        proof[key] = dict(vertices=len(vertices), triangles=len(triangles), exactMappedTriangles=len(maps),
                          explicitSeamSegments=len(coordinates))
    require(len(copy_names) <= 24 and sum(pins[n]["bytes"] for n in copy_names) <= 128 * 1024 * 1024,
            "Qualification copy admission exceeded")
    work.check()
    destination.mkdir()
    for name in sorted(copy_names):
        with (package / name).open("rb") as incoming, (destination / name).open("xb") as outgoing:
            shutil.copyfileobj(incoming, outgoing, 1024 * 1024)
        require((destination / name).stat().st_size == pins[name]["bytes"] and author.sha256(destination / name) == pins[name]["sha256"],
                "Source dependency changed during copy")
        work.check()
    author.write_new(destination / "physical.json", author.canonical(physical))
    definition = dict(key="nordic-forest-contact", revision=1, familyKey="forest-cliff",
                      sourceSha256=parent["source"]["sha256"], physicalSha256=author.sha256(destination / "physical.json"),
                      units="metres", basis="right-handed-y-up", minimumScale=1, maximumScale=1,
                      physical=physical, domains=[dict(patchKey="closed-bare", boundaries=[])], contacts=[],
                      contactLoops=[dict(key="outer-contact", vertices=physical_binding["vertices"])], lods=lods)
    profile = dict(key="qualification-bare", bedrock=1, looseStone=0, soil=0, soilDepthMetres=0,
                   plantSuitability=0, grassCoverage=0, shrubsPerSquareMetre=0, treesPerSquareMetre=0,
                   litter=0, minimumTemperatureC=0, maximumTemperatureC=40, minimumSoilUpDot=.6)
    author.write_new(destination / "sidecar.json", author.canonical(dict(schemaVersion=1, definitions=[definition], profiles=[profile], recipes=[])))
    author.write_new(destination / "bridge-proof.json", author.canonical(dict(schemaVersion=1, assetAccepted=False,
        moduleReceiptSha256=options.receipt_sha256, surfaceCertificateSha256=options.certificate_sha256,
        surfaceErrorTransfer=evidence["surfaceErrorTransfer"], lods=proof, contactVertices=len(coordinates),
        permissions=[], recipes=[], canonicalVisualOrder="primitive-declaration/vertex/triangle-v1",
        physicalTopology="stable-exact-position-indexing-no-coordinate-or-face-order-change-v1",
        physicalInputToCanonicalVertex=physical_remap, physicalVertices=len(physical["vertices"]))))
    # Last write is the receipt; a failed output has no complete admission token.
    files = [dict(file=p.name, bytes=p.stat().st_size, sha256=author.sha256(p)) for p in sorted(destination.iterdir())]
    receipt = dict(schemaVersion=1, status="inactive-geometry-contract-no-art-or-support-approval", assetAccepted=False,
                   source=parent["source"], manifest=parent["manifest"], outputFiles=files,
                   moduleReceiptSha256=options.receipt_sha256, toolSha256=author.sha256(Path(__file__)),
                   pending=["explicit source tangents", "soil material authoring", "terrain fit", "support and habitat qualification", "native visual and performance acceptance"])
    author.write_new(destination / "qualification-receipt.json", author.canonical(receipt))
    return dict(status=receipt["status"], receiptSha256=author.sha256(destination / "qualification-receipt.json"), lods=proof)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--receipt-sha256", required=True)
    parser.add_argument("--surface-certificate", type=Path, required=True)
    parser.add_argument("--certificate-sha256", required=True)
    parser.add_argument("--destination", type=Path, required=True)
    import json
    print(json.dumps(bridge(parser.parse_args()), sort_keys=True))


if __name__ == "__main__":
    main()
