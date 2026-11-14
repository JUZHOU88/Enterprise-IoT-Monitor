#!/usr/bin/env python3
"""Stdlib-only local demo telemetry producer; no ESP32 required."""
import argparse
import json
import os
import time
import urllib.error
import urllib.request
import uuid

parser = argparse.ArgumentParser(description="Simulated ESP32 telemetry for the local API")
parser.add_argument("--url", default=os.getenv("MONITOR_URL", "http://localhost:5070/api/telemetry"))
parser.add_argument("--key", default=os.getenv("MONITOR_DEVICE_KEY", "local-demo-key"))
parser.add_argument("--device", default="esp32-sim-01")
parser.add_argument("--count", type=int, default=20)
parser.add_argument("--interval", type=float, default=1)
args = parser.parse_args()

boot = uuid.uuid4().hex[:12]
for index in range(max(0, args.count)):
    temperature = 22.0 + (index % 9) * 0.85
    event = {
        "deviceId": args.device,
        "eventId": f"{boot}-{index}",
        "temperatureC": round(temperature, 2),
        "doorOpen": index % 11 in (8, 9),
        "sensorOk": True,
        "uptimeMs": index * int(args.interval * 1000),
    }
    req = urllib.request.Request(
        args.url,
        data=json.dumps(event).encode("utf-8"),
        method="POST",
        headers={"Content-Type": "application/json", "X-Device-Key": args.key},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as response:
            print(response.status, event["eventId"], response.read().decode())
    except (urllib.error.URLError, TimeoutError) as error:
        print("Upload failed:", error)
    if index + 1 < args.count:
        time.sleep(args.interval)
