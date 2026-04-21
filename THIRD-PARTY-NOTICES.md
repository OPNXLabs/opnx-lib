# Third-Party Notices

This document describes third-party software used by `OPNX.Lib` that is subject to separate license terms.

The OPNX source-available license applies only to OPNX-owned code. Third-party components remain subject to their own licenses and notices.

## Serilog

- Components:
  - `Serilog` (`4.3.1`)
  - `Serilog.Sinks.Console` (`6.1.1`)
  - `Serilog.Sinks.File` (`7.0.0`)
- Project: [https://serilog.net/](https://serilog.net/)
- Package references:
  - [https://www.nuget.org/packages/Serilog](https://www.nuget.org/packages/Serilog)
  - [https://www.nuget.org/packages/Serilog.Sinks.Console](https://www.nuget.org/packages/Serilog.Sinks.Console)
  - [https://www.nuget.org/packages/Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File)
- License: `Apache-2.0`
- Usage: structured logging infrastructure used by common, media, and streaming components

The full license text is provided in:

- [third_party_licenses/Apache-2.0.txt](third_party_licenses/Apache-2.0.txt)

## ZstdSharp.Port

- Component: `ZstdSharp.Port`
- Version: `0.8.7`
- Project: [https://www.nuget.org/packages/ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port)
- License: `MIT`
- Usage: Zstandard compression support used by networking/common components

The full license text is provided in:

- [third_party_licenses/MIT.txt](third_party_licenses/MIT.txt)

## Npgsql

- Component: `Npgsql`
- Version: `10.0.2`
- Project: [https://www.npgsql.org/](https://www.npgsql.org/)
- Package reference: [https://www.nuget.org/packages/Npgsql](https://www.nuget.org/packages/Npgsql)
- License: `PostgreSQL License`
- Usage: PostgreSQL database provider used by `OPNX.Lib.Data`

The full license text is provided in:

- [third_party_licenses/PostgreSQL-License.txt](third_party_licenses/PostgreSQL-License.txt)

## MySqlConnector

- Component: `MySqlConnector`
- Version: `2.5.0`
- Project: [https://mysqlconnector.net/](https://mysqlconnector.net/)
- Package reference: [https://www.nuget.org/packages/MySqlConnector](https://www.nuget.org/packages/MySqlConnector)
- License: `MIT`
- Usage: MySQL database provider used by `OPNX.Lib.Data`

The full license text is provided in:

- [third_party_licenses/MIT.txt](third_party_licenses/MIT.txt)

## OpenCvSharp4

- Component: `OpenCvSharp4`
- Version: `4.13.0.20260318`
- Project: [https://www.nuget.org/packages/OpenCvSharp4](https://www.nuget.org/packages/OpenCvSharp4)
- License: `Apache-2.0`
- Usage: image processing support used by `OPNX.Lib.Media`

The full license text is provided in:

- [third_party_licenses/Apache-2.0.txt](third_party_licenses/Apache-2.0.txt)

## SkiaSharp

- Component: `SkiaSharp`
- Version: `3.119.2`
- Project: [https://www.nuget.org/packages/SkiaSharp](https://www.nuget.org/packages/SkiaSharp)
- License: `MIT`
- Usage: bitmap and drawing support used by `OPNX.Lib.Media`

The full license text is provided in:

- [third_party_licenses/MIT.txt](third_party_licenses/MIT.txt)

## SIPSorcery

- Component: `SIPSorcery`
- Version: `8.0.14`
- Project: [https://github.com/sipsorcery-org/sipsorcery](https://github.com/sipsorcery-org/sipsorcery)
- Package reference: [https://www.nuget.org/packages/SIPSorcery/8.0.14](https://www.nuget.org/packages/SIPSorcery/8.0.14)
- License: `BSD-3-Clause`
- Usage: WebRTC and SIP/media-related support used by `OPNX.Lib.Streaming`

Note:

- This notice applies to the version currently referenced by this repository.
- Future versions may use different license terms and should be reviewed before upgrading.

The full license text is provided in:

- [third_party_licenses/BSD-3-Clause.txt](third_party_licenses/BSD-3-Clause.txt)

## FFmpeg.AutoGen

- Component: `FFmpeg.AutoGen`
- Version: `8.0.0.1`
- Project: [https://www.nuget.org/packages/FFmpeg.AutoGen](https://www.nuget.org/packages/FFmpeg.AutoGen)
- Upstream source: [https://github.com/Ruslan-B/FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
- License: `MIT`
- Usage: .NET bindings used by `OPNX.Lib.Media` to interoperate with FFmpeg native libraries

The full license text is provided in:

- [third_party_licenses/FFmpeg.AutoGen-MIT.txt](third_party_licenses/FFmpeg.AutoGen-MIT.txt)

## SharpRTSP

- Component: `SharpRTSP`
- Project: [https://github.com/ngraziano/SharpRTSP](https://github.com/ngraziano/SharpRTSP)
- License: `MIT`
- Usage: source-derived RTSP transport, messaging, RTP/RTCP, SDP, and server/client handling code included in `OPNX.Lib.Streaming/RTSP`
- Modification status: upstream code was incorporated and modified by OPNX

Important notes:

- Portions of `OPNX.Lib.Streaming/RTSP` are derived from `SharpRTSP`.
- The original `SharpRTSP` license and attribution requirements continue to apply to the derived portions.
- OPNX modifications are provided alongside the upstream-derived code, but inclusion in this repository does not revoke or narrow rights granted under the upstream MIT license for the upstream-derived portions.

The full license text is provided in:

- [third_party_licenses/SharpRTSP-MIT.txt](third_party_licenses/SharpRTSP-MIT.txt)

## FFmpeg Native Libraries

- Component: `FFmpeg` native libraries
- Typical libraries: `avcodec`, `avformat`, `avutil`, `swresample`, `swscale`, `avfilter`, `avdevice`
- Project: [https://ffmpeg.org/](https://ffmpeg.org/)
- License: depends on the selected build and enabled components

Important notes:

- `OPNX.Lib.Media` is designed to work with FFmpeg native libraries, but the native FFmpeg license is separate from `FFmpeg.AutoGen`.
- OPNX does not grant any rights to FFmpeg native binaries under the OPNX source-available license.
- OPNX recommends that users obtain and configure FFmpeg native binaries separately.
- If you bundle, redistribute, or otherwise provide FFmpeg native binaries with your product or service, you are responsible for complying with the applicable FFmpeg license terms for the specific build you use.
- FFmpeg is generally available under `LGPL-2.1-or-later`, but some builds or enabled components may cause `GPL` terms to apply.

Reference materials included in this repository:

- [third_party_licenses/FFmpeg-License-Reference.txt](third_party_licenses/FFmpeg-License-Reference.txt)
- [third_party_licenses/FFmpeg-Native-Binaries-NOTICE.txt](third_party_licenses/FFmpeg-Native-Binaries-NOTICE.txt)
