#!/usr/bin/env python3
"""
LINK Serial device simulator — compatible with LOGDrakon/LINK-Client.

This script opens a serial port and speaks the LINK protocol exactly as a
real device would.  It is the easiest way to test the .NET SDK over a serial
link without needing a physical device.

To create a pair of virtual COM ports you can use:
  - Windows : com0com  (e.g. COM10 <-> COM11)
  - Linux   : socat    (socat -d -d pty,raw,echo=0 pty,raw,echo=0)
  - macOS   : socat    (same command as Linux)

The simulator connects to one end of the pair; point the .NET
``LinkSerialTransport`` at the other end.

Supported frames (sent by the client):
  LINK:GETAPP\0
  LINK:<APP-ID>:GETV\0   (also accepts LINK:AUTO:GETV\0)
  LINK:<APP-ID>:AUTH:<password>\0
  LINK:<APP-ID>:GETTEMP\0
  LINK:<APP-ID>:PING\0
  LINK:<APP-ID>:<any command>\0  → replied with ERR:UNKNOWN_COMMAND

Usage:
  python link_serial_simulator.py                        # default COM10, 115200
  python link_serial_simulator.py --port COM11 --baud 9600
  python link_serial_simulator.py --app-id MYAPP --password secret --temp 36.6

Requirements:
  pip install pyserial
"""

import argparse
import sys
import threading
from dataclasses import dataclass, field

try:
    import serial
except ImportError:
    print(
        "pyserial is required.  Install it with:\n  pip install pyserial",
        file=sys.stderr,
    )
    sys.exit(1)


# ---------------------------------------------------------------------------
# Device state  (shared with link_tcp_simulator.py)
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
# Frame helpers  (same logic as the TCP simulator)
# ---------------------------------------------------------------------------

def build_frame(app_id: str | None, command: str, *args) -> bytes:
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
# Serial handler
# ---------------------------------------------------------------------------

class SerialHandler:
    """Reads from a serial port, processes LINK frames, and writes replies."""

    def __init__(self, ser: serial.Serial, state: DeviceState,
                 verbose: bool = True):
        self.ser = ser
        self.state = state
        self.verbose = verbose
        self._buf = bytearray()
        self._running = False

    def log(self, msg: str):
        if self.verbose:
            print(f"[LINK-SIM] [{self.ser.port}] {msg}",
                  file=sys.stderr, flush=True)

    def send(self, payload: bytes):
        self.ser.write(payload)
        self.ser.flush()
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

        if command == "GETV":
            self.send(build_frame(state.app_id, "RETURN", "GETV",
                                  *state.getv_args()))
            return

        # For all other commands, only respond if the app-id matches
        if app_id not in ("AUTO", state.app_id):
            self.log(f"Ignored command for unknown app_id={app_id!r}")
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
        self.send(build_frame(state.app_id, "RETURN", command,
                              "ERR", "UNKNOWN_COMMAND"))

    def run(self):
        """Blocking read-loop.  Call from the main thread or a worker thread."""
        self._running = True
        self.log("Waiting for data…")
        try:
            while self._running:
                # Read one byte at a time (serial.read honours the timeout)
                data = self.ser.read(1)
                if not data:
                    # Timeout — loop back to check _running flag
                    continue
                # If more bytes are already in the buffer, grab them too
                waiting = self.ser.in_waiting
                if waiting:
                    data += self.ser.read(waiting)

                for byte in data:
                    if byte == 0:
                        raw = self._buf.decode("ascii", errors="ignore")
                        self._buf.clear()
                        self.handle_frame(raw)
                    else:
                        self._buf.append(byte)
        except serial.SerialException as exc:
            self.log(f"Serial error: {exc}")
        except Exception as exc:
            self.log(f"Error: {exc}")
        finally:
            self.log("Handler stopped.")

    def stop(self):
        self._running = False


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description="LINK Serial device simulator — compatible with "
                    "LOGDrakon/LINK-Client"
    )
    parser.add_argument("--port", default="COM10",
                        help="Serial port name (default: COM10)")
    parser.add_argument("--baud", type=int, default=115200,
                        help="Baud rate (default: 115200)")
    parser.add_argument("--bytesize", type=int, default=8,
                        choices=[5, 6, 7, 8],
                        help="Data bits (default: 8)")
    parser.add_argument("--parity", default="N",
                        choices=["N", "E", "O", "M", "S"],
                        help="Parity: N(one), E(ven), O(dd), M(ark), "
                             "S(pace) (default: N)")
    parser.add_argument("--stopbits", type=float, default=1,
                        choices=[1, 1.5, 2],
                        help="Stop bits (default: 1)")
    parser.add_argument("--app-id", default="DRAGON",
                        help="LINK application identifier (default: DRAGON)")
    parser.add_argument("--password", default="password",
                        help="AUTH password (default: password)")
    parser.add_argument("--model", default="Dragon-Sensor")
    parser.add_argument("--uid", default="0x12345678")
    parser.add_argument("--enc", default="NONE")
    parser.add_argument("--locked", action="store_true",
                        help="Start in locked state")
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

    ser = serial.Serial(
        port=args.port,
        baudrate=args.baud,
        bytesize=args.bytesize,
        parity=args.parity,
        stopbits=args.stopbits,
        timeout=0.5,            # read timeout so we can check _running flag
    )

    handler = SerialHandler(ser, state, verbose=not args.quiet)

    print(
        f"[LINK-SIM] Serial simulator on {ser.port}  "
        f"({ser.baudrate} {ser.bytesize}{ser.parity}{int(ser.stopbits)})  "
        f"app-id={state.app_id}",
        file=sys.stderr, flush=True,
    )

    try:
        handler.run()
    except KeyboardInterrupt:
        handler.stop()
        print("\n[LINK-SIM] Stopped.", file=sys.stderr)
    finally:
        if ser.is_open:
            ser.close()


if __name__ == "__main__":
    main()
