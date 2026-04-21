# OPNX.Lib

OPNX.Lib is a modular SDK for building stateful video systems such as VMS, NVR, and real-time media platforms.

It provides reusable libraries for networking, media processing, streaming, and database-backed entity storage.

## Projects

- `OPNX.Lib.Common`  
  Shared utilities including logging, lifecycle management, compression, serialization, and common primitives.

- `OPNX.Lib.Network`  
  High-performance transport and protocol components for TCP, named pipe, and shared memory communication.

- `OPNX.Lib.Media`  
  FFmpeg-based media processing utilities for encoding, decoding, conversion, filtering, and muxing.

- `OPNX.Lib.Streaming`  
  RTSP and WebRTC components for real-time video and audio streaming.

- `OPNX.Lib.Data`  
  A database-backed entity store for stateful applications that keep entities in memory and synchronize them with a database.

- `OPNX.Lib`  
  A unified SDK package that references the main OPNX libraries.

## Use Cases

- Video Management Systems (VMS / NVR)
- Real-time streaming systems
- AI-based video processing systems
- Device and server state management systems
- Platform infrastructure development

## License

This repository is source-available for learning, evaluation, research, testing, and other non-commercial use.

Commercial use, redistribution, OEM integration, or inclusion in commercial products or services requires prior written permission from OPNX.

See [License.txt](License.txt) for full terms.

## Third-Party Components

This repository uses third-party software components under their respective licenses.

- `OPNX.Lib.Media` uses `FFmpeg.AutoGen` under the MIT License.
- `OPNX.Lib.Streaming.RTSP` contains code derived from `SharpRTSP` under the MIT License.
- Native `FFmpeg` binaries are not covered by the OPNX license.
- OPNX recommends that end users obtain and configure `FFmpeg` native binaries separately.
- Any party that bundles or redistributes `FFmpeg` native binaries is responsible for complying with the applicable `FFmpeg` license terms for the selected build.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.

## Commercial & OEM Inquiries

For commercial licensing, OEM agreements, or partnership inquiries:

- `opnx@opnx.kr`
