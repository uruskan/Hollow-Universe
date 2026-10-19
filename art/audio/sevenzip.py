"""A small 7z reader on Python's standard library (lzma, zlib), enough for the CC0 sound packs: LZMA, LZMA2, BCJ,
Delta and Copy coders, plain or encoded headers, solid folders. Folders are decoded as a stream straight to disk, so a
big archive never sits in memory. Archives are untrusted data: every name is checked to stay inside the output folder.

    python -I art/audio/sevenzip.py <archive.7z> <out-dir>      (list with --list)
"""

import lzma
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from longpath import fs  # noqa: E402

SIGNATURE = b"7z\xbc\xaf\x27\x1c"
CHUNK = 1 << 20


class Bad(Exception):
    pass


class Reader:
    def __init__(self, data):
        self.d = data
        self.p = 0

    def byte(self):
        if self.p >= len(self.d):
            raise Bad("header ends early")
        b = self.d[self.p]
        self.p += 1
        return b

    def take(self, n):
        if n < 0 or self.p + n > len(self.d):
            raise Bad("header ends early")
        b = self.d[self.p:self.p + n]
        self.p += n
        return b

    def u32(self):
        return struct.unpack("<I", self.take(4))[0]

    def number(self):
        first = self.byte()
        mask = 0x80
        value = 0
        for i in range(8):
            if first & mask == 0:
                return value | ((first & (mask - 1)) << (8 * i))
            value |= self.byte() << (8 * i)
            mask >>= 1
        return value

    def bits(self, n):
        out = []
        mask = 0
        b = 0
        for _ in range(n):
            if mask == 0:
                b = self.byte()
                mask = 0x80
            out.append(bool(b & mask))
            mask >>= 1
        return out

    def defined(self, n):
        return [True] * n if self.byte() else self.bits(n)


class Coder:
    def __init__(self, cid, ins, outs, props):
        self.cid, self.ins, self.outs, self.props = cid, ins, outs, props


class Folder:
    def __init__(self):
        self.coders = []
        self.bind = []
        self.packed = []
        self.sizes = []
        self.crc = None

    def out_count(self):
        return sum(c.outs for c in self.coders)

    def unpack_size(self):
        bound = {o for (_, o) in self.bind}
        for i in range(self.out_count()):
            if i not in bound:
                return self.sizes[i]
        raise Bad("folder without a main stream")

    def filters(self):
        """liblzma's raw filter chain for this folder (single-stream coders in a chain only)."""
        n = len(self.coders)
        if any(c.ins != 1 or c.outs != 1 for c in self.coders):
            raise Bad("complex coders (BCJ2) are not supported")
        if sorted(self.bind) != [(i, i + 1) for i in range(n - 1)] or self.packed != [n - 1]:
            raise Bad("unsupported coder layout")
        chain = []
        for c in self.coders:
            if c.cid == b"\x00":
                continue
            if c.cid == b"\x21":
                p = c.props[0]
                size = 0xFFFFFFFF if p >= 40 else (2 | (p & 1)) << (p // 2 + 11)
                chain.append({"id": lzma.FILTER_LZMA2, "dict_size": size})
            elif c.cid == b"\x03\x01\x01":
                d = c.props[0]
                lc, d = d % 9, d // 9
                lp, pb = d % 5, d // 5
                size = max(struct.unpack("<I", c.props[1:5])[0], 4096)
                chain.append({"id": lzma.FILTER_LZMA1, "lc": lc, "lp": lp, "pb": pb, "dict_size": size})
            elif c.cid == b"\x03\x03\x01\x03":
                chain.append({"id": lzma.FILTER_X86})
            elif c.cid == b"\x03":
                chain.append({"id": lzma.FILTER_DELTA, "dist": c.props[0] + 1})
            else:
                raise Bad("unsupported coder %s" % c.cid.hex())
        return chain


def read_pack_info(r):
    pos = r.number()
    n = r.number()
    sizes = []
    while True:
        t = r.byte()
        if t == 0x00:
            return pos, sizes
        if t == 0x09:
            sizes = [r.number() for _ in range(n)]
        elif t == 0x0A:
            for d in r.defined(n):
                if d:
                    r.u32()
        else:
            raise Bad("pack info: property %d" % t)


def read_folder(r):
    f = Folder()
    for _ in range(r.number()):
        flags = r.byte()
        cid = r.take(flags & 0x0F)
        ins, outs = (r.number(), r.number()) if flags & 0x10 else (1, 1)
        props = r.take(r.number()) if flags & 0x20 else b""
        if flags & 0x80:
            raise Bad("alternative coder methods are not supported")
        f.coders.append(Coder(cid, ins, outs, props))
    total_in = sum(c.ins for c in f.coders)
    f.bind = [(r.number(), r.number()) for _ in range(f.out_count() - 1)]
    packed = total_in - len(f.bind)
    if packed == 1:
        bound = {i for (i, _) in f.bind}
        f.packed = [i for i in range(total_in) if i not in bound]
    else:
        f.packed = [r.number() for _ in range(packed)]
    return f


def read_unpack_info(r):
    if r.byte() != 0x0B:
        raise Bad("unpack info: no folders")
    n = r.number()
    if r.byte() != 0:
        raise Bad("external folders are not supported")
    folders = [read_folder(r) for _ in range(n)]
    if r.byte() != 0x0C:
        raise Bad("unpack info: no sizes")
    for f in folders:
        f.sizes = [r.number() for _ in range(f.out_count())]
    while True:
        t = r.byte()
        if t == 0x00:
            return folders
        if t == 0x0A:
            for f, d in zip(folders, r.defined(n)):
                if d:
                    f.crc = r.u32()
        else:
            raise Bad("unpack info: property %d" % t)


def read_substreams(r, folders):
    counts = [1] * len(folders)
    t = r.byte()
    if t == 0x0D:
        counts = [r.number() for _ in folders]
        t = r.byte()
    sizes = []
    for f, c in zip(folders, counts):
        if c == 0:
            sizes.append([])
        elif t == 0x09:
            s = [r.number() for _ in range(c - 1)]
            s.append(f.unpack_size() - sum(s))
            sizes.append(s)
        elif c == 1:
            sizes.append([f.unpack_size()])
        else:
            raise Bad("substreams without sizes")
    if t == 0x09:
        t = r.byte()
    crcs = [[None] * len(s) for s in sizes]
    for i, (f, c) in enumerate(zip(folders, counts)):
        if c == 1 and f.crc is not None:
            crcs[i][0] = f.crc
    while t != 0x00:
        if t == 0x0A:
            unknown = [(i, j) for i, (f, c) in enumerate(zip(folders, counts)) for j in range(c)
                       if not (c == 1 and f.crc is not None)]
            for (i, j), d in zip(unknown, r.defined(len(unknown))):
                if d:
                    crcs[i][j] = r.u32()
        else:
            raise Bad("substreams: property %d" % t)
        t = r.byte()
    return [list(zip(s, c)) for s, c in zip(sizes, crcs)]


def read_streams(r):
    pos, packs, folders, subs = 0, [], [], None
    while True:
        t = r.byte()
        if t == 0x00:
            break
        if t == 0x06:
            pos, packs = read_pack_info(r)
        elif t == 0x07:
            folders = read_unpack_info(r)
        elif t == 0x08:
            subs = read_substreams(r, folders)
        else:
            raise Bad("streams info: property %d" % t)
    if subs is None:
        subs = [[(f.unpack_size(), f.crc)] for f in folders]
    return pos, packs, folders, subs


def read_files(r):
    n = r.number()
    empty = [False] * n
    files = []
    names = [""] * n
    while True:
        t = r.byte()
        if t == 0x00:
            # An empty entry is a directory unless kEmptyFile says it's an empty file.
            k = 0
            kinds = []
            for e in empty:
                kinds.append("file" if not e else ("empty" if k < len(files) and files[k] else "dir"))
                k += e
            return names, kinds
        size = r.number()
        sub = Reader(r.take(size))
        if t == 0x0E:
            empty = sub.bits(n)
        elif t == 0x0F:
            files = sub.bits(sum(empty))
        elif t == 0x11:
            if sub.byte() != 0:
                raise Bad("external names are not supported")
            names = sub.d[1:].decode("utf-16-le").split("\x00")[:n]


def decode(fh, offset, packed_size, folder, out_size, sink):
    """Decodes one folder from the archive at `offset`, giving its output to sink(bytes) in order."""
    chain = folder.filters()
    fh.seek(offset)
    left = packed_size
    dec = lzma.LZMADecompressor(lzma.FORMAT_RAW, filters=chain) if chain else None
    done = 0
    while done < out_size:
        if dec is None:
            data = fh.read(min(CHUNK, left))
            left -= len(data)
            if not data:
                raise Bad("archive ends early")
        elif dec.needs_input:
            chunk = fh.read(min(CHUNK, left)) if left > 0 else b""
            left -= len(chunk)
            if not chunk:
                raise Bad("archive ends early")
            data = dec.decompress(chunk, 4 * CHUNK)
        else:
            data = dec.decompress(b"", 4 * CHUNK)
        if done + len(data) > out_size:
            data = data[:out_size - done]
        if data:
            sink(data)
            done += len(data)


class Archive:
    def __init__(self, path):
        self.path = path
        with open(fs(path), "rb") as fh:
            head = fh.read(32)
            if len(head) < 32 or head[:6] != SIGNATURE:
                raise Bad("not a 7z archive")
            off, size, crc = struct.unpack("<QQI", head[12:32])
            fh.seek(32 + off)
            data = fh.read(size)
            if len(data) != size or zlib.crc32(data) != crc:
                raise Bad("header checksum")
            r = Reader(data)
            t = r.byte()
            while t == 0x17:
                pos, packs, folders, _ = read_streams(r)
                out = bytearray()
                decode(fh, 32 + pos, packs[0], folders[0], folders[0].unpack_size(), out.extend)
                if folders[0].crc is not None and zlib.crc32(out) != folders[0].crc:
                    raise Bad("encoded header checksum")
                r = Reader(bytes(out))
                t = r.byte()
            if t != 0x01:
                raise Bad("no header")
            self.pos, self.packs, self.folders, self.subs = 0, [], [], []
            self.names, self.kinds = [], []
            t = r.byte()
            if t == 0x02:
                while r.byte() != 0x00:
                    r.take(r.number())
                t = r.byte()
            if t == 0x03:
                read_streams(r)
                t = r.byte()
            if t == 0x04:
                self.pos, self.packs, self.folders, self.subs = read_streams(r)
                t = r.byte()
            if t == 0x05:
                self.names, self.kinds = read_files(r)
                t = r.byte()
            if t != 0x00:
                raise Bad("header: property %d" % t)

    def entries(self):
        """(name, size or None for directories and empty files) in archive order."""
        sizes = [s for folder in self.subs for (s, _) in folder]
        k = 0
        out = []
        for name, kind in zip(self.names, self.kinds):
            if kind != "file":
                out.append((name, None if kind == "dir" else 0))
            else:
                out.append((name, sizes[k]))
                k += 1
        return out

    def extract(self, out_dir):
        root = os.path.realpath(out_dir)
        streams = [n for n, kind in zip(self.names, self.kinds) if kind == "file"]
        for name, kind in zip(self.names, self.kinds):
            if kind == "dir":
                os.makedirs(fs(safe(root, name)), exist_ok=True)
            elif kind == "empty":
                path = safe(root, name)
                os.makedirs(fs(os.path.dirname(path)), exist_ok=True)
                open(fs(path), "wb").close()
        k = 0
        offset = 32 + self.pos
        pack_index = 0
        with open(fs(self.path), "rb") as fh:
            for folder, subs in zip(self.folders, self.subs):
                packed = sum(self.packs[pack_index:pack_index + len(folder.packed)])
                if len(folder.packed) != 1:
                    raise Bad("folders with several packed streams are not supported")
                files = []
                for size, crc in subs:
                    files.append((safe(root, streams[k]), size, crc))
                    k += 1
                writer = Splitter(files)
                decode(fh, offset, self.packs[pack_index], folder, sum(s for (_, s, _) in files), writer.write)
                writer.finish()
                offset += packed
                pack_index += len(folder.packed)


class Splitter:
    """Writes a folder's output into its files in turn, checking each file's CRC."""

    def __init__(self, files):
        self.files = files
        self.i = -1
        self.fh = None
        self.left = 0
        self.crc = 0
        self.next()

    def next(self):
        if self.fh:
            self.fh.close()
            path, _, crc = self.files[self.i]
            if crc is not None and self.crc != crc:
                raise Bad("checksum of %s" % path)
        self.i += 1
        self.fh = None
        while self.i < len(self.files):
            path, size, _ = self.files[self.i]
            os.makedirs(fs(os.path.dirname(path)), exist_ok=True)
            self.fh = open(fs(path), "wb")
            self.left = size
            self.crc = 0
            if size > 0:
                return
            self.fh.close()
            self.fh = None
            self.i += 1

    def write(self, data):
        view = memoryview(data)
        while len(view):
            if self.fh is None:
                raise Bad("more data than files")
            n = min(self.left, len(view))
            self.fh.write(view[:n])
            self.crc = zlib.crc32(view[:n], self.crc)
            self.left -= n
            view = view[n:]
            if self.left == 0:
                self.next()

    def finish(self):
        if self.fh is not None:
            raise Bad("archive data ends early")


def safe(root, name):
    """The path of an archive member inside root; refuses absolute names and '..'."""
    name = name.replace("\\", "/")
    parts = [p for p in name.split("/") if p not in ("", ".")]
    if not parts or any(p == ".." or ":" in p for p in parts):
        raise Bad("unsafe name %r" % name)
    path = os.path.realpath(os.path.join(root, *parts))
    if not (path + os.sep).startswith(root + os.sep):
        raise Bad("unsafe name %r" % name)
    return path


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "--list":
        for name, size in Archive(sys.argv[2]).entries():
            print("%12s  %s" % ("dir" if size is None else size, name))
    elif len(sys.argv) == 3:
        Archive(sys.argv[1]).extract(sys.argv[2])
    else:
        print(__doc__)
        sys.exit(2)
