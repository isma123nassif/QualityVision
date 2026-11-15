#!/usr/bin/env python3
"""Phone camera spike: receives JPEG frames via POST /frame, serves MJPEG on /stream."""
import argparse
import json
import os
import ssl
import struct
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MAX_BODY = 2 * 1024 * 1024
HERE = os.path.dirname(os.path.abspath(__file__))

cond = threading.Condition()
state = {
    "seq": 0,          # incremented on every new frame
    "jpeg": b"",
    "ts": 0.0,         # monotonic time of latest frame
    "size": None,      # (w, h) if known
    "viewers": 0,
    "win_frames": 0,   # counters for the 1 s log window
    "win_bytes": 0,
    "fps_in": 0.0,
    "avg_kb": 0.0,
}


def jpeg_size(data):
    """Parse width/height from JPEG SOF marker; None if not found."""
    try:
        i = 2
        n = len(data)
        while i + 4 <= n:
            if data[i] != 0xFF:
                i += 1
                continue
            m = data[i + 1]
            if m in (0xD8, 0x01) or 0xD0 <= m <= 0xD7 or m == 0xFF:
                i += 2 if m != 0xFF else 1
                continue
            (seglen,) = struct.unpack(">H", data[i + 2:i + 4])
            if m in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
                h, w = struct.unpack(">HH", data[i + 5:i + 9])
                return w, h
            i += 2 + seglen
    except Exception:
        pass
    return None


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def _send(self, code, body=b"", ctype="text/plain"):
        self.send_response(code)
        self.send_header("Cache-Control", "no-store")
        if code != 204:
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _file(self, name):
        with open(os.path.join(HERE, name), "rb") as f:
            self._send(200, f.read(), "text/html; charset=utf-8")

    def do_GET(self):
        path = self.path.split("?")[0]
        try:
            if path == "/":
                self._file("index.html")
            elif path == "/view":
                self._send(200, VIEW_HTML.encode(), "text/html; charset=utf-8")
            elif path == "/stats":
                with cond:
                    age = int((time.monotonic() - state["ts"]) * 1000) if state["ts"] else None
                    s = {
                        "fps_in": round(state["fps_in"], 1),
                        "last_frame_age_ms": age,
                        "frame_bytes": len(state["jpeg"]),
                        "width": state["size"][0] if state["size"] else None,
                        "height": state["size"][1] if state["size"] else None,
                        "viewers": state["viewers"],
                    }
                self._send(200, json.dumps(s).encode(), "application/json")
            elif path == "/stream":
                self._stream()
            else:
                self._send(404, b"not found")
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            self.close_connection = True

    def do_POST(self):
        if self.path.split("?")[0] != "/frame":
            self._send(404, b"not found")
            return
        try:
            length = int(self.headers.get("Content-Length", "-1"))
        except ValueError:
            length = -1
        if length < 0:
            self.close_connection = True
            self._send(411, b"length required")
            return
        if length > MAX_BODY:
            self.close_connection = True
            self._send(413, b"too large")
            return
        try:
            data = self.rfile.read(length)
            if len(data) != length:
                self.close_connection = True
                return
            size = jpeg_size(data)
            with cond:
                state["seq"] += 1
                state["jpeg"] = data
                state["ts"] = time.monotonic()
                state["size"] = size or state["size"]
                state["win_frames"] += 1
                state["win_bytes"] += len(data)
                cond.notify_all()
            self._send(204)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            self.close_connection = True

    def _stream(self):
        self.send_response(200)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=frame")
        self.send_header("Connection", "close")
        self.end_headers()
        self.close_connection = True
        with cond:
            state["viewers"] += 1
            last = state["seq"] - 1 if state["seq"] else 0  # send current frame right away
        try:
            while True:
                with cond:
                    while state["seq"] == last:
                        cond.wait(timeout=5)
                        if state["seq"] == last:
                            # keep-alive check: loop again (write below not needed)
                            continue
                    last = state["seq"]
                    jpeg = state["jpeg"]
                self.wfile.write(
                    b"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: "
                    + str(len(jpeg)).encode() + b"\r\n\r\n" + jpeg + b"\r\n"
                )
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError, OSError):
            pass
        finally:
            with cond:
                state["viewers"] -= 1


VIEW_HTML = """<!doctype html><html><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Live</title>
<style>html,body{margin:0;background:#000;height:100%}
img{width:100%;height:100%;object-fit:contain;display:block}
#o{position:fixed;top:8px;left:8px;background:rgba(0,0,0,.6);color:#0f0;font:14px monospace;padding:6px 8px;border-radius:4px;white-space:pre}</style>
</head><body><img id="v" src="/stream"><div id="o">...</div>
<script>
// Viewer-side FPS: count image loads is not possible for MJPEG, so use server fps_in
// plus a local estimate based on the age of frames reported by /stats.
const o=document.getElementById('o');
async function poll(){
  try{
    const s=await (await fetch('/stats',{cache:'no-store'})).json();
    o.textContent='FPS: '+s.fps_in+'\\nAlter: '+(s.last_frame_age_ms??'-')+' ms\\n'
      +(s.width?s.width+'x'+s.height+' ':'')+(s.frame_bytes/1024).toFixed(0)+' KB\\nViewer: '+s.viewers;
  }catch(e){o.textContent='Keine Verbindung';}
}
setInterval(poll,500);poll();
</script></body></html>"""


def logger():
    last = time.monotonic()
    while True:
        time.sleep(1)
        now = time.monotonic()
        with cond:
            dt = now - last
            n = state["win_frames"]
            b = state["win_bytes"]
            state["fps_in"] = n / dt
            state["avg_kb"] = (b / n / 1024) if n else 0.0
            state["win_frames"] = 0
            state["win_bytes"] = 0
            v = state["viewers"]
            fps, kb = state["fps_in"], state["avg_kb"]
        last = now
        print(f"fps_in={fps:.1f} avg_kb={kb:.1f} viewers={v}", flush=True)


class QuietServer(ThreadingHTTPServer):
    def handle_error(self, request, client_address):
        exc = sys.exc_info()[1]
        if isinstance(exc, (ssl.SSLError, ConnectionResetError, BrokenPipeError)):
            return
        print(f"error from {client_address[0]}: {exc!r}", flush=True)


def main():
    parser = argparse.ArgumentParser(description="Phone camera spike server")
    parser.add_argument("--port", type=int, default=8490)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--cert", help="PEM certificate (enables HTTPS with --key)")
    parser.add_argument("--key", help="PEM private key (enables HTTPS with --cert)")
    args = parser.parse_args()
    port = args.port
    ThreadingHTTPServer.daemon_threads = True
    srv = QuietServer((args.host, port), Handler)
    scheme = "http"
    if args.cert and args.key:
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(args.cert, args.key)
        srv.socket = ctx.wrap_socket(srv.socket, server_side=True)
        scheme = "https"
    threading.Thread(target=logger, daemon=True).start()
    print(f"Listening on {scheme}://{args.host}:{port}", flush=True)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
