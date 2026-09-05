# OPNX.Lib

[한국어](README.ko.md)

> **License notice:** OPNX.Lib is source-available software, not open-source software. Commercial use and redistribution require prior written permission from OPNX. See [License.txt](License.txt).

OPNX.Lib is a modular .NET infrastructure SDK for stateful video systems such as VMS, NVR, streaming servers, device gateways, media-processing services, and monitoring applications.

## Built For Real-Time Video Systems

OPNX.Lib brings together capabilities that usually require several unrelated libraries and a large amount of product-specific integration code:

- **A complete media pipeline** — FFmpeg-backed audio/video decoding and encoding, pixel and sample conversion, filtering, frame handling, and file muxing. Video decoding includes hardware-device paths such as CUDA, DXVA2, and D3D11VA when the runtime and hardware support them.
- **One protocol model across transports** — the same connection, packet, request/response, serialization, timeout, cancellation, and lifecycle concepts are available over TCP, Named Pipe, and Shared Memory. Applications can choose network IPC, local IPC, or high-throughput memory transport without redesigning their message contract.
- **Video-device integration beyond basic discovery** — ONVIF discovery and service initialization lead directly into media profiles, RTSP URIs, PTZ, presets, imaging, relay outputs, and PullPoint events.
- **Streaming primitives extended from a proven base** — RTSP/RTP code derived from SharpRTSP is adapted for the OPNX runtime and combined with media-payload handling plus WebRTC/DataChannel adapters for live, recording, and playback services.
- **Stateful server infrastructure** — entity persistence, EntityStore synchronization, cascades, transactions, batch/bulk write paths, and system-resource monitoring are designed to coexist in long-running services.

## Main Capabilities

- Common lifecycle, serialization, compression, reflection, and utility infrastructure
- TCP, named pipes, shared memory, packet framing, and ownership-aware binary payload transport
- FFmpeg, OpenCV, and SkiaSharp-based media processing
- Adapted SharpRTSP-derived RTSP/RTP plus preview WebRTC and DataChannel infrastructure
- ONVIF discovery, media, PTZ, presets, imaging, relays, and PullPoint events
- PostgreSQL/MySQL entity persistence, transactions, cascades, batch operations, and multi-row bulk insert
- Windows/Linux system-resource monitoring

## Project Modules

| Module | Purpose |
| --- | --- |
| `OPNX.Lib.Common` | Shared primitives, lifecycle management, serialization, reflection, and utilities |
| `OPNX.Lib.Network` | TCP, named pipes, shared memory, framing, packets, and connection management |
| `OPNX.Lib.Media` | Encoding, decoding, conversion, filtering, muxing, and media data handling |
| `OPNX.Lib.Streaming` | SharpRTSP-derived RTSP/RTP plus WebRTC and DataChannel transport components |
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

## Real-Time Streaming

`OPNX.Lib.Streaming` provides components for RTSP/RTP video transport and WebRTC adapters. It does not impose one complete VMS product workflow; applications compose clients, servers, sessions, transports, and payload processors into live, recording, and playback paths.

| Area | Available components |
| --- | --- |
| RTSP | Client/server infrastructure, request/response messages, Basic/Digest authentication, and sessions |
| RTP/RTCP | UDP and interleaved transports, packet processing, timestamps, and control flow |
| Media payloads | H.264, H.265/HEVC, H.266/VVC, JPEG, AAC, and G.711-family processing |
| SDP | Session-description parsing and media-information handling |
| WebRTC | SIPSorcery and DataChannel-based signal-server and peer-connection adapters |

Portions of the RTSP, RTP/RTCP, SDP, and client/server handling code are derived from [SharpRTSP](https://github.com/ngraziano/SharpRTSP). SharpRTSP is distributed under the MIT License. OPNX has integrated the code into its namespaces and runtime structure and modified or extended areas including nullable annotations, logging abstractions, media-payload handling, and stability behavior. SharpRTSP copyright and MIT terms continue to apply to the upstream-derived portions; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the [SharpRTSP MIT License](third_party_licenses/SharpRTSP-MIT.txt).

The WebRTC and DataChannel components are preview adapters for later product integration. RTSP support in this README does not imply that all protocol code was authored independently by OPNX, and the OPNX.Lib source-available terms do not replace the MIT terms that apply to SharpRTSP-derived code.

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

`OPNX.Lib.Data` is a lightweight data layer with attribute-based mapping, typed queries, synchronous and asynchronous CRUD, partial updates, EntityStore synchronization, foreign-key and cascade policies, cancellation, and callback-based transactions. PostgreSQL and MySQL share the same entity and query contracts, while database-specific SQL generators produce parameterized commands.

### Generic Entity Contracts

The contracts are separated by role so the data layer can support more than the original int-key entity model.

| Contract | Purpose |
| --- | --- |
| `IDatabaseEntity` | Minimum marker contract for all database-mapped models |
| `IKeylessEntity` | Query-only models that do not require a primary key |
| `IEntity<TKey>` | Entities with explicit `int`, `long`, `Guid`, `string`, or other key types |
| `IEntity` | Convenience contract for existing int-key applications |
| `IAuditableEntity` | Creation and modification audit data |
| `ISoftDeletableEntity` | Soft-delete policy |

Auditing and soft deletion are optional contracts rather than requirements on every table. Append-only logs and external schemas can therefore omit UpdateTime or IsDeleted without special bypasses.

### Typed Queries And SQL Generators

`SelectQuery<T>` builds conditions, ranges, sets, ordering, and pages without scattering handwritten SQL through application code.

```csharp
SelectQuery<UserLog> query = SelectQuery<UserLog>.Create()
    .WhereBetween(log => log.EventTimeUtc, fromTimeUtc, toTimeUtc)
    .WhereIn(log => log.Severity, severities)
    .OrderByDescending(log => log.EventTimeUtc)
    .OrderByDescending(log => log.ID)
    .Page(1, 20);

IReadOnlyList<UserLog> logs = await databaseService.SelectAsync(query);
long totalCount = await databaseService.CountAsync(query);
```

The same query model supports `Select`, `Count`, `First`, `FirstOrDefault`, `Any`, multiple ordering clauses, and stable pagination. Values remain parameters, while the PostgreSQL and MySQL generators handle identifier and SQL-dialect differences.

### CRUD, Partial Updates, And EntityStore

In addition to complete entity updates, callers can update only selected properties.

```csharp
await databaseService.UpdateEntityAsync<UserSettings, int>(
    settings,
    cancellationToken,
    entity => entity.Theme,
    entity => entity.Language);
```

After a successful database operation, the configured `EntityStore` synchronizes insert, update, and delete results and publishes change events. EntityStore is more than a cache: it provides key-based lookup, shared state, and downstream change flow for long-running components such as servers and clients.

EntityStore maintains one authoritative instance per entity type and key. Use that instance for reads and UI binding. Submit inserts with a new entity that is not attached to an EntityStore, and submit updates with a new entity or an editing copy created by `Clone()`. To persist an aggregate, populate the required child collections and submit the parent once; do not add the children to EntityStore first. The database service cascade assigns foreign keys and persists each row in order.

`EntityChanged` does not expose the tracked entity or a clone of its object graph. It publishes an immutable row change containing the entity type, key, operation, original and current values, and changed property names. Consumers that need the current entity look it up by type and key. Remote consumers can apply only `CurrentValues` to their existing authoritative instance, preserving object identity and bindings while reducing clone, graph-serialization, and transport costs.

When an update changes a foreign key, EntityStore compares the FK values before and after applying the update and invalidates only the inverse collection caches for the changed old and new relationships. Moving a user between groups therefore reloads both groups' member collections, while an ordinary property update leaves relationship caches untouched. This is an in-memory navigation-consistency operation; it does not invoke a cascade or mutate additional database rows.

```csharp
databaseService.EntityChanged += (_, change) =>
{
    if (change.Is<Device>() && change.IsChanged(nameof(Device.ConnectionState)))
    {
        Device? current = change.FindCurrent<Device, int>(databaseService.EntityStore);
    }
};
```

If EntityStore synchronization fails after the database operation succeeds, the database result cannot be rolled back and `EntityStoreSynchronizationFailed` is raised. A queued Store failure after commit is also recorded as a critical log, and no automatic recovery is performed. A long-running service may subscribe to schedule a complete EntityStore reload or service recovery. A reconnecting client should use a complete reload to recover any real-time changes it missed while disconnected.

```csharp
await databaseService.ExecuteInTransactionAsync(async (service, cancellationToken) =>
{
    await service.InsertEntityAsync<User, int>(user, cancellationToken);
    await service.InsertEntityAsync<UserPermission, int>(permission, cancellationToken);
    await service.UpdateEntityAsync<UserSettings, int>(setting, cancellationToken);
});
```

A transaction commits when its callback succeeds and rolls back on failure. Commands inside one transaction share one connection and must be awaited sequentially; parallel execution such as `Task.WhenAll` is not supported inside the transaction. Ordinary CRUD retains generated-ID handling, EntityStore synchronization, cascades, and change events.

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

### Deliberate Scope

OPNX.Lib.Data is not intended to reproduce the complete surface area of EF Core. It focuses on predictable operations for stateful services: attribute mapping, typed CRUD, query generation, transactions, cascades, EntityStore, partial updates, batch operations, and bulk insert. Full LINQ translation, lazy loading, migrations, and complex relationship-graph tracking are intentionally outside its scope; applications centered on those features are better served by a general-purpose ORM such as EF Core.

## System Monitoring

`OPNX.Lib.SystemMonitoring` periodically collects CPU, memory, process, network-interface, disk-volume, and platform-specific GPU data. It separates point-in-time `SystemResourceSnapshot` data from operational `SystemResourceState<TKey>`, while `SystemResourceStore<TKey>` shares the latest state for multiple servers or devices by key. Windows and Linux providers are available; individual metrics depend on platform support.

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

---

> **“Have not I commanded thee? Be strong and of a good courage; be not afraid, neither be thou dismayed: for the LORD thy God is with thee whithersoever thou goest.”**
>
> — Joshua 1:9, King James Version (KJV)
