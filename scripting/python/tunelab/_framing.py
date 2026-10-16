"""Content-Length framed JSON over stdio (wire §5.1)."""
import json
import sys

_MAX_FRAME = 512 * 1024 * 1024


def read_message(stream):
    """Read one frame; returns the decoded object, or None on EOF (host died — caller must exit)."""
    length = None
    while True:
        line = stream.readline()
        if not line:
            return None
        line = line.rstrip(b"\r\n")
        if line == b"":
            break
        name, _, value = line.partition(b":")
        if name.strip().lower() == b"content-length":
            length = int(value.strip())
    if length is None or length < 0 or length > _MAX_FRAME:
        return None
    body = b""
    while len(body) < length:
        chunk = stream.read(length - len(body))
        if not chunk:
            return None
        body += chunk
    return json.loads(body.decode("utf-8"))


def write_message(stream, obj):
    payload = json.dumps(obj, ensure_ascii=False, allow_nan=True).encode("utf-8")
    stream.write(b"Content-Length: %d\r\n\r\n" % len(payload))
    stream.write(payload)
    stream.flush()


def open_stdio():
    # Binary pipes with explicit UTF-8; text-mode newline translation would corrupt framing.
    stdin = getattr(sys.stdin, "buffer", sys.stdin)
    stdout = getattr(sys.stdout, "buffer", sys.stdout)
    return stdin, stdout
