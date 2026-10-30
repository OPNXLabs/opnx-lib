# Third-Party Notices

This document describes third-party software used by `OPNX.Lib` that is subject to separate license terms.

The OPNX source-available license applies only to OPNX-owned code in this repository. Third-party packages, source-derived portions, native runtimes, and their accompanying notices remain subject to their own license terms and attribution requirements.

## Scope

This notice covers the third-party components currently referenced or included by this repository, including:

- direct NuGet package references used by OPNX.Lib projects
- selected transitive dependencies that are relevant to runtime behavior or redistribution review
- source-derived RTSP code incorporated from SharpRTSP
- native FFmpeg libraries that OPNX.Lib.Media can interoperate with, but does not license or provide under the OPNX license

Users who redistribute, package, or deploy software based on OPNX.Lib are responsible for reviewing the exact dependency graph and native binaries they ship.

## Direct NuGet Package References

The following versions describe current repository source. Previously published packages can have a different graph. NuGet metadata and the restored assets for the shipped application determine its actual dependencies.

## ZstdSharp.Port

- Component: `ZstdSharp.Port`
- Version: `0.8.8`
- Project: [https://www.nuget.org/packages/ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port)
- License: `MIT`
- Used by: `OPNX.Lib.Common`
- Usage: Zstandard compression support used by common/network-oriented infrastructure

The full license text is provided in:

- [third_party_licenses/ZstdSharp-MIT.txt](third_party_licenses/ZstdSharp-MIT.txt)

## Npgsql

- Component: `Npgsql`
- Version: `10.0.3`
- Project: [https://www.npgsql.org/](https://www.npgsql.org/)
- Package reference: [https://www.nuget.org/packages/Npgsql](https://www.nuget.org/packages/Npgsql)
- License: `PostgreSQL License`
- Used by: `OPNX.Lib.Data`
- Usage: PostgreSQL database provider used by the data/entity storage infrastructure

The full license text is provided in:

- [third_party_licenses/PostgreSQL-License.txt](third_party_licenses/PostgreSQL-License.txt)

## MySqlConnector

- Component: `MySqlConnector`
- Version: `2.6.2`
- Project: [https://mysqlconnector.net/](https://mysqlconnector.net/)
- Package reference: [https://www.nuget.org/packages/MySqlConnector](https://www.nuget.org/packages/MySqlConnector)
- License: `MIT`
- Used by: `OPNX.Lib.Data`
- Usage: MySQL database provider used by the data/entity storage infrastructure

The full license text is provided in:

- [third_party_licenses/MySqlConnector-MIT.txt](third_party_licenses/MySqlConnector-MIT.txt)

## FFmpeg.AutoGen

- Component: `FFmpeg.AutoGen`
- Version: `9.0.1.1`
- Project: [https://www.nuget.org/packages/FFmpeg.AutoGen](https://www.nuget.org/packages/FFmpeg.AutoGen)
- Upstream source: [https://github.com/Ruslan-B/FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
- License: `MIT`
- Used by: `OPNX.Lib.Media`
- Usage: .NET bindings used to interoperate with FFmpeg native libraries

The full license text is provided in:

- [third_party_licenses/FFmpeg.AutoGen-MIT.txt](third_party_licenses/FFmpeg.AutoGen-MIT.txt)

## OpenCvSharp4

- Component: `OpenCvSharp4`
- Version: `4.13.0.20260627`
- Project: [https://www.nuget.org/packages/OpenCvSharp4](https://www.nuget.org/packages/OpenCvSharp4)
- License: `Apache-2.0`
- Used by: `OPNX.Lib.Media`
- Usage: image processing support used by media components

The full license text is provided in:

- [third_party_licenses/Apache-2.0.txt](third_party_licenses/Apache-2.0.txt)

## SkiaSharp

- Component: `SkiaSharp`
- Version: `4.156.0-preview.1.26512.10`
- Project: [https://www.nuget.org/packages/SkiaSharp](https://www.nuget.org/packages/SkiaSharp)
- License: `MIT`
- Used by: `OPNX.Lib.Media`
- Usage: bitmap and drawing support used by media components

The full license text is provided in:

- [third_party_licenses/SkiaSharp-MIT.txt](third_party_licenses/SkiaSharp-MIT.txt)

## DataChannelDotnet

- Component: `DataChannelDotnet`
- Version: `1.3.1`
- Project: [https://www.nuget.org/packages/DataChannelDotnet](https://www.nuget.org/packages/DataChannelDotnet)
- Package reference: [https://www.nuget.org/packages/DataChannelDotnet/1.3.1](https://www.nuget.org/packages/DataChannelDotnet/1.3.1)
- License: `MIT`
- Used by: `OPNX.Lib.Streaming`
- Usage: Managed WebRTC peer connections, H.264/PCMU media tracks, and data channel interoperability

The full license text is provided in:

- [third_party_licenses/MIT.txt](third_party_licenses/MIT.txt)

### Native WebRTC Components

The MIT declaration of the managed `DataChannelDotnet` package does not relicense its native dependencies. Package `1.3.1` contains `datachannel` native libraries and, on Windows, OpenSSL 3 `libssl`/`libcrypto` binaries.

- Native [libdatachannel](https://github.com/paullouisageneau/libdatachannel) is distributed under [MPL-2.0](https://github.com/paullouisageneau/libdatachannel/blob/master/LICENSE).
- OpenSSL 3 is distributed under [Apache-2.0](https://www.openssl.org/source/license.html).
- Preserve native upstream notices and provide the corresponding libdatachannel source availability information required by MPL-2.0 for the binary build being redistributed. A link to the latest upstream branch does not identify the exact shipped build.
- The wrapper package metadata identifies the managed author as `ZetrocDev` and declares MIT. Native build revisions and their dependency notices must be checked separately when preparing a distribution.

The OPNX license does not override these terms. See [third_party_licenses/WebRTC-Native-NOTICE.txt](third_party_licenses/WebRTC-Native-NOTICE.txt).

## Framework References

### Microsoft.AspNetCore.App

- Component: ASP.NET Core shared framework (`Microsoft.AspNetCore.App`)
- Target framework: .NET 10
- Project: [https://github.com/dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)
- License: [MIT](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)
- Used by: `OPNX.Lib.Streaming` and the `OPNX.Lib` aggregate package
- Usage: Kestrel HTTP listener and the built-in .NET WebSocket signaling transport
- Deployment: install the ASP.NET Core 10 runtime or publish the consuming application as self-contained.

## Selected Transitive Components

The following components are used directly or transitively. Exact resolved versions depend on the selected target and restore graph; preserve the notices delivered by those packages.

## Microsoft.Extensions.* Abstractions

- Components:
  - `Microsoft.Extensions.DependencyInjection.Abstractions` (resolved transitively; consult the selected target's assets file)
  - `Microsoft.Extensions.Logging.Abstractions` (`11.0.0-rc.2.26504.105`, updated from the common `10.0.0` reference)
- Project: [https://dot.net/](https://dot.net/)
- License: `MIT`
- Usage: logging and dependency-injection abstractions used directly or transitively by OPNX.Lib components

Important notes:

- These packages may ship additional upstream notice files in their own distributions.

The full license text is provided in:

- [third_party_licenses/Microsoft-MIT.txt](third_party_licenses/Microsoft-MIT.txt)

## SkiaSharp Native Assets

- Components:
  - `SkiaSharp.NativeAssets.Win32` (`4.156.0-preview.1.26512.10`)
  - `SkiaSharp.NativeAssets.macOS` (`4.156.0-preview.1.26512.10`)
- License: `MIT`
- Usage: native asset packages referenced transitively by `SkiaSharp`

The full license text is provided in:

- [third_party_licenses/SkiaSharp-MIT.txt](third_party_licenses/SkiaSharp-MIT.txt)
- Native Skia assets also contain upstream notices; retain those from the selected runtime package.

## Source-Derived Components

## SharpRTSP

- Component: `SharpRTSP`
- Project: [https://github.com/ngraziano/SharpRTSP](https://github.com/ngraziano/SharpRTSP)
- License: `MIT`
- Usage: source-derived RTSP transport, messaging, RTP/RTCP, SDP, and server/client handling code included in `OPNX.Lib.Streaming/RTSP`
- Modification status: upstream code was incorporated and modified by OPNX

Important notes:

- Portions of `OPNX.Lib.Streaming/RTSP` are derived from `SharpRTSP`.
- The original `SharpRTSP` license and attribution requirements continue to apply to the upstream-derived portions.
- OPNX modifications are provided alongside the upstream-derived code, but inclusion in this repository does not revoke or narrow rights granted under the upstream MIT license for the upstream-derived portions.

The full license text is provided in:

- [third_party_licenses/SharpRTSP-MIT.txt](third_party_licenses/SharpRTSP-MIT.txt)

## Native Runtime Components

## FFmpeg Native Libraries

- Component: `FFmpeg` native libraries
- Typical libraries: `avcodec`, `avformat`, `avutil`, `swresample`, `swscale`, `avfilter`, `avdevice`
- Project: [https://ffmpeg.org/](https://ffmpeg.org/)
- License: depends on the selected build and enabled components

Important notes:

- `OPNX.Lib.Media` is designed to work with FFmpeg native libraries through `FFmpeg.AutoGen`, but native FFmpeg libraries are separate from `FFmpeg.AutoGen` and from OPNX-owned code.
- OPNX does not grant any rights to FFmpeg native binaries under the OPNX source-available license.
- OPNX recommends that users obtain and configure FFmpeg native binaries separately.
- If you bundle, redistribute, or otherwise provide FFmpeg native binaries with your product or service, you are responsible for complying with the applicable FFmpeg license terms for the specific build you use.
- FFmpeg is generally available under `LGPL-2.1-or-later`, but some builds or enabled components may cause `GPL` terms to apply.

Reference materials included in this repository:

- [third_party_licenses/FFmpeg-License-Reference.txt](third_party_licenses/FFmpeg-License-Reference.txt)
- [third_party_licenses/FFmpeg-Native-Binaries-NOTICE.txt](third_party_licenses/FFmpeg-Native-Binaries-NOTICE.txt)
