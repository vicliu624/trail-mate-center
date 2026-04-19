# Trail Mate Center

> Desktop control center for Trail Mate / Meshtastic field devices

[English](README.md) | [Chinese](README_CN.md)

---

## What It Is

Trail Mate Center is the desktop-side control center for Trail Mate and Meshtastic-compatible devices connected over USB HostLink.
It combines live situational awareness, messaging, offline map preparation, protocol inspection, and propagation analysis in one Avalonia application.

This repository includes the desktop app plus shared protocol/core libraries.
It does **not** include device firmware.

---

## What You Can Do With It

### Connection and protocol

- Scan serial ports and connect to a device over USB HostLink.
- Configure ACK timeout and retry behavior.
- Enable auto-connect and auto-reconnect.
- Switch to replay mode to inspect recorded sessions without live hardware.
- Work with both Meshtastic-like traffic and Trail Mate MeshCore protocol decoding.

### Tactical dashboard

- Watch live positions on a map with `OSM`, `Terrain`, or `Satellite` basemaps.
- Toggle contour overlays and the NASA GIBS imagery overlay.
- View teams, node lists, event streams, and command actions in one screen.
- Follow the latest position automatically or inspect the map manually.
- Send quick broadcast messages and issue rally / move / hold tactical commands.
- Run in offline mode to suppress APRS-IS and MQTT feeds and focus on local cached data.

### Offline map caching and export

- Draw a cache area directly on the map.
- Save frequently used cache regions and inspect their health later.
- Build offline tiles for selected basemaps and contour layers.
- Continue cache jobs for regions that still have missing tiles.
- Export cached regions to removable media in a `maps/base/...` and `maps/contour/...` layout.
- Import a `KML` track and generate a buffered cache area around the route.
- Reuse local map cache for message location previews when tiles are available.

### Chat and message workflow

- Browse conversation threads and message history.
- Reply in the main chat panel or quick-send to the selected node.
- Inspect the selected node's last-seen state, telemetry summary, and last known location.
- Retry failed sends from the chat UI.

### Configuration and integrations

- Load and edit device configuration values.
- Tune tactical thresholds and severity rules.
- Configure APRS-IS gateway settings.
- Configure one or more Meshtastic MQTT sources.
- Configure contour generation and test the Earthdata token used for terrain products.
- Switch UI language and theme from the top bar.

### Logs, inspection, and data export

- Filter logs by severity and save or copy them out.
- Monitor decoded tactical events.
- Inspect raw frames for protocol-level troubleshooting.
- Export messages or events to `CSV` or `JSONL`.
- Persist sessions locally for later replay and analysis.

### Propagation workbench

- Run coverage, interference, relay optimization, advanced, and calibration workflows.
- Define scenario sites directly in the workbench.
- Tune radio, vegetation/clutter, uncertainty, and optimization parameters.
- Inspect analytical layers, legends, hover probes, and path evidence.
- Export simulation results after a run completes.
- Attach an optional Unity viewport/bridge for richer terrain presentation and diagnostics.

---

## Main Tabs

| Tab | What it is for |
| --- | --- |
| `Dashboard` | Live map, team view, node list, event feed, tactical commands, and offline map tools |
| `Propagation` | Coverage analysis, scenario editing, analytical layers, and result export |
| `Chat` | Conversation list, message history, quick send, and selected-node context |
| `Config` | Device config, tactical thresholds, APRS, MQTT, contour, language, and theme setup |
| `Logs` | Application logs with filtering, copy, and save |
| `Events` | Persisted tactical event stream |
| `Raw Frames` | Low-level frame inspection and protocol troubleshooting |
| `Export` | Message/event export to `CSV` or `JSONL` |

---

## Quick Start

### Prerequisites

- `.NET 8 SDK`

### Run the app

```bash
dotnet run --project src/TrailMateCenter.App
```

### First-run checklist

1. Select the device serial port in the top bar.
2. Adjust ACK timeout / retries if your link needs it.
3. Click `Connect`.
4. Open `Dashboard` to confirm map, node, and event updates are flowing.
5. Open `Config` if you need APRS, MQTT, or contour settings.

If you want to inspect recorded data instead of a live device, enable `Replay` and provide a replay file before connecting.

---

## Offline Map Workflow

1. In `Dashboard`, use the map selection tool to draw a cache area, or import a `KML` track and build from the route.
2. Choose which basemaps and contour layers to cache.
3. Start the cache build and wait for the status text to finish.
4. Open the saved region list to inspect cache coverage and continue incomplete regions.
5. Export the region to a USB drive or SD card when the cache is ready.

Notes:

- Basemap tiles are stored under `Documents/TrailMateCenter`.
- Contour generation needs a valid Earthdata token.
- Export can now report when the source cache is still incomplete, which helps catch missing tiles before field use.

---

## Local Data Locations

Common Windows defaults:

- Database: `%LocalAppData%\TrailMateCenter\trailmate.db`
- Settings: `%AppData%\TrailMateCenter\settings.json`
- Map cache: `%UserProfile%\Documents\TrailMateCenter\`

Map cache subfolders include:

- `tilecache` for OSM tiles
- `terrain-cache` for terrain basemap tiles
- `satellite-cache` for satellite tiles
- `contours/tiles` for generated contour overlays

---

## Repository Scope

Included in this repository:

- Avalonia desktop UI
- HostLink protocol implementation
- Meshtastic protobuf decoding and shared data models
- Local storage, replay, export, and offline map cache tooling
- Propagation engine, contracts, and supporting bridge/service code

Not included:

- Device firmware
- Embedded UI running on the target device
- Mobile apps
- Hosted cloud services

---

## Related Docs

- [Offline map cache integration notes](docs/TRAIL_MATE_CACHE_INTEGRATION.md)
- [Propagation implementation docs](docs/propagation/implementation/README.md)
- [Propagation explainability docs](docs/propagation/explainability/README.md)
- [Propagation knowledge notes](docs/propagation/knowledge/README.md)

---

## License

Licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.
See [LICENSE](LICENSE) for details.
