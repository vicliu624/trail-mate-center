# Trail Mate Center

> Desktop control center for Trail Mate / Meshtastic devices

[English](README.md) | [中文](README_CN.md)

---

## Overview

Trail Mate Center is the host-side desktop application for monitoring and managing Trail Mate or Meshtastic-based devices over USB HostLink.
It focuses on situational awareness, message workflows, telemetry visibility, and protocol inspection.

This repository contains the desktop app and shared protocol/core libraries. It does **not** include device firmware.

---

## Core Features

- USB HostLink connection management (port scan, auto reconnect, ACK timeout/retry)
- Live chat and conversation view with quick send
- Tactical dashboard with map view, node list, and event stream
- Node details with telemetry snapshots and last-seen status
- Device configuration viewer/editor
- Logs, event monitor, and raw frame inspector
- Export messages/events to CSV or JSONL
- Session replay from recorded files

---

## Getting Started

Build and run (requires .NET 8):

```bash
dotnet run --project src/TrailMateCenter.App
```

---

## Project Scope

This repository includes:

- Desktop UI (Avalonia)
- HostLink protocol implementation
- Meshtastic protobuf decoding and data models
- Local storage, export, and replay tools

It does **not** include:

- Device firmware
- Embedded UI or hardware drivers
- Mobile apps or commercial services

---

## License

Licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.
See `LICENSE` for details.
