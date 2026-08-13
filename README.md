# OPNX.Lib

[한국어](README.ko.md)

> **License notice:** OPNX.Lib is source-available software, not open-source software. Commercial use and redistribution require prior written permission from OPNX. See [License.txt](License.txt).

OPNX.Lib is a modular .NET infrastructure SDK for stateful video systems such as VMS, NVR, streaming servers, device gateways, media-processing services, and monitoring applications.

## Built For Real-Time Video Systems

OPNX.Lib brings together capabilities that usually require several unrelated libraries and a large amount of product-specific integration code:

- **A complete media pipeline** — FFmpeg-backed audio/video decoding and encoding, pixel and sample conversion, filtering, frame handling, and file muxing. Video decoding includes hardware-device paths such as CUDA, DXVA2, and D3D11VA when the runtime and hardware support them.
- **One protocol model across transports** — the same connection, packet, request/response, serialization, timeout, cancellation, and lifecycle concepts are available over TCP, Named Pipe, and Shared Memory. Applications can choose network IPC, local IPC, or high-throughput memory transport without redesigning their message contract.
- **Video-device integration beyond basic discovery** — ONVIF discovery and service initialization lead directly into media profiles, RTSP URIs, PTZ, presets, imaging, relay outputs, and PullPoint events.
- **Streaming primitives, not only wrappers** — RTSP client/server infrastructure, RTP transport, media packet handling, and ownership-aware binary payloads can be composed into live, recording, and playback services.
- **Stateful server infrastructure** — entity persistence, EntityStore synchronization, cascades, transactions, batch/bulk write paths, and system-resource monitoring are designed to coexist in long-running services.

## Main Capabilities

- Common lifecycle, serialization, compression, reflection, and utility infrastructure
- TCP, named pipes, shared memory, packet framing, and ownership-aware binary payload transport
- FFmpeg, OpenCV, and SkiaSharp-based media processing
- RTSP-oriented real-time streaming infrastructure
- ONVIF discovery, media, PTZ, presets, imaging, relays, and PullPoint events
- PostgreSQL/MySQL entity persistence, transactions, cascades, batch operations, and multi-row bulk insert
- Windows/Linux system-resource monitoring

## Project Modules

| Module | Purpose |
| --- | --- |
| `OPNX.Lib.Common` | Shared primitives, lifecycle management, serialization, reflection, and utilities |
| `OPNX.Lib.Network` | TCP, named pipes, shared memory, framing, packets, and connection management |
| `OPNX.Lib.Media` | Encoding, decoding, conversion, filtering, muxing, and media data handling |
| `OPNX.Lib.Streaming` | RTSP and reusable real-time media transport components |
| `OPNX.Lib.Onvif` | ONVIF discovery and SOAP client services for network video devices |
| `OPNX.Lib.Data` | EntityStore and ORM-style persistence for PostgreSQL and MySQL |
| `OPNX.Lib.SystemMonitoring` | System-resource collection, state models, and stores |
| `OPNX.Lib` | Aggregated SDK package containing the primary modules |

## Media Processing

`OPNX.Lib.Media` exposes reusable FFmpeg-based building blocks rather than limiting media processing to one player or recorder implementation.

| Area | Available building blocks |
| --- | --- |
| Video | Decode, encode, pixel-format/size conversion, filtering, frame pooling, and muxing |
| Audio | Decode, encode, sample-format/rate/channel conversion, and frame handling |
| Hardware paths | CUDA, DXVA2, and D3D11VA selection and fallback where supported |
| Image processing | OpenCV and SkiaSharp interoperability for conversion and image workflows |
| Runtime integration | Explicit FFmpeg native-library initialization and deterministic resource disposal |

This makes the media layer useful for live viewers, transcoders, recorders, thumbnail generators, analytics preprocessing, and streaming gateways without embedding those product roles into the library itself.

## Unified Network And IPC Protocol

`OPNX.Lib.Network` separates the application protocol from the underlying transport.

| Transport | Typical use |
| --- | --- |
| TCP | Communication between machines or independently deployed services |
| Named Pipe | Local process-to-process communication with operating-system IPC semantics |
| Shared Memory | High-volume local transfer where avoiding unnecessary copies and socket overhead matters |

The transports share common packet framing and protocol behavior, including typed serialization, request/response correlation, asynchronous send and receive, cancellation, timeout handling, connection lifecycle, and bounded payload rules. A service can therefore keep its message model while selecting the transport that fits its deployment boundary.

## ONVIF Device Integration

`OPNX.Lib.Onvif` currently provides:

- WS-Discovery with cancellation, duplicate filtering, and bounded retry behavior
- Device-service initialization and advertised service-address resolution
- Media profiles and RTSP stream URI lookup
- PTZ movement, stop, and preset operations
- Focus and iris controls
- Relay output and PullPoint event subscriptions
- A simulated camera for integration testing

```csharp
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

Use the service addresses resolved by `InitializeAsync`. Optional services are `null` when a device does not advertise them. See the [ONVIF module documentation](src/OPNX.Lib.Onvif/README.md).

ONVIF is a trademark of ONVIF, Inc. This project is not affiliated with or endorsed by ONVIF.

## Database And Entity Storage

`OPNX.Lib.Data` provides attribute-based mapping, typed queries, synchronous and asynchronous CRUD, EntityStore synchronization, foreign-key and cascade policies, cancellation, and callback-based transactions.

```csharp
await databaseService.ExecuteInTransactionAsync(async (service, cancellationToken) =>
{
    await service.InsertEntityAsync(user, cancellationToken);
    await service.InsertEntityAsync(permission, cancellationToken);
    await service.UpdateEntityAsync(setting, cancellationToken);
});
```

A transaction commits when its callback succeeds and rolls back on failure. Commands inside one transaction share one connection and must be awaited sequentially; parallel execution such as `Task.WhenAll` is not supported inside the transaction.

### Batch And Bulk Insert

Batch operations run ordinary entity operations sequentially in one transaction. They preserve generated-ID handling, EntityStore synchronization, and cascades.

Bulk insert is an opt-in path for append-only metadata, telemetry, and high-volume records that do not require generated IDs, EntityStore synchronization, or cascades. PostgreSQL and MySQL use parameterized multi-row `INSERT` statements split into bounded chunks while the complete input remains one transaction.

```csharp
[EntityTable("analysis_metadata", SupportsBulkInsert = true)]
public sealed class AnalysisMetadata : Entity
{
}

await databaseService.BulkInsertAsync(metadataItems);
```

| Capability | Batch insert | Bulk insert |
| --- | --- | --- |
| SQL execution | One insert command per entity | Multi-row insert commands |
| Transaction | One for the complete input | One for the complete input |
| Generated IDs | Applied | Not returned or applied |
| EntityStore | Synchronized | Not updated |
| Cascades | Supported | Not supported |
| Intended use | Stateful application entities | Append-only high-volume data |

## System Monitoring

`OPNX.Lib.SystemMonitoring` provides periodic CPU, memory, network, disk, and platform-specific GPU resource collection through platform providers and shared resource-state stores. Windows and Linux providers are available; individual metrics depend on platform support.

## Design Direction

- Product-specific logic stays outside the reusable infrastructure layer.
- Public services depend on logging abstractions rather than a concrete logging framework.
- Native runtimes such as FFmpeg remain separate in licensing and distribution responsibility.
- Modules can be referenced individually, while `OPNX.Lib` provides an aggregated package.

## Current Status

OPNX.Lib is under active development. Its public API is still being stabilized and the current package should be treated as a preview SDK for evaluation, integration testing, research, and early feedback.

## NuGet Package And Build

```powershell
dotnet add package OPNX.Lib --prerelease
dotnet build OPNX.Lib.slnx -c Debug
```

Requirements: .NET 10 SDK.

## Samples And Documentation

- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples)
- [ONVIF client services](src/OPNX.Lib.Onvif/README.md)

## License And Support

OPNX.Lib is source-available but is not permissively licensed open-source software. Commercial use, redistribution, OEM integration, or inclusion in commercial products requires prior written permission from OPNX. See [License.txt](License.txt), [License.ko.txt](License.ko.txt), and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

- Website: [https://www.opnx.kr/](https://www.opnx.kr/)
- Contact: `opnx@opnx.kr`
- Security: [SECURITY.md](SECURITY.md)
- Contributions: [CONTRIBUTING.md](CONTRIBUTING.md)

## Related Projects

- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples) — runnable examples
- `OPNX.UI` — reusable UI components for video clients
- `OPNX.V` — a video-platform application suite built on OPNX.Lib and OPNX.UI
