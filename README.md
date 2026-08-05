# OPNX.Lib

[한국어](README.ko.md)

> **License notice:** OPNX.Lib is source-available software, not open-source software. Commercial use and redistribution require prior written permission from OPNX. See [License.txt](License.txt).

OPNX.Lib is a modular .NET infrastructure SDK for stateful video systems such as VMS, NVR, streaming servers, device gateways, media processing services, and monitoring applications.

It provides reusable foundations for network communication, media processing, real-time streaming, ONVIF device integration, and database-backed entity management. OPNX.Lib is the core library layer used by OPNX applications including OPNX.V and OPNX.UI.

## Main Capabilities

- **Common infrastructure** — lifecycle management, serialization, compression, reflection, shared models, and utilities.
- **Networking and protocols** — TCP, named pipes, shared memory, framing, packets, and connection-oriented communication.
- **Media processing** — FFmpeg, OpenCV, and SkiaSharp-based encoding, decoding, conversion, filtering, muxing, and media data handling.
- **Real-time streaming** — RTSP-oriented streaming infrastructure and reusable media transport components.
- **ONVIF device integration** — device service initialization, media profiles, RTSP URI lookup, PTZ, presets, imaging controls, relays, and PullPoint events.
- **Data and entity storage** — PostgreSQL/MySQL persistence, typed queries, entity storage, synchronous and asynchronous CRUD, batching, and transactions.

## Project Modules

| Module | Purpose |
| --- | --- |
| `OPNX.Lib.Common` | Shared primitives, lifecycle management, serialization, compression, reflection, and utilities. |
| `OPNX.Lib.Network` | TCP, named pipes, shared memory, framing, packets, and connection management. |
| `OPNX.Lib.Media` | FFmpeg, OpenCV, and SkiaSharp-based media processing components. |
| `OPNX.Lib.Streaming` | RTSP-oriented infrastructure and real-time media transport building blocks. |
| `OPNX.Lib.Onvif` | ONVIF SOAP client services for network video devices. |
| `OPNX.Lib.Data` | Entity store and ORM-style persistence for PostgreSQL and MySQL. |
| `OPNX.Lib` | Aggregated SDK package containing the primary modules. |

## ONVIF Device Integration

`OPNX.Lib.Onvif` currently supports:

- Device service initialization and advertised service address resolution
- Media profiles and RTSP stream URI lookup
- PTZ continuous movement, stop, and preset operations
- Imaging focus and iris controls
- DeviceIO relay output
- PullPoint event subscriptions
- A simulated camera for application flow and range-conversion testing

Basic usage:

```csharp
using OPNX.Lib.Onvif;
using OPNX.Lib.Onvif.Models;

await using var client = new OnvifClient(new OnvifClientOptions
{
    DeviceServiceUri = new Uri("http://192.168.0.10/onvif/device_service"),
    UserName = "admin",
    Password = "password"
});

await client.InitializeAsync();
var profile = (await client.Media!.GetProfilesAsync()).First();
await client.Ptz!.ContinuousMoveAsync(profile.Token, 0.5f, 0, 0);
await client.Ptz.StopAsync(profile.Token);
```

Always use the service addresses resolved by `InitializeAsync`. Optional services are exposed as `null` when the device does not advertise them. See the [ONVIF module documentation](src/OPNX.Lib.Onvif/README.md) for more details.

ONVIF is a trademark of ONVIF, Inc. This project is not affiliated with or endorsed by ONVIF.

## Database And Entity Storage

`OPNX.Lib.Data` provides:

- PostgreSQL and MySQL database services
- Attribute-based table and column mapping with configurable naming conventions
- In-memory `EntityStore` synchronization
- Synchronous and asynchronous CRUD operations
- Typed `Query<T>` and `QueryAsync<T>` materialization
- Batch insert, update, and delete operations
- Callback-based synchronous and asynchronous transactions
- Cancellation support for asynchronous operations

Transaction example:

```csharp
await databaseService.ExecuteInTransactionAsync(async (service, cancellationToken) =>
{
    await service.InsertEntityAsync(user, cancellationToken);
    await service.InsertEntityAsync(permission, cancellationToken);
    await service.UpdateEntityAsync(setting, cancellationToken);
});
```

The transaction commits when the callback completes successfully. If opening the connection, executing a command, or committing fails, it rolls back and rethrows the exception to the caller. All commands within a single transaction share the same connection and must be awaited sequentially. Parallel command execution within a transaction, including `Task.WhenAll`, is not supported. Independent operations outside a transaction use separate connections and may run concurrently.

## Design Direction

OPNX.Lib is an infrastructure layer for long-running video servers, clients, gateways, and media pipelines.

- Application-specific product logic is separated from reusable platform infrastructure.
- Public services depend on logging abstractions rather than a concrete logging framework.
- Consumers may use Serilog, NLog, Microsoft.Extensions.Logging providers, or another logging implementation.
- Native runtimes such as FFmpeg remain separate from OPNX-owned code in licensing and distribution responsibility.
- Modules may be referenced individually, while `OPNX.Lib` provides the aggregated SDK package.

## Use Cases

- Video Management Systems (VMS)
- Network Video Recorders (NVR)
- Network camera and ONVIF device gateways
- Real-time video streaming servers
- Media processing and analysis pipelines
- Server and device state synchronization
- Shared infrastructure for video-platform applications

## Current Status

OPNX.Lib is under active development and its public API is being stabilized. The current package should be treated as a preview SDK for evaluation, integration testing, research, non-commercial experimentation, and early feedback rather than as a production-ready stable SDK.

## NuGet Package

Install the preview package:

```powershell
dotnet add package OPNX.Lib --prerelease
```

API compatibility, package structure, and documentation may change before a stable release.

## Build

Requirements:

- .NET 10 SDK

```powershell
dotnet build OPNX.Lib.slnx -c Debug
```

## Samples And Documentation

Runnable examples are maintained in [OPNXLabs/opnx-samples](https://github.com/OPNXLabs/opnx-samples), including entity storage, TCP communication, RTSP live viewing, and playback timeline integration.

Module-specific documentation:

- [ONVIF client services](src/OPNX.Lib.Onvif/README.md)
- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples)

## License

OPNX.Lib is source-available, but it is not licensed as permissive open-source software. OPNX-owned code may be used for learning, evaluation, research, testing, and other non-commercial purposes.

Commercial use, redistribution, OEM integration, or inclusion in commercial products or services requires prior written permission from OPNX. See [License.txt](License.txt) for full terms. A Korean reference translation is available at [License.ko.txt](License.ko.txt).

## Third-Party Components

This repository uses third-party components under their respective licenses. Native FFmpeg binaries are not covered by the OPNX license, and distributors are responsible for complying with the license terms of their selected FFmpeg build.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.

## Related Projects

- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples) — runnable examples for OPNX.Lib and OPNX.UI.
- `OPNX.UI` — reusable UI components for video client applications.
- `OPNX.V` — a video platform built on OPNX.Lib and OPNX.UI.

## Commercial And OEM Inquiries

- [https://www.opnx.kr/](https://www.opnx.kr/)
- `opnx@opnx.kr`

## Security And Contributions

- Report security issues privately as described in [SECURITY.md](SECURITY.md).
- Review [CONTRIBUTING.md](CONTRIBUTING.md) before opening an issue or proposing a change.
