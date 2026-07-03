# OPNX.Lib

[Korean](README.ko.md)

OPNX.Lib is a modular .NET infrastructure SDK for building stateful video systems, including VMS, NVR, streaming servers, device gateways, media processing services, and video-platform applications.

The library provides reusable .NET modules for the foundations that video systems repeatedly need: network communication, packet-oriented protocol handling, media processing, real-time streaming, and database-backed entity storage.

OPNX.Lib is the core library layer of the OPNX ecosystem. It is designed to separate application-specific product logic from complex video-platform infrastructure, so higher-level applications can be built on a more stable and consistent foundation.

## Why OPNX.Lib Exists

Video systems are not just media playback applications.

Real VMS, NVR, monitoring clients, streaming servers, and device integration servers repeatedly need the same difficult foundations:

- long-lived network connections
- packet-oriented protocol handling
- real-time video and audio stream processing
- media transport foundations such as RTSP, RTP, and WebRTC
- integration with native and media libraries such as FFmpeg, OpenCV, and SkiaSharp
- stateful entity management for devices, channels, servers, users, and configuration
- synchronization between in-memory state and database-backed storage
- common utilities and lifecycle management that can be used consistently across applications

OPNX.Lib exists so these foundations do not have to be rebuilt separately for every application.

It is used as the infrastructure layer beneath OPNX-based applications such as OPNX.V and OPNX.UI, while also being developed as an independent foundation SDK for video-oriented servers, gateways, media processing pipelines, and monitoring systems.

## What It Provides

OPNX.Lib is organized around the following areas.

- Common infrastructure  
  Lifecycle management, serialization, compression, reflection, shared models, and utility components.

- Networking and protocol handling  
  Building blocks for TCP, named pipes, shared memory, framing, packet handling, and connection-oriented communication.

- Media processing  
  Helpers and components for FFmpeg, OpenCV, and SkiaSharp-based encoding, decoding, conversion, filtering, muxing, and image/media data handling.

- Real-time streaming  
  RTSP-oriented streaming infrastructure and reusable components for real-time media transport.

- Data and entity storage  
  Store and ORM-style infrastructure for applications that keep stateful entities in memory and synchronize them with a database.

## Project Modules

- `OPNX.Lib.Common`  
  Shared primitives and utilities for lifecycle management, serialization, compression, reflection, and common application infrastructure.

- `OPNX.Lib.Network`  
  Transport and protocol components for TCP, named pipes, shared memory, framing, packets, and connection-oriented communication.

- `OPNX.Lib.Media`  
  FFmpeg, OpenCV, and SkiaSharp-oriented components for encoding, decoding, conversion, filtering, muxing, and media data handling.

- `OPNX.Lib.Streaming`  
  RTSP-oriented infrastructure and building blocks for real-time media transport.

- `OPNX.Lib.Data`  
  Entity store and ORM-style infrastructure for working with in-memory entity state and database-backed persistence.

- `OPNX.Lib`  
  Aggregated SDK package that references the main OPNX.Lib modules.

## Design Direction

OPNX.Lib is designed as an infrastructure layer for larger video systems, not as application-specific code for a single product.

- It separates application product logic from reusable platform infrastructure.
- It is designed with long-running servers, clients, gateways, and media pipelines in mind.
- Libraries depend on logging abstractions instead of a concrete logging framework.
- Consumers can use Serilog, NLog, Microsoft.Extensions.Logging providers, or another logging implementation.
- Runtime services, stores, readers, writers, and connection objects accept optional `ILogger` instances where diagnostics are useful.
- Static models and pure data structures avoid logger ownership unless there is a clear operational reason.
- Native runtimes such as FFmpeg are kept separate from OPNX-owned code in both licensing and distribution responsibility.

## Use Cases

- Video Management Systems, VMS
- Network Video Recorders, NVR
- Real-time video streaming servers
- Device gateways
- Media processing and analysis pipelines
- Server/device state synchronization systems
- Shared infrastructure for video-oriented platform applications

## Current Status

OPNX.Lib is under active development.

The public API surface is being stabilized, and samples/API documentation will be added separately as the project matures.

The current repository should be treated as a preview-quality SDK for evaluation, integration testing, research, non-commercial experimentation, and early feedback rather than as a production-ready SDK.

## Build

Requirements:

- .NET 10 SDK

Build:

```powershell
dotnet build OPNX.Lib.slnx -c Debug
```

## Samples And Documentation Roadmap

Samples, API documentation, and integration guides are not included yet.

Initial documentation targets are expected to cover:

- network connections and packet flow
- entity store and database usage
- media reader/writer usage
- streaming components
- logging integration
- FFmpeg native library configuration

## License

OPNX.Lib is source-available, but it is not licensed as permissive open-source software.

OPNX-owned code in this repository may be used for learning, evaluation, research, testing, and other non-commercial purposes.

Commercial use, redistribution, OEM integration, or inclusion in commercial products or services requires prior written permission from OPNX.

See [License.txt](License.txt) for full terms. A Korean reference translation is available at [License.ko.txt](License.ko.txt).

## Third-Party Components

This repository uses third-party software components under their respective licenses.

Important notes:

- `FFmpeg.AutoGen` is used under the MIT License.
- Native `FFmpeg` binaries are not covered by the OPNX license.
- OPNX recommends that users obtain and configure native FFmpeg binaries separately.
- Any party that bundles or redistributes native FFmpeg binaries is responsible for complying with the license terms that apply to the selected FFmpeg build.
- OpenCV, SkiaSharp, SIPSorcery, Npgsql, MySqlConnector, ZstdSharp.Port, and other third-party components remain subject to their own license terms.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.

## Related Projects

- `OPNX.UI`  
  WPF UI control library for OPNX-based applications.

- `OPNX.V`  
  Video platform applications built on top of OPNX.Lib and OPNX.UI.

## Commercial And OEM Inquiries

For commercial licensing, OEM agreements, or partnership inquiries, contact:

- `opnx@opnx.kr`
