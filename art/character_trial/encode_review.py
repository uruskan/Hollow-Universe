"""Encode completed frame manifests and validate every resulting review MP4.

python art/character_trial/encode_review.py --out .artifacts/character-trial-v2
Optional --view tps|fps|both, --clip NAME, --ffmpeg PATH. No Blender dependency.
Preflights every selected frame set before starting any encoding. Existing good
videos are replaced only after the new temporary MP4 decodes and validates.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from fractions import Fraction
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / '.artifacts/character-trial-v2'
DEFAULT_FFMPEG = Path('D:/GameDev/Tools/ScreenCapture/ShareX/ffmpeg.exe')
EXPECTED = ('idle', 'walk', 'sprint', 'crouch_walk', 'jump_land',
            'rock_strike', 'hatchet_chop', 'torch_equip')
FPS = 30


def now():
    return datetime.now(timezone.utc).isoformat(timespec='seconds')


def sha256(path):
    value = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def write_json(path, data):
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(data, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    temporary.replace(path)


def load_clips(out, view):
    path = out / ('fps-clips.json' if view == 'fps' else 'clips.json')
    data = json.loads(path.read_text(encoding='utf-8'))
    if not isinstance(data, list) or len(data) != 8 or {c.get('name') for c in data} != set(EXPECTED):
        raise ValueError(f'{path}: expected exactly the eight agreed clip names')
    return data


def preflight(out, view, clip):
    name = clip['name']
    directory = out / 'frames' / view / name
    manifest_path = directory / 'frames.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    prefix = f'{view}/{name}'
    if manifest.get('complete') is not True:
        raise ValueError(f'{prefix}: frames.json is not complete; wait for rendering to finish')
    if manifest.get('view') != view or manifest.get('clip') != name:
        raise ValueError(f'{prefix}: manifest identifies a different view or clip')
    if manifest.get('fps') != FPS or int(clip.get('fps', FPS)) != FPS:
        raise ValueError(f'{prefix}: expected {FPS} fps source timing')
    duration = float(clip['duration'])
    if not math.isfinite(duration) or duration <= 0:
        raise ValueError(f'{prefix}: invalid clip duration')
    count = round(duration * FPS)
    if abs(count / FPS - duration) > 1.e-6:
        raise ValueError(f'{prefix}: duration is not a whole number of {FPS} fps frames')
    if manifest.get('frames_planned') != count or len(manifest.get('frames', [])) != count:
        raise ValueError(f'{prefix}: expected {count} complete frame records')
    if abs(float(manifest.get('duration_seconds', -1)) - duration) > 1.e-6:
        raise ValueError(f'{prefix}: rendered duration differs from clip metadata')
    source_range = manifest.get('source_frame_range', [])
    if (len(source_range) != 2 or not all(isinstance(v, int) for v in source_range)
            or source_range[1] - source_range[0] != count or manifest.get('end_frame_excluded') is not True):
        raise ValueError(f'{prefix}: inconsistent exclusive-end source frame range')
    resolution = manifest.get('resolution', [])
    if (len(resolution) != 2 or not all(isinstance(v, int) and v > 0 and v % 2 == 0 for v in resolution)):
        raise ValueError(f'{prefix}: yuv420p requires positive even image dimensions')
    total_bytes = 0
    for index, frame in enumerate(manifest['frames']):
        filename = f'{index:04d}.png'
        if (frame.get('index') != index or frame.get('file') != filename
                or frame.get('source_frame') != source_range[0] + index
                or abs(float(frame.get('time_seconds', -1)) - index / FPS) > 1.e-6):
            raise ValueError(f'{prefix}: nonconsecutive filename/index/source time at frame {index}')
        path = directory / filename
        if not path.is_file():
            raise FileNotFoundError(f'{prefix}: missing image {filename}')
        with path.open('rb') as stream:
            header = stream.read(24)
        if (len(header) != 24 or header[:8] != b'\x89PNG\r\n\x1a\n'
                or header[12:16] != b'IHDR' or list(struct.unpack('>II', header[16:24])) != resolution):
            raise ValueError(f'{prefix}: invalid PNG header or dimensions: {filename}')
        total_bytes += path.stat().st_size
    return {'view': view, 'clip': name, 'directory': directory, 'count': count,
            'duration': duration, 'resolution': resolution,
            'manifest': manifest_path, 'manifest_sha256': sha256(manifest_path),
            'source_frame_bytes': total_bytes}


def run(command, timeout=60):
    result = subprocess.run([str(v) for v in command], shell=False, capture_output=True,
                            text=True, encoding='utf-8', errors='replace', timeout=timeout,
                            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if result.returncode:
        detail = (result.stderr or result.stdout)[-6000:]
        raise RuntimeError(f'{Path(str(command[0])).name} exited {result.returncode}:\n{detail}')
    return result


def find_ffprobe(ffmpeg):
    sibling = ffmpeg.with_name('ffprobe' + ffmpeg.suffix)
    if sibling.is_file():
        return sibling
    found = shutil.which('ffprobe')
    return Path(found) if found else None


def faststart(path):
    """Check top-level MP4 atoms directly rather than trusting encoder options."""
    atoms = []
    size = path.stat().st_size
    with path.open('rb') as stream:
        while stream.tell() + 8 <= size:
            position = stream.tell()
            length, kind = struct.unpack('>I4s', stream.read(8))
            if length == 1:
                raw = stream.read(8)
                if len(raw) != 8:
                    raise ValueError('Truncated MP4 extended atom header')
                length = struct.unpack('>Q', raw)[0]
            elif length == 0:
                length = size - position
            if length < stream.tell() - position or position + length > size:
                raise ValueError('Invalid MP4 atom size')
            atoms.append(kind.decode('ascii', errors='replace'))
            stream.seek(position + length)
    return 'moov' in atoms and 'mdat' in atoms and atoms.index('moov') < atoms.index('mdat')


def validate_video(path, job, ffmpeg, ffprobe):
    # Always decode every output frame, including when ffprobe is available.
    decoded = run([ffmpeg, '-hide_banner', '-v', 'info', '-threads', '2', '-i', path,
                   '-map', '0:v:0', '-an', '-threads', '2', '-filter_threads', '1',
                   '-progress', 'pipe:1', '-nostats', '-f', 'null', '-'])
    progress = {}
    for line in decoded.stdout.splitlines():
        if '=' in line:
            key, value = line.split('=', 1)
            progress[key.strip()] = value.strip()
    decoded_frames = int(progress.get('frame', '-1'))
    if decoded_frames != job['count'] or progress.get('progress') != 'end':
        raise ValueError(f'Decoded frame count/end marker differs: {decoded_frames}, expected {job["count"]}')
    if re.search(r'(?i)error|corrupt|invalid data', decoded.stderr):
        raise ValueError('Decoder reported damaged video: ' + decoded.stderr[-4000:])
    if ffprobe:
        probe = run([ffprobe, '-v', 'error', '-threads', '2', '-select_streams', 'v:0',
                     '-count_frames', '-show_entries',
                     'stream=codec_name,pix_fmt,width,height,avg_frame_rate,nb_frames,nb_read_frames,duration:format=duration',
                     '-of', 'json', path])
        metadata = json.loads(probe.stdout)
        streams = metadata.get('streams', [])
        if len(streams) != 1:
            raise ValueError('Expected one selected video stream')
        stream = streams[0]
        codec, pixels = stream.get('codec_name'), stream.get('pix_fmt')
        resolution = [int(stream['width']), int(stream['height'])]
        rate = float(Fraction(stream['avg_frame_rate']))
        duration = float(stream.get('duration', metadata.get('format', {}).get('duration', 'nan')))
        if int(stream.get('nb_read_frames', '-1')) != job['count']:
            raise ValueError('ffprobe counted a different number of frames')
        method = 'ffprobe metadata plus full ffmpeg decode'
    else:
        line = next((line for line in decoded.stderr.splitlines() if 'Video:' in line), '')
        codec_match = re.search(r'Video:\s*([a-zA-Z0-9_]+)', line)
        dimensions = re.search(r'\b(\d{2,5})x(\d{2,5})\b', line)
        rate_match = re.search(r'([0-9.]+)\s+fps\b', line)
        if not codec_match or not dimensions or not rate_match:
            raise ValueError('Could not read input video metadata from ffmpeg decoder')
        codec = codec_match.group(1)
        pixels = 'yuv420p' if re.search(r'\byuv420p\b', line) else 'unknown'
        resolution = [int(v) for v in dimensions.groups()]
        rate = float(rate_match.group(1))
        duration = int(progress.get('out_time_us', '-1')) / 1_000_000
        method = 'ffmpeg input metadata and full decode progress; ffprobe unavailable'
    if codec != 'h264' or pixels != 'yuv420p':
        raise ValueError(f'Wrong output format: {codec}/{pixels}')
    if resolution != job['resolution'] or abs(rate - FPS) > 1.e-6:
        raise ValueError(f'Wrong output dimensions/rate: {resolution}/{rate}')
    if not math.isfinite(duration) or abs(duration - job['duration']) > .011:
        raise ValueError(f'Wrong decoded duration: {duration}, expected {job["duration"]}')
    if not faststart(path):
        raise ValueError('MP4 moov metadata is not before mdat; fast-start validation failed')
    return {'passed': True, 'method': method, 'codec': codec, 'pixel_format': pixels,
            'resolution': resolution, 'fps': rate, 'decoded_frames': decoded_frames,
            'duration_seconds': duration, 'expected_duration_seconds': job['duration'],
            'faststart_moov_before_mdat': True}


def encode(job, out, ffmpeg, ffprobe):
    target = out / 'videos' / job['view'] / (job['clip'] + '.mp4')
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + '.tmp')
    command = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-threads', '2',
               '-framerate', str(FPS), '-start_number', '0', '-i', job['directory'] / '%04d.png',
               '-frames:v', str(job['count']), '-an', '-c:v', 'libx264', '-preset', 'medium',
               '-crf', '18', '-pix_fmt', 'yuv420p', '-r', str(FPS), '-threads', '2',
               '-filter_threads', '1', '-filter_complex_threads', '1',
               '-movflags', '+faststart', '-f', 'mp4', temporary]
    run(command)
    validation = validate_video(temporary, job, ffmpeg, ffprobe)
    temporary.replace(target)
    return {'view': job['view'], 'clip': job['clip'], 'path': target.relative_to(out).as_posix(),
            'validated_utc': now(), 'bytes': target.stat().st_size, 'sha256': sha256(target),
            'source_manifest': job['manifest'].relative_to(out).as_posix(),
            'source_manifest_sha256': job['manifest_sha256'], 'source_frame_count': job['count'],
            'source_frame_bytes': job['source_frame_bytes'], 'encoder_threads': 2,
            'encoder': 'libx264', 'crf': 18, 'preset': 'medium', 'validation': validation}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=DEFAULT_OUT)
    parser.add_argument('--ffmpeg', type=Path, default=DEFAULT_FFMPEG)
    parser.add_argument('--view', choices=('tps', 'fps', 'both'), default='both')
    parser.add_argument('--clip', choices=EXPECTED)
    args = parser.parse_args(argv)
    out, ffmpeg = args.out.resolve(), args.ffmpeg.resolve()
    if not out.is_dir():
        parser.error('Output directory does not exist: ' + str(out))
    report_path = out / 'video-validation.json'
    report = {'schema_version': 1, 'state': 'preflight', 'passed': False,
              'started_utc': now(), 'requested_view': args.view, 'requested_clip': args.clip,
              'ffmpeg': str(ffmpeg), 'clips': [], 'error': None,
              'validation_boundary': 'Checks frame files, dimensions, timing, output format, '
              'decoded count/duration and fast-start. Does not establish visual quality.'}
    try:
        if not ffmpeg.is_file():
            raise FileNotFoundError('ffmpeg not found: ' + str(ffmpeg))
        ffprobe = find_ffprobe(ffmpeg)
        report['ffprobe'] = str(ffprobe) if ffprobe else None
        views = ('tps', 'fps') if args.view == 'both' else (args.view,)
        jobs = [preflight(out, view, clip) for view in views for clip in load_clips(out, view)
                if args.clip is None or clip['name'] == args.clip]
        report['jobs_planned'] = len(jobs)
        report['frames_planned'] = sum(job['count'] for job in jobs)
        # Retain only still-matching previously verified videos during subset runs.
        selected = {(j['view'], j['clip']) for j in jobs}
        if report_path.is_file():
            prior = json.loads(report_path.read_text(encoding='utf-8'))
            for item in prior.get('clips', []):
                path = (out / item['path']).resolve()
                source_manifest = (out / item['source_manifest']).resolve()
                if (path.is_relative_to(out) and source_manifest.is_relative_to(out)
                        and (item.get('view'), item.get('clip')) not in selected and path.is_file()
                        and source_manifest.is_file()
                        and item.get('validation', {}).get('passed') is True
                        and item.get('sha256') == sha256(path)
                        and item.get('source_manifest_sha256') == sha256(source_manifest)):
                    report['clips'].append(item)
        report['state'] = 'encoding'
        write_json(report_path, report)
        for job in jobs:
            report['active_clip'] = job['view'] + '/' + job['clip']
            print('ENCODE BEGIN ' + report['active_clip'], flush=True)
            write_json(report_path, report)
            report['clips'].append(encode(job, out, ffmpeg, ffprobe))
            write_json(report_path, report)
            print('ENCODE VERIFIED ' + report['active_clip'], flush=True)
        report.update(state='complete', passed=True, active_clip=None, finished_utc=now())
        verified = {(c['view'], c['clip']) for c in report['clips']}
        report['package_video_set_complete'] = verified == {(v, n) for v in ('tps', 'fps') for n in EXPECTED}
        write_json(report_path, report)
        print('REVIEW_VIDEO_ENCODING_COMPLETE ' + str(len(jobs)) + ' videos', flush=True)
        return report
    except BaseException as exc:
        report.update(state='failed', passed=False, finished_utc=now(),
                      error={'type': type(exc).__name__, 'message': str(exc)})
        write_json(report_path, report)
        print(f'REVIEW_VIDEO_ENCODING_FAILED {type(exc).__name__}: {exc}', file=sys.stderr, flush=True)
        raise


if __name__ == '__main__':
    main()
