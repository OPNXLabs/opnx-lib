# OPNX.Lib

OPNX.Lib is a modular .NET SDK for building stateful video systems such as VMS, NVR, streaming servers, device gateways, and media processing platforms.

The library focuses on reusable infrastructure for networking, protocol handling, media processing, real-time streaming, and database-backed entity storage.

## Status

OPNX.Lib is under active development.

The public API surface is being stabilized, and samples/API documentation will be added separately as the project matures. Until then, the repository should be treated as a preview-quality SDK intended for evaluation, integration testing, and early feedback.

## Projects

- `OPNX.Lib.Common`  
  Shared utilities for lifecycle management, serialization, compression, reflection, and common primitives.

- `OPNX.Lib.Network`  
  Transport and protocol components for TCP, named pipes, shared memory, framing, packets, and connection-oriented communication.

- `OPNX.Lib.Media`  
  Media helpers and FFmpeg/OpenCV-oriented components for encoding, decoding, conversion, filtering, muxing, and media data handling.

- `OPNX.Lib.Streaming`  
  Streaming components including RTSP-oriented infrastructure and real-time media transport building blocks.

- `OPNX.Lib.Data`  
  Entity store and ORM-style infrastructure for applications that keep stateful entities in memory and synchronize them with a database.

- `OPNX.Lib`  
  Aggregated SDK package that references the main OPNX.Lib modules.

## Design Direction

OPNX.Lib is designed as infrastructure for larger OPNX and third-party applications.

- Libraries depend on logging abstractions instead of a concrete logging framework.
- Consumers can use Serilog, NLog, Microsoft.Extensions.Logging providers, or another logging implementation.
- Runtime services, stores, readers, writers, and connection objects accept optional `ILogger` instances where diagnostics are useful.
- Static/model-oriented code avoids logger ownership unless there is a clear operational reason.

## Use Cases

- Video Management Systems (VMS / NVR)
- Real-time streaming systems
- Media processing and analysis pipelines
- Device/server state synchronization
- Platform infrastructure for video-oriented applications

## Build

Requirements:

- .NET 10 SDK

Build:

```powershell
dotnet build OPNX.Lib.slnx -c Debug
```

## Samples And Documentation

Samples, API documentation, and integration guides are planned but are not included yet.

The first documentation targets are expected to cover:

- network connections and packet flow
- entity store/database usage
- media reader/writer usage
- streaming components
- logging integration

## License

This repository is source-available for learning, evaluation, research, testing, and other non-commercial use.

Commercial use, redistribution, OEM integration, or inclusion in commercial products or services requires prior written permission from OPNX.

See [License.txt](License.txt) for full terms.

## Third-Party Components

This repository uses third-party software components under their respective licenses.

Important notes:

- `FFmpeg.AutoGen` is used under the MIT License.
- Native `FFmpeg` binaries are not covered by the OPNX license.
- OPNX recommends that users obtain and configure native FFmpeg binaries separately.
- Any party that bundles or redistributes native FFmpeg binaries is responsible for complying with the applicable FFmpeg license terms for the selected build.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.

## Related Projects

- `OPNX.UI` - WPF UI controls for OPNX-based applications.
- `OPNX.V` - Video platform applications built on top of OPNX.Lib and OPNX.UI.

## Commercial And OEM Inquiries

For commercial licensing, OEM agreements, or partnership inquiries:

- `opnx@opnx.kr`
