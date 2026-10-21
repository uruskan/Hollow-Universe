"""Run a reviewed Python file through Cascadeur's official loopback MCP.

This client never launches Cascadeur, activates a license, or exports FBX.
Receipts and scene outputs are confined to the isolated Cascadeur trial.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / ".artifacts" / "character-trial-cascadeur"
ENDPOINT = "http://127.0.0.1:8765/mcp"


def call(method: str, params: dict, request_id: int) -> dict:
    body = json.dumps({"jsonrpc": "2.0", "id": request_id,
                       "method": method, "params": params}).encode()
    request = Request(ENDPOINT, body, {"Content-Type": "application/json"})
    with urlopen(request, timeout=40) as response:
        return json.load(response)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("script", type=Path)
    parser.add_argument("--receipt", required=True)
    args = parser.parse_args()
    script = args.script.resolve()
    script.relative_to(Path(__file__).resolve().parent)
    receipt = (OUT / "receipts" / (args.receipt + ".json")).resolve()
    receipt.relative_to(OUT.resolve())
    if receipt.exists():
        raise FileExistsError(receipt)
    code = script.read_text(encoding="utf-8")
    event_log = Path(os.environ["LOCALAPPDATA"]) / "Nekki Limited/Cascadeur/logs/cascadeur_log.log"
    log_start = event_log.stat().st_size if event_log.exists() else 0
    initialized = call("initialize", {
        "protocolVersion": "2024-11-05", "capabilities": {},
        "clientInfo": {"name": "isolated-cascadeur-trial", "version": "1.0"}}, 1)
    try:
        response = call("tools/call", {"name": "run_script",
                                       "arguments": {"code": code}}, 2)
    except Exception as error:
        # A transport failure may occur after a scene operation starts.
        # Record the uncertain result; never automatically retry a mutation.
        response = {"error": {"type": type(error).__name__, "message": str(error),
                              "outcome": "unknown; inspect scene and disk before retry"}}
    event_delta = ""
    if event_log.exists():
        with event_log.open("rb") as stream:
            stream.seek(log_start)
            event_delta = stream.read().decode("utf-8", errors="replace")
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps({"script": str(script), "code": code,
                                  "initialize": initialized, "response": response,
                                  "event_log_delta": event_delta},
                                 indent=2) + "\n", encoding="utf-8")
    result = response.get("result", {})
    failed = "error" in response or result.get("isError", False)
    if "error" in response:
        print(json.dumps(response["error"]))
    for content in result.get("content", []):
        if content.get("type") != "text":
            continue
        payload = json.loads(content["text"])
        failed |= not payload.get("ok", False)
        for message in payload.get("messages", []):
            print(message.get("level", ""), message.get("text", ""))
        if payload.get("error"):
            print(payload["error"])
    print("Receipt:", receipt)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
