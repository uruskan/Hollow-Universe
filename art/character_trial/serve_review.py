"""Serve an isolated review folder on localhost with seekable HTTP media.

python art/character_trial/serve_review.py --root .artifacts/character-trial-v2 --port 8873
"""
import argparse
import os
from email.utils import parsedate_to_datetime
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import re
import stat
from urllib.parse import unquote, urlsplit


class ReviewHandler(SimpleHTTPRequestHandler):
    """Read-only, directory-confined file responses supporting one byte range."""

    def send_head(self):
        self.remaining = None
        root = Path(self.directory).resolve()
        try:
            relative = unquote(urlsplit(self.path).path).lstrip('/')
            path = (root / relative).resolve()
            if not path.is_relative_to(root):
                self.send_error(403, 'Path is outside the review folder')
                return None
            if path.is_dir():
                path = (path / 'index.html').resolve()
                if not path.is_relative_to(root):
                    self.send_error(403, 'Path is outside the review folder')
                    return None
            stream = path.open('rb')
            info = os.fstat(stream.fileno())
        except (OSError, ValueError):
            self.send_error(404, 'File not found')
            return None
        if not stat.S_ISREG(info.st_mode):
            stream.close()
            self.send_error(404, 'File not found')
            return None
        size = info.st_size
        etag = f'"{info.st_mtime_ns:x}-{size:x}"'
        requested = self.headers.get('Range')
        if_range = self.headers.get('If-Range')
        if requested and if_range and if_range != etag:
            try:
                unchanged = int(info.st_mtime) <= int(parsedate_to_datetime(if_range).timestamp())
            except (ValueError, TypeError, OverflowError):
                unchanged = False
            if not unchanged:
                requested = None
        start, end = 0, size - 1
        if requested:
            match = re.fullmatch(r'bytes=(\d*)-(\d*)', requested.strip())
            valid = match is not None and bool(match[1] or match[2]) and size > 0
            if valid:
                try:
                    if match[1]:
                        start = int(match[1])
                        end = min(int(match[2]), size - 1) if match[2] else size - 1
                        valid = start < size and start <= end
                    else:
                        suffix = int(match[2])
                        valid = suffix > 0
                        start = max(0, size - suffix)
                except ValueError:
                    valid = False
            if not valid:
                stream.close()
                self.send_response(416)
                self.send_header('Content-Range', f'bytes */{size}')
                self.send_header('Content-Length', '0')
                self.common_headers(etag)
                self.end_headers()
                return None
        length = end - start + 1
        self.send_response(206 if requested else 200)
        self.send_header('Content-Type', self.guess_type(str(path)))
        self.send_header('Content-Length', str(length))
        self.send_header('Last-Modified', self.date_time_string(info.st_mtime))
        if requested:
            self.send_header('Content-Range', f'bytes {start}-{end}/{size}')
        self.common_headers(etag)
        self.end_headers()
        stream.seek(start)
        self.remaining = length
        return stream

    def common_headers(self, etag):
        self.send_header('Accept-Ranges', 'bytes')
        self.send_header('Cache-Control', 'no-store')
        self.send_header('ETag', etag)
        self.send_header('X-Content-Type-Options', 'nosniff')

    def copyfile(self, source, output):
        remaining = self.remaining
        try:
            while remaining:
                block = source.read(min(128 * 1024, remaining))
                if not block:
                    break
                output.write(block)
                remaining -= len(block)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass  # Browser cancellation while changing clips is normal.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', required=True, type=Path)
    parser.add_argument('--port', type=int, default=8873)
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    if not root.is_dir():
        parser.error('--root must name a directory')
    handler = partial(ReviewHandler, directory=str(root))
    with ThreadingHTTPServer(('127.0.0.1', args.port), handler) as server:
        print(f'Serving {root} at http://127.0.0.1:{args.port}/index.html', flush=True)
        server.serve_forever()


if __name__ == '__main__':
    main()
