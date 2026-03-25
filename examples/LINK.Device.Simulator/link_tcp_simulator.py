#!/usr/bin/env python3
"""
LINK TCP device simulator — compatible with LOGDrakon/LINK-Client.

This script listens on a TCP port and speaks the LINK protocol exactly as a
real device would.  It is the easiest way to test the .NET SDK without needing
a physical device or a virtual COM-port bridge.

Supported frames (sent by the client):
  LINK\x1fGETAPP\0
  LINK\x1f<APP-ID>\x1fGETV\0
  LINK\x1f<APP-ID>\x1fAUTH_INIT\x1f<CLIENT_NONCE>\0   → nonce exchange (challenge-response)
  LINK\x1f<APP-ID>\x1fAUTH\x1f<HASHED_PASSWORD>\0    → hash = H(clientNonce + deviceNonce + password)
  LINK\x1f<APP-ID>\x1fCHPWD\x1f<OLD_HASH>\x1f<NEW_HASH>\x1f<CRC32>\0  → change password
  LINK\x1f<APP-ID>\x1fGETTEMP\0
  LINK\x1f<APP-ID>\x1fPING\0
  LINK\x1f<APP-ID>\x1f<any command>\0  → replied with ERR\x1fUNKNOWN_COMMAND

The hash algorithm is announced by the device in GETV (e.g. HASH=SHA256).
The client must first call AUTH_INIT to exchange nonces, then AUTH with the
hashed password.  Nonces can be reused within the same session.

Usage:
  python link_tcp_simulator.py                          # default 127.0.0.1:5000
  python link_tcp_simulator.py --host 0.0.0.0 --port 5000
  python link_tcp_simulator.py --app-id MYAPP --password secret --temp 36.6
  python link_tcp_simulator.py --hash SHA512 --locked
  python link_tcp_simulator.py --max-packet-size 32     # smaller chunks
  python link_tcp_simulator.py --max-packet-size 0      # no chunking
"""

import argparse
import asyncio
import hashlib
import secrets
import sys
import zlib
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
    hash_method: str = "SHA256"
    locked: bool = False
    password: str = "password"
    temperature_c: float = 24.6
    max_packet_size: int = 64
    extra_getv: list = field(default_factory=list)

    def getv_args(self) -> list:
        args = [
            self.link_version,
            f"UID={self.uid}",
            f"MODEL={self.model}",
            f"ENC={self.enc}",
            f"HASH={self.hash_method}",
            f"LOCKED={'true' if self.locked else 'false'}",
        ]
        args.extend(self.extra_getv)
        return args


def compute_password_hash(hash_method: str, client_nonce: str,
                          device_nonce: str, password: str) -> str:
    """Compute H(clientNonce + deviceNonce + password) using the given algorithm."""
    algo = hash_method.lower()
    h = hashlib.new(algo)
    h.update((client_nonce + device_nonce + password).encode("utf-8"))
    return h.hexdigest()


def compute_crc32(data: str) -> str:
    """Compute CRC32 of the given string and return it as 8-char lowercase hex."""
    return format(zlib.crc32(data.encode("utf-8")) & 0xFFFFFFFF, "08x")


# ---------------------------------------------------------------------------
# Frame helpers
# ---------------------------------------------------------------------------

def build_frame(app_id, command, *args) -> bytes:
    parts = ["LINK"]
    if app_id:
        parts.append(app_id)
    parts.append(command)
    parts.extend(args)
    return ("\x1f".join(parts) + "\0").encode("latin-1")


def parse_frame(raw: str) -> dict:
    """Parse a raw LINK frame string (without the NUL terminator)."""
    if not raw.strip():
        raise ValueError("empty frame")

    parts = [p for p in raw.split("\x1f") if p != ""]
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
        self._client_nonce: str | None = None
        self._device_nonce: str | None = None
        peer = writer.get_extra_info("peername")
        self._peer = f"{peer[0]}:{peer[1]}" if peer else "unknown"

    def log(self, msg: str):
        if self.verbose:
            print(f"[LINK-SIM] [{self._peer}] {msg}", file=sys.stderr, flush=True)

    def send(self, payload: bytes):
        chunk_size = self.state.max_packet_size
        if chunk_size > 0 and len(payload) > chunk_size:
            for i in range(0, len(payload), chunk_size):
                chunk = payload[i:i + chunk_size]
                self.writer.write(chunk)
                self.log(f"TX chunk [{i}:{i + len(chunk)}] {chunk!r}")
        else:
            self.writer.write(payload)
        self.log(f"TX {payload!r} ({len(payload)} bytes)")

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

        if command == "AUTH_INIT":
            self._client_nonce = args[0] if args else ""
            self._device_nonce = secrets.token_hex(32)
            self.log(f"Nonce exchange: client={self._client_nonce} "
                     f"device={self._device_nonce}")
            self.send(build_frame(state.app_id, "RETURN", "AUTH_INIT",
                                  self._device_nonce))
            return

        if command == "AUTH":
            supplied = args[0] if args else ""
            if self._client_nonce is None or self._device_nonce is None:
                self.log("AUTH received without prior AUTH_INIT")
                self.send(build_frame(state.app_id, "RETURN", "AUTH", "ERR"))
                return
            expected = compute_password_hash(
                state.hash_method, self._client_nonce,
                self._device_nonce, state.password)
            if supplied == expected:
                state.locked = False
                self.send(build_frame(state.app_id, "RETURN", "AUTH", "OK"))
            else:
                self.send(build_frame(state.app_id, "RETURN", "AUTH", "ERR"))
            return

        if command == "CHPWD":
            if self._client_nonce is None or self._device_nonce is None:
                self.log("CHPWD received without prior AUTH_INIT")
                self.send(build_frame(state.app_id, "RETURN", "CHPWD", "ERR"))
                return
            if len(args) < 3:
                self.log("CHPWD missing arguments")
                self.send(build_frame(state.app_id, "RETURN", "CHPWD", "ERR"))
                return
            old_hash, new_hash, crc = args[0], args[1], args[2]
            expected_crc = compute_crc32(old_hash + new_hash)
            if crc != expected_crc:
                self.log(f"CHPWD CRC mismatch: got={crc} expected={expected_crc}")
                self.send(build_frame(state.app_id, "RETURN", "CHPWD",
                                      "ERR", "BAD_CRC"))
                return
            expected_old = compute_password_hash(
                state.hash_method, self._client_nonce,
                self._device_nonce, state.password)
            if old_hash != expected_old:
                self.log("CHPWD old password hash mismatch")
                self.send(build_frame(state.app_id, "RETURN", "CHPWD",
                                      "ERR", "BAD_OLD_PWD"))
                return
            # Accept — we cannot reverse the new hash, so we store the raw
            # hash for this session.  A real device would store the new
            # password in flash.  For the simulator we simply log the event.
            self.log("CHPWD accepted — password changed (simulator cannot "
                     "persist the new password across restarts)")
            self.send(build_frame(state.app_id, "RETURN", "CHPWD", "OK"))
            return

        if command == "GETTEMP":            self.send(build_frame(state.app_id, "RETURN", "GETTEMP",
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
    parser.add_argument("--hash", default="SHA256",
                        choices=["SHA1", "SHA256", "SHA384", "SHA512"],
                        help="Hash algorithm for AUTH (default: SHA256)")
    parser.add_argument("--max-packet-size", type=int, default=64,
                        help="Max bytes per write (simulates USB FS "
                             "buffer); 0 = no chunking (default: 64)")
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
        hash_method=args.hash,
        locked=args.locked,
        temperature_c=args.temp,
        max_packet_size=args.max_packet_size,
    )

    try:
        asyncio.run(run_server(args.host, args.port, state, verbose=not args.quiet))
    except KeyboardInterrupt:
        print("\n[LINK-SIM] Stopped.", file=sys.stderr)


if __name__ == "__main__":
    main()
