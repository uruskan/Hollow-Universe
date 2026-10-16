"""Read existing route JSON only; never launch, modify images, or claim overall FPS.

Run: python tools/compare_environment_routes.py --evidence docs/images/repairs/environment-2026-10-16
Final: python .artifacts/environment-pass/compare_routes.py --candidate-high candidate-route-high2
       --candidate-night candidate-route-max-night-final --output-prefix route-comparison-final
Missing candidate reports remain pending. Outputs are route-comparison.json/.md.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / ".artifacts" / "environment-pass"
PAIRS = (
    ("High / day", "baseline-route-high2", "candidate-route-high2"),
    ("Maximum / night", "baseline-route-max-night", "candidate-route-max-night"),
)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else None


def distance(a, b):
    if a is None or b is None or len(a) != len(b):
        return None
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def angle(a, b):
    if a is None or b is None:
        return None
    norm = math.sqrt(sum(x*x for x in a) * sum(x*x for x in b))
    return math.degrees(math.acos(max(-1, min(1, sum(x*y for x, y in zip(a, b)) / norm)))) if norm else None


def delta(a, b):
    return b - a if isinstance(a, (int, float)) and isinstance(b, (int, float)) else None


def statistics(profile):
    raw = profile.get("frameMs", [])
    valid = [v for v in raw if isinstance(v, (int, float)) and math.isfinite(v) and v > 0]
    values = sorted(valid)
    def rank(fraction):
        return values[max(0, math.ceil(len(values) * fraction) - 1)] if values else None
    result = {
        "rawCount": len(raw), "validCount": len(valid), "invalidCount": len(raw) - len(valid),
        "measuredSeconds": sum(valid) / 1000, "elapsedSeconds": profile.get("elapsedSeconds"),
        "meanMs": sum(valid) / len(valid) if valid else None,
        "medianMs": rank(.5), "p95Ms": rank(.95), "p99Ms": rank(.99),
        "worstMs": max(valid) if valid else None,
        "gpuMs": profile.get("gpuMs"), "renderCpuMs": profile.get("renderCpuMs"),
    }
    result["reportedStatisticsAgree"] = (profile.get("frames") == len(raw) and all(
        result[key] is not None and profile.get(key) is not None and
        abs(result[key] - profile[key]) < .001
        for key in ("measuredSeconds", "meanMs", "medianMs", "p95Ms", "p99Ms", "worstMs")))
    return result


def memory_fields(value, path=""):
    """Retain original key/unit; absent VRAM telemetry must not be reported as zero."""
    found = {}
    if isinstance(value, dict):
        for key, item in value.items():
            location = path + "." + key
            normalized = key.lower().replace("_", "")
            if any(term in normalized for term in ("vram", "videomem", "gpumemory")):
                found[location.lstrip(".")] = item
            elif isinstance(item, (dict, list)):
                found.update(memory_fields(item, location))
    elif isinstance(value, list):
        for i, item in enumerate(value):
            if isinstance(item, (dict, list)):
                found.update(memory_fields(item, f"{path}[{i}]"))
    return found


def summary(report, name):
    checkpoints = report.get("checkpoints", [])
    poses = [sample.get("pose", {}) for sample in report.get("trajectory", [])]
    launch = read(ROOT / (name + ".run.json")) or {}
    args = report.get("args", [])
    def option(key):
        try:
            return args[args.index("--" + key) + 1]
        except (ValueError, IndexError):
            return None
    return {
        "report": str(ROOT / name / "environment-route.json"),
        "technicalChecksPassed": report.get("technicalChecksPassed"), "failure": report.get("failure"),
        "elapsedSeconds": report.get("seconds"), "version": report.get("version"),
        "runtimeModuleVersionId": report.get("runtimeModuleVersionId"), "runtimeHash": report.get("runtimeHash"),
        "launchBinarySha256": launch.get("BinarySha256"), "launchUtc": launch.get("Utc"),
        "mode": report.get("mode"), "graphics": report.get("graphics"),
        "displayVsync": report.get("displayVsync"), "frameCap": report.get("frameCap"),
        "requestedHour": option("hour"), "requestedClouds": option("clouds"),
        "requestedProfileSeconds": option("environment-profile"),
        "readyCheckpoints": sum(c.get("ready") is True for c in checkpoints), "checkpoints": len(checkpoints),
        "groundedCheckpoints": sum("OnGround" in c.get("pose", {}).get("flags", "") for c in checkpoints),
        "trajectorySamples": len(poses), "sampledGrounded": sum("OnGround" in p.get("flags", "") for p in poses),
        "sampledOtherFrames": sum(p.get("Frame") != 0 for p in poses),
        "sampledCheats": sorted(set(str(p.get("cheats")) for p in poses)),
        "spawnConnectorMetres": (report.get("spawnConnector") or {}).get("metres"),
        "vramTelemetry": memory_fields(report) or None,
    }


def compare(title, before_name, after_name):
    before = read(ROOT / before_name / "environment-route.json")
    after = read(ROOT / after_name / "environment-route.json")
    result = {"pair": title, "baselineName": before_name, "candidateName": after_name,
              "status": "available" if before and after else "pending",
              "baseline": summary(before, before_name) if before else None,
              "candidate": summary(after, after_name) if after else None}
    if not before or not after:
        return result
    graphics_a, graphics_b = before.get("graphics", {}), after.get("graphics", {})
    result["graphicsDifferences"] = {key: {"baseline": graphics_a.get(key), "candidate": graphics_b.get(key)}
        for key in sorted(graphics_a.keys() | graphics_b.keys()) if graphics_a.get(key) != graphics_b.get(key)}
    result["samePlannedRoute"] = before.get("plan") == after.get("plan")
    result["elapsedDeltaSeconds"] = delta(before.get("seconds"), after.get("seconds"))
    result["runSettingDifferences"] = {key: {"baseline": result["baseline"][key], "candidate": result["candidate"][key]}
        for key in ("mode", "displayVsync", "frameCap", "requestedHour", "requestedClouds", "requestedProfileSeconds")
        if result["baseline"][key] != result["candidate"][key]}
    ca = {c["label"]: c for c in before.get("checkpoints", [])}
    cb = {c["label"]: c for c in after.get("checkpoints", [])}
    result["checkpointDifferences"] = []
    for label in sorted(ca.keys() | cb.keys()):
        a, b = ca.get(label), cb.get(label)
        row = {"label": label, "matched": a is not None and b is not None}
        if a and b:
            ac, bc = a.get("camera", {}), b.get("camera", {})
            al, bl = a.get("lighting", {}), b.get("lighting", {})
            row.update(playerOffsetMetres=distance(a["pose"].get("position"), b["pose"].get("position")),
                cameraOffsetMetres=distance(ac.get("position"), bc.get("position")),
                forwardAngleDegrees=angle(ac.get("forward"), bc.get("forward")),
                upAngleDegrees=angle(ac.get("up"), bc.get("up")), fovDeltaDegrees=delta(ac.get("Fov"), bc.get("Fov")),
                sameResolution=(a.get("width"), a.get("height")) == (b.get("width"), b.get("height")),
                baselineReady=a.get("ready"), candidateReady=b.get("ready"),
                baselineLighting=al, candidateLighting=bl,
                lightingDelta={key: delta(al.get(key), bl.get(key)) for key in
                    ("LocalHours", "SunElevation", "MoonElevation", "MoonPhase", "Exposure")},
                sunIlluminanceVectorDelta=distance(al.get("sun"), bl.get("sun")),
                moonIlluminanceVectorDelta=distance(al.get("moon"), bl.get("moon")))
            # Clock difference is cyclic; midnight's tiny negative offset is not a24h phase change.
            hours = row["lightingDelta"]["LocalHours"]
            if hours is not None: row["lightingDelta"]["LocalHours"] = (hours + 12) % 24 - 12
        result["checkpointDifferences"].append(row)
    pa = {p["stage"]: p for p in before.get("profiles", [])}
    pb = {p["stage"]: p for p in after.get("profiles", [])}
    result["profileDifferences"] = []
    for stage in dict.fromkeys([*pa, *pb]):
        a, b = statistics(pa[stage]) if stage in pa else None, statistics(pb[stage]) if stage in pb else None
        row = {"stage": stage, "baseline": a, "candidate": b}
        if a and b:
            row["candidateMinusBaseline"] = {key: delta(a[key], b[key]) for key in
                ("validCount", "measuredSeconds", "elapsedSeconds", "medianMs", "p95Ms", "p99Ms", "worstMs")}
        result["profileDifferences"].append(row)
    return result


def fmt(value, places=3):
    return "unavailable" if value is None else f"{value:.{places}f}"


def markdown(pairs):
    lines = ["# Matched route evidence", "", "Read-only comparison; all time deltas are candidate minus baseline.", "",
        "Frame statistics are recomputed from raw positive wall-clock samples using the harness's nearest-rank convention. "
        "Each leg covers at most20s and starts at that leg's beginning; counts and movement speed can differ. "
        "These are neither full-route/cold-load measurements nor an overall FPS claim. "
        "Lighting/exposure differences are reported as candidate behavior, not silently treated as matched. "
        "A launch/process roster and unedited images still require human review; JSON alone cannot prove an isolated GPU or visual quality.", ""]
    for pair in pairs:
        lines += ["## " + pair["pair"], ""]
        if pair["status"] == "pending":
            lines += ["Pending complete baseline/candidate report; no comparison asserted.", ""]
            continue
        a, b = pair["baseline"], pair["candidate"]
        lines += [f"Technical completion: {a['technicalChecksPassed']} → {b['technicalChecksPassed']}; "
            f"elapsed {fmt(a['elapsedSeconds'])} → {fmt(b['elapsedSeconds'])}s. "
            f"Ready checkpoints {a['readyCheckpoints']}/{a['checkpoints']} → {b['readyCheckpoints']}/{b['checkpoints']}. "
            f"Grounded trajectory samples {a['sampledGrounded']}/{a['trajectorySamples']} → {b['sampledGrounded']}/{b['trajectorySamples']}. "
            f"Same planned route: {pair['samePlannedRoute']}.", "",
            "Graphics differences: `" + json.dumps(pair["graphicsDifferences"]) + "`.",
            "Other run-setting differences: `" + json.dumps(pair["runSettingDifferences"]) + "`.", "",
            "| Checkpoint | Player Δm | Camera Δm | View angle Δ° | Solar-hour Δ | Sun elevation Δ° | Exposure Δ |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
        for c in pair["checkpointDifferences"]:
            if not c["matched"]: lines.append(f"| {c['label']} | unmatched | | | | | |"); continue
            light = c["lightingDelta"]
            lines.append(f"| {c['label']} | {fmt(c['playerOffsetMetres'])} | {fmt(c['cameraOffsetMetres'])} | "
                f"{fmt(c['forwardAngleDegrees'])} | {fmt(light['LocalHours'], 6)} | {fmt(light['SunElevation'])} | {fmt(light['Exposure'])} |")
        lines += ["", "| Leg | Raw valid n (base → candidate) | Sample seconds | Median ms | P95 ms | P99 ms | Worst ms |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
        for p in pair["profileDifferences"]:
            x, y = p["baseline"], p["candidate"]
            if not x or not y: lines.append(f"| {p['stage']} | unmatched | | | | | |"); continue
            cells = [f"{x['validCount']} → {y['validCount']}"] + [f"{fmt(x[key])} → {fmt(y[key])}" for key in
                ("measuredSeconds", "medianMs", "p95Ms", "p99Ms", "worstMs")]
            lines.append("| " + p["stage"] + " | " + " | ".join(cells) + " |")
        valid_profiles = [p[side] for p in pair["profileDifferences"] for side in ("baseline", "candidate") if p[side]]
        lines += ["", f"Raw sample validation: {sum(p['invalidCount'] for p in valid_profiles)} invalid samples; "
            f"reported statistics agree for {sum(p['reportedStatisticsAgree'] for p in valid_profiles)}/{len(valid_profiles)} profiles.", "",
            "VRAM telemetry: " + ("present in JSON" if a["vramTelemetry"] or b["vramTelemetry"] else
            "not recorded; unavailable, not zero") + ".", ""]
    return "\n".join(lines)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, default=ROOT, help="Directory containing the captured route folders and launch manifests")
    parser.add_argument("--candidate-high", default=PAIRS[0][2], help="High/day report folder under this script's directory")
    parser.add_argument("--candidate-night", default=PAIRS[1][2], help="Maximum/night report folder under this script's directory")
    parser.add_argument("--output-prefix", default="route-comparison", help="Output stem; use a distinct name to preserve rejected comparisons")
    args = parser.parse_args()
    ROOT = args.evidence.resolve()
    for name in (args.candidate_high, args.candidate_night, args.output_prefix):
        if name in ("", ".", "..") or Path(name).name != name or "/" in name or "\\" in name:
            parser.error("Names must be single folder/file names under the evidence directory")
    pairs = [compare(PAIRS[0][0], PAIRS[0][1], args.candidate_high),
             compare(PAIRS[1][0], PAIRS[1][1], args.candidate_night)]
    (ROOT / (args.output_prefix + ".json")).write_text(json.dumps({"pairs": pairs}, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    (ROOT / (args.output_prefix + ".md")).write_text(markdown(pairs).rstrip() + "\n", encoding="utf-8")
    for pair in pairs:
        print(pair["pair"] + ": " + pair["status"])
