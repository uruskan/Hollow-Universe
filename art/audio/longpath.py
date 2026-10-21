"""File paths past Windows' MAX_PATH (260 characters) without the system-wide long-path setting.

A checkout at a long path (a worktree under .claude\\worktrees) puts the cache's deepest members past that limit, where
open, os.path.isfile and older ffmpeg builds fail. `fs(path)` gives such a path in its extended-length form
(\\\\?\\D:\\...), which lifts the limit; every shorter path, and every path on other systems, comes back unchanged, so
a build at a normal path runs exactly as before. Pass `fs(path)` only to the call that touches the file system or to
ffmpeg; recipe keys, messages and returned paths keep the ordinary form.

machines.py hashes its own functions' source into its cache names, so its bodies don't call this: they read downloads
outside the repository and write the short synth cache.
"""

import os

PREFIX = "\\\\?\\"
# Under MAX_PATH with room to spare: CreateDirectory stops at 248 characters.
LIMIT = 240


def fs(path):
    if os.name != "nt" or path.startswith("\\\\"):
        return path
    full = os.path.abspath(path)
    return PREFIX + full if len(full) >= LIMIT else path
