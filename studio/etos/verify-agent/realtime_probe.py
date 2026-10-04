#!/usr/bin/env python3
"""W0 realtime probe for studio/etos/verify.sh.

Opens /api/v1/realtime/connect?provider=studio-voice with the etos Python SDK (sdk/py, standard
library only) and the verify agent's key, sends 2 s of a 440 Hz sine as PCM16 mono 24 kHz in
100 ms chunks followed by 1 s of silence, never asks for a response (transcript only), then
closes. Prints one JSON line per event (audio payloads replaced by their length) and exits 0 when
the session was `ready` and ended with a clean `closed`.

Environment: ETOS_SDK_PY (path of sdk/py), ETOS_KEY_FILE, ETOS_URL (default 127.0.0.1:7410).
"""
import asyncio
import json
import math
import os
import struct
import sys
import time

sys.path.insert(0, os.environ["ETOS_SDK_PY"])
from etos.agent import Client  # noqa: E402

RATE = 24000
CHUNK = RATE // 10  # 100 ms


def tone(start: int, n: int, hz: float) -> bytes:
    amp = 0.3 * 32767
    return b"".join(struct.pack("<h", int(amp * math.sin(2 * math.pi * hz * (start + i) / RATE))) for i in range(n))


def show(t0: float, frame: dict) -> dict:
    event = dict(frame.get("event", {}))
    if "audio" in event:
        event["audio"] = f"<{len(event['audio'])} base64 chars>"
    line = {"t_ms": int((time.monotonic() - t0) * 1000), "event": event}
    print(json.dumps(line), flush=True)
    return event


async def main() -> int:
    with open(os.environ["ETOS_KEY_FILE"], encoding="utf-8") as f:
        key = f.read().strip()
    client = Client(os.environ.get("ETOS_URL", "http://127.0.0.1:7410"), key, agent="verify")
    config = {
        "session_id": f"verify-{int(time.time())}",
        "generation": 1,
        "instructions": "Transcribe the user's speech. Do not reply.",
        "audio_format": "pcm16",
        "sample_rate_hz": RATE,
    }
    t0 = time.monotonic()
    session = await client.realtime.open("studio-voice", config)
    ready = show(t0, session.ready)
    seen = [ready.get("type")]
    async def read():
        async for frame in session:
            seen.append(show(t0, frame).get("type"))
    reader = asyncio.create_task(read())
    try:
        seq = 0
        for i in range(20):
            await session.audio(seq, tone(i * CHUNK, CHUNK, 440.0))
            seq += 1
            await asyncio.sleep(0.1)
        for _ in range(10):
            await session.audio(seq, b"\x00\x00" * CHUNK)
            seq += 1
            await asyncio.sleep(0.1)
        await asyncio.sleep(1.0)
        await session.command("close-1", {"type": "close"})
        await asyncio.wait_for(reader, 10)
    finally:
        await session.close()
    ok = seen[0] == "ready" and seen[-1] == "closed"
    print(json.dumps({"summary": {"events": seen, "sent_chunks": seq, "ok": ok}}), flush=True)
    return 0 if ok else 1


if __name__ == "__main__":
    try:
        sys.exit(asyncio.run(main()))
    except Exception as e:  # report the refusal verbatim
        print(json.dumps({"error": type(e).__name__, "detail": str(e),
                          "code": getattr(e, "code", None)}), flush=True)
        sys.exit(2)
