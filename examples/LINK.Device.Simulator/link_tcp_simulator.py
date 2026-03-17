#!/usr/bin/env python3
"""
LINK TCP device simulator — compatible with LOGDrakon/LINK-Client.

This script listens on a TCP port and speaks the LINK protocol exactly as a
real device would.  It is the easiest way to test the .NET SDK without needing
a physical device or a virtual COM-port bridge.

Supported frames (sent by the client):
  LINK:GETAPP\0
  LINK:<APP-ID>:GETV\0
  LINK:<APP-ID>:AUTH:<password>\0
  LINK:<APP-ID>:GETTEMP\0
  LINK:<APP-ID>:PING\0
  LINK:<APP-ID>:<any command>\0  → replied with ERR:UNKNOWN_COMMAND

Usage:
  python link_tcp_simulator.py                          # default 127.0.0.1:5000
  python link_tcp_simulator.py --host 0.0.0.0 --port 5000
  python link_tcp_simulator.py --app-id MYAPP --password secret --temp 36.6
"""

import argparse
import asyncio
import sys
from dataclasses import dataclass, field


# ---------------------------------------------------------------------------
# Device state
# ---------------------------------------------------------------------------

@dataclass
class DeviceState:
    app_id: str = "DRAGON"
    link_version: str = "LINKv1.1"
    uid: str = "0x12345678"
    model: str = "Dragon-Sensor"
    enc: str = "NONE"
    locked: bool = False
    password: str = "password"
    temperature_c: float = 24.6
    extra_getv: list = field(default_factory=list)

    def getv_args(self) -> list:
        args = [
            self.link_version,
            f"UID={self.uid}",
            f"MODEL={self.model}",
            f"ENC={self.enc}",
            f"LOCKED={'true' if self.locked else 'false'}",
        ]
        args.extend(self.extra_getv)
        return args


# ---------------------------------------------------------------------------
# Frame helpers
# ---------------------------------------------------------------------------

def build_frame(app_id, command, *args) -> bytes:
    parts = ["LINK"]
    if app_id:
        parts.append(app_id)
    parts.append(command)
    parts.extend(args)
    return (":".join(parts) + "\0").encode("latin-1")


def parse_frame(raw: str) -> dict:
    """Parse a raw LINK frame string (without the NUL terminator)."""
    if not raw.strip():
        raise ValueError("empty frame")

    parts = [p for p in raw.split(":") if p != ""]
    if len(parts) < 2 or parts[0] != "LINK":
        raise ValueError(f"invalid LINK frame: {raw!r}")

    # LINK:GETAPP  (no app-id)
    if len(parts) == 2 and parts[1] == "GETAPP":
        return {"app_id": None, "command": "GETAPP", "args": []}

    if len(parts) < 3:
        raise ValueError(f"incomplete standard frame: {raw!r}")

    return {
        "app_id": parts[1],
        "command": parts[2],
        "args": parts[3:],
    }


# ---------------------------------------------------------------------------
# Client handler
# ---------------------------------------------------------------------------

class ClientHandler:
    def __init__(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter,
                 state: DeviceState, verbose: bool = True):
        self.reader = reader
        self.writer = writer
        self.state = state
        self.verbose = verbose
        self._buf = bytearray()
        peer = writer.get_extra_info("peername")
        self._peer = f"{peer[0]}:{peer[1]}" if peer else "unknown"

    def log(self, msg: str):
        if self.verbose:
            print(f"[LINK-SIM] [{self._peer}] {msg}", file=sys.stderr, flush=True)

    def send(self, payload: bytes):
        self.writer.write(payload)
        self.log(f"TX {payload!r}")

    def handle_frame(self, raw: str):
        self.log(f"RX {raw!r}")
        try:
            frame = parse_frame(raw)
        except ValueError as exc:
            self.log(f"Ignored invalid frame: {exc}")
            return

        app_id = frame["app_id"]
        command = frame["command"]
        args = frame["args"]
        state = self.state

        if command == "GETAPP":
            self.send(build_frame(state.app_id, "RETURN", "GETAPP", state.app_id))
            return

        # For all commands (except GETAPP), only respond if the app-id matches
        if app_id != state.app_id:
            self.log(f"Ignored command for unknown app_id={app_id!r}")
            return

        if command == "GETV":
            self.send(build_frame(state.app_id, "RETURN", "GETV", *state.getv_args()))
            return

        if command == "AUTH":
            supplied = args[0] if args else ""
            if supplied == state.password:
                state.locked = False
                self.send(build_frame(state.app_id, "RETURN", "AUTH", "OK"))
            else:
                self.send(build_frame(state.app_id, "RETURN", "AUTH", "ERR"))
            return

        if command == "GETTEMP":
            self.send(build_frame(state.app_id, "RETURN", "GETTEMP",
                                  f"{state.temperature_c:.1f}\xb0C"))
            return

        if command == "PING":
            self.send(build_frame(state.app_id, "RETURN", "PING", "PONG"))
            return

        # Unknown command — reply with generic error
        self.send(build_frame(state.app_id, "RETURN", command, "ERR", "UNKNOWN_COMMAND"))

    async def run(self):
        self.log("Client connected.")
        try:
            while True:
                chunk = await self.reader.read(4096)
                if not chunk:
                    break
                for byte in chunk:
                    if byte == 0:
                        raw = self._buf.decode("ascii", errors="ignore")
                        self._buf.clear()
                        self.handle_frame(raw)
                    else:
                        self._buf.append(byte)
                await self.writer.drain()
        except asyncio.CancelledError:
            pass
        except ConnectionResetError:
            pass
        except Exception as exc:
            self.log(f"Error: {exc}")
        finally:
            self.log("Client disconnected.")
            try:
                self.writer.close()
                await self.writer.wait_closed()
            except Exception:
                pass


# ---------------------------------------------------------------------------
# Server
# ---------------------------------------------------------------------------

async def run_server(host: str, port: int, state: DeviceState, verbose: bool):
    async def handle(reader, writer):
        handler = ClientHandler(reader, writer, state, verbose)
        await handler.run()

    server = await asyncio.start_server(handle, host, port)
    addr = server.sockets[0].getsockname() if server.sockets else (host, port)
    print(f"[LINK-SIM] Listening on {addr[0]}:{addr[1]}  (app-id={state.app_id})",
          file=sys.stderr, flush=True)
    async with server:
        await server.serve_forever()


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description="LINK TCP device simulator — compatible with LOGDrakon/LINK-Client"
    )
    parser.add_argument("--host", default="127.0.0.1",
                        help="Host/IP to listen on (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=5000,
                        help="TCP port to listen on (default: 5000)")
    parser.add_argument("--app-id", default="DRAGON",
                        help="LINK application identifier (default: DRAGON)")
    parser.add_argument("--password", default="password",
                        help="AUTH password (default: password)")
    parser.add_argument("--model", default="Dragon-Sensor")
    parser.add_argument("--uid", default="0x12345678")
    parser.add_argument("--enc", default="NONE")
    parser.add_argument("--locked", action="store_true",
                        help="Start in locked state (requires AUTH before other commands)")
    parser.add_argument("--temp", type=float, default=24.6,
                        help="Simulated temperature in °C (default: 24.6)")
    parser.add_argument("--quiet", action="store_true",
                        help="Suppress verbose frame logging")
    args = parser.parse_args()

    state = DeviceState(
        app_id=args.app_id,
        password=args.password,
        model=args.model,
        uid=args.uid,
        enc=args.enc,
        locked=args.locked,
        temperature_c=args.temp,
    )

    try:
        asyncio.run(run_server(args.host, args.port, state, verbose=not args.quiet))
    except KeyboardInterrupt:
        print("\n[LINK-SIM] Stopped.", file=sys.stderr)


if __name__ == "__main__":
    main()
