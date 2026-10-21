"""Run comparison scripts through Cascadeur's installed official loopback MCP.

Scene edits belong only to the separate v3-comparison output directory. Never
retry a timed-out mutation automatically: first inspect its receipt and state.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.artifacts/character-trial-cascadeur-v3-comparison'
ENDPOINT = 'http://127.0.0.1:8765/mcp'


def call(method, params, request_id=1):
    data = json.dumps({'jsonrpc': '2.0', 'id': request_id,
                       'method': method, 'params': params}).encode()
    request = Request(ENDPOINT, data, {'Content-Type': 'application/json'})
    with urlopen(request, timeout=45) as response:
        return json.load(response)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('script', type=Path)
    parser.add_argument('--receipt', required=True)
    args = parser.parse_args()
    script = args.script.resolve()
    script.relative_to(Path(__file__).resolve().parent)
    path = (OUT / 'receipts' / (args.receipt + '.json')).resolve()
    path.relative_to(OUT.resolve())
    if path.exists():
        raise FileExistsError(path)
    code = script.read_text(encoding='utf-8')
    initialize = call('initialize', {'protocolVersion': '2024-11-05',
        'capabilities': {}, 'clientInfo': {'name': 'cascadeur-v3-comparison', 'version': '1.0'}})
    try:
        response = call('tools/call', {'name': 'run_script', 'arguments': {'code': code}}, 2)
    except Exception as exc:
        response = {'error': {'message': str(exc), 'outcome': 'Unknown; inspect state before retrying'}}
    receipt = {'script': str(script), 'sha256': hashlib.sha256(code.encode()).hexdigest(),
               'code': code, 'initialize': initialize, 'response': response}
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    failed = 'error' in response or response.get('result', {}).get('isError', False)
    for block in response.get('result', {}).get('content', []):
        if block.get('type') == 'text':
            payload = json.loads(block['text'])
            failed |= not payload.get('ok', False)
            for message in payload.get('messages', []):
                print(message.get('level', ''), message.get('text', ''))
            if payload.get('error'):
                print(payload['error'])
    if 'error' in response:
        print(json.dumps(response['error']))
    print('Receipt:', path)
    return int(failed)


if __name__ == '__main__':
    raise SystemExit(main())
