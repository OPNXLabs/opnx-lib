# OPNX.Lib

[English](README.md)

> **라이선스 안내:** OPNX.Lib는 오픈 소스 소프트웨어가 아닌 source-available 소프트웨어입니다. 상업적 사용과 재배포에는 OPNX의 사전 서면 허가가 필요합니다. 자세한 내용은 [License.txt](License.txt)를 확인하십시오.

OPNX.Lib는 VMS, NVR, 스트리밍 서버, 장치 게이트웨이, 미디어 처리 서비스 및 모니터링 애플리케이션을 위한 모듈형 .NET 인프라 SDK입니다.

## 실시간 영상 시스템을 위한 통합 기반

OPNX.Lib는 일반적으로 여러 라이브러리와 많은 제품별 통합 코드가 필요한 기능을 하나의 일관된 기반으로 제공합니다.

- **완성도 높은 미디어 파이프라인** — FFmpeg 기반 오디오·비디오 디코딩과 인코딩, Pixel·Sample 변환, 필터링, Frame 처리 및 파일 Muxing을 제공합니다. Runtime과 Hardware가 지원하면 CUDA, DXVA2, D3D11VA 같은 Hardware Decode 경로도 사용할 수 있습니다.
- **Transport가 달라도 하나의 Protocol 모델** — TCP, Named Pipe, Shared Memory에서 동일한 Connection, Packet, Request/Response, Serialization, Timeout, Cancellation 및 수명주기 개념을 사용합니다. 메시지 계약을 다시 설계하지 않고 Network IPC, Local IPC 또는 고속 Memory 전송을 선택할 수 있습니다.
- **검색을 넘어 실제 제어까지 이어지는 영상 장치 연동** — ONVIF Discovery와 Service 초기화에서 Media Profile, RTSP URI, PTZ, Preset, Imaging, Relay 및 PullPoint Event까지 연결됩니다.
- **단순 Wrapper가 아닌 Streaming 구성요소** — RTSP Client/Server, RTP Transport, Media Packet 처리와 소유권 기반 Binary Payload를 조합하여 Live, Recording 및 Playback 서비스를 구성할 수 있습니다.
- **상태를 가진 서버를 위한 인프라** — Entity 저장, EntityStore 동기화, Cascade, Transaction, Batch/Bulk 쓰기와 시스템 자원 모니터링이 장기 실행 서비스 안에서 함께 동작하도록 설계되어 있습니다.

## 주요 기능

- 수명주기, 직렬화, 압축, 리플렉션 및 공통 유틸리티
- TCP, Named Pipe, Shared Memory, 패킷 프레이밍 및 소유권 기반 바이너리 페이로드 전송
- FFmpeg, OpenCV, SkiaSharp 기반 미디어 처리
- RTSP 중심 실시간 스트리밍 인프라
- ONVIF 검색, Media, PTZ, Preset, Imaging, Relay 및 PullPoint Event
- PostgreSQL/MySQL 엔티티 저장, 트랜잭션, Cascade, Batch 및 Multi-row Bulk Insert
- Windows/Linux 시스템 자원 모니터링

## 프로젝트 모듈

| 모듈 | 역할 |
| --- | --- |
| `OPNX.Lib.Common` | 공통 자료형, 수명주기, 직렬화, 리플렉션 및 유틸리티 |
| `OPNX.Lib.Network` | TCP, Named Pipe, Shared Memory, 패킷과 연결 관리 |
| `OPNX.Lib.Media` | 인코딩, 디코딩, 변환, 필터링, Muxing 및 미디어 데이터 처리 |
| `OPNX.Lib.Streaming` | RTSP 및 실시간 미디어 전송 구성요소 |
| `OPNX.Lib.Onvif` | ONVIF 검색과 네트워크 영상 장치 SOAP 클라이언트 |
| `OPNX.Lib.Data` | PostgreSQL/MySQL용 EntityStore 및 경량 ORM |
| `OPNX.Lib.SystemMonitoring` | 시스템 자원 수집, 상태 모델 및 저장소 |
| `OPNX.Lib` | 주요 모듈을 포함하는 통합 SDK 패키지 |

## 미디어 처리

`OPNX.Lib.Media`는 특정 Player나 Recorder에 묶인 기능이 아니라 재사용 가능한 FFmpeg 기반 구성요소를 제공합니다.

| 영역 | 제공 구성요소 |
| --- | --- |
| Video | Decode, Encode, Pixel Format·크기 변환, Filtering, Frame Pooling 및 Muxing |
| Audio | Decode, Encode, Sample Format·Rate·Channel 변환 및 Frame 처리 |
| Hardware 경로 | 지원 환경에서 CUDA, DXVA2 및 D3D11VA 선택과 Fallback |
| Image 처리 | 변환 및 이미지 작업을 위한 OpenCV·SkiaSharp 연동 |
| Runtime 통합 | 명시적인 FFmpeg Native Library 초기화와 결정적인 자원 해제 |

따라서 제품 역할을 라이브러리에 고정하지 않으면서 Live Viewer, Transcoder, Recorder, Thumbnail 생성기, 영상 분석 전처리 및 Streaming Gateway의 미디어 계층으로 사용할 수 있습니다.

## 통합 Network 및 IPC Protocol

`OPNX.Lib.Network`는 애플리케이션 Protocol과 실제 Transport를 분리합니다.

| Transport | 대표 용도 |
| --- | --- |
| TCP | 서로 다른 장비 또는 독립 배포 서비스 간 통신 |
| Named Pipe | 운영체제 IPC 특성을 사용하는 로컬 프로세스 간 통신 |
| Shared Memory | 불필요한 복사와 Socket 비용을 줄여야 하는 대용량 로컬 전송 |

각 Transport는 공통 Packet Framing과 Protocol 동작을 공유합니다. Typed Serialization, Request/Response 연결, 비동기 송수신, Cancellation, Timeout, Connection 수명주기 및 Payload 제한을 같은 방식으로 다룰 수 있으므로 배포 경계가 바뀌어도 메시지 모델을 유지할 수 있습니다.

## ONVIF 장치 연동

`OPNX.Lib.Onvif`는 다음 기능을 제공합니다.

- 취소, 중복 제거 및 제한된 재시도를 지원하는 WS-Discovery
- Device Service 초기화와 장치가 제공하는 서비스 주소 확인
- Media Profile 및 RTSP Stream URI 조회
- PTZ 이동·정지 및 Preset 제어
- Focus와 Iris 제어
- Relay 출력 및 PullPoint Event 구독
- 통합 테스트용 모의 카메라

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

항상 `InitializeAsync`가 확인한 서비스 주소를 사용해야 합니다. 장치가 광고하지 않는 선택 서비스는 `null`로 제공됩니다. 자세한 내용은 [ONVIF 모듈 문서](src/OPNX.Lib.Onvif/README.md)를 참고하십시오.

ONVIF는 ONVIF, Inc.의 상표입니다. 이 프로젝트는 ONVIF와 제휴하거나 보증받은 프로젝트가 아닙니다.

## 데이터베이스 및 엔티티 저장소

`OPNX.Lib.Data`는 Attribute 기반 매핑, Typed Query, 동기·비동기 CRUD, EntityStore 동기화, Foreign Key와 Cascade 정책, 취소 및 Callback 기반 Transaction을 제공합니다.

```csharp
await databaseService.ExecuteInTransactionAsync(async (service, cancellationToken) =>
{
    await service.InsertEntityAsync(user, cancellationToken);
    await service.InsertEntityAsync(permission, cancellationToken);
    await service.UpdateEntityAsync(setting, cancellationToken);
});
```

Callback이 성공하면 Commit하고 실패하면 Rollback합니다. 하나의 Transaction 내부 명령은 동일 Connection을 공유하므로 순차적으로 `await`해야 하며 `Task.WhenAll` 같은 병렬 실행은 지원하지 않습니다.

### Batch와 Bulk Insert

Batch 작업은 일반 엔티티 작업을 하나의 Transaction에서 순차 실행합니다. 생성 ID, EntityStore 동기화 및 Cascade 처리를 유지합니다.

Bulk Insert는 생성 ID, EntityStore 또는 Cascade가 필요 없는 Append-only 메타데이터·Telemetry·대용량 기록을 위한 명시적 허용 경로입니다. PostgreSQL과 MySQL에서 Parameter 기반 Multi-row `INSERT`를 제한된 Chunk로 나누어 실행하며 전체 입력은 하나의 Transaction으로 처리합니다.

```csharp
[EntityTable("analysis_metadata", SupportsBulkInsert = true)]
public sealed class AnalysisMetadata : Entity
{
}

await databaseService.BulkInsertAsync(metadataItems);
```

| 기능 | Batch Insert | Bulk Insert |
| --- | --- | --- |
| SQL 실행 | 엔티티별 Insert | Multi-row Insert |
| Transaction | 전체 입력당 1개 | 전체 입력당 1개 |
| 생성 ID | 엔티티에 반영 | 반환·반영하지 않음 |
| EntityStore | 동기화 | 갱신하지 않음 |
| Cascade | 지원 | 미지원 |
| 용도 | 상태를 가진 업무 엔티티 | Append-only 대용량 데이터 |

## 시스템 모니터링

`OPNX.Lib.SystemMonitoring`은 Platform Provider와 공통 상태 저장소를 통해 CPU, 메모리, 네트워크, 디스크 및 플랫폼별 GPU 자원을 주기적으로 수집합니다. Windows와 Linux Provider가 있으며 세부 Metric 지원 범위는 플랫폼에 따라 다릅니다.

## 설계 방향

- 제품 고유 로직과 재사용 가능한 인프라를 분리합니다.
- 공개 서비스는 특정 Logging Framework 대신 Logging 추상화에 의존합니다.
- FFmpeg 같은 Native Runtime은 라이선스와 배포 책임을 OPNX 코드와 분리합니다.
- 모듈을 개별 참조할 수 있고 `OPNX.Lib`는 통합 패키지를 제공합니다.

## 현재 상태

OPNX.Lib는 활발히 개발 중이며 공개 API를 안정화하고 있습니다. 현재 패키지는 평가, 통합 테스트, 연구 및 초기 피드백을 위한 Preview SDK로 보아야 합니다.

## NuGet 패키지 및 빌드

```powershell
dotnet add package OPNX.Lib --prerelease
dotnet build OPNX.Lib.slnx -c Debug
```

요구 사항은 .NET 10 SDK입니다.

## 샘플 및 문서

- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples)
- [ONVIF 클라이언트 서비스](src/OPNX.Lib.Onvif/README.md)

## 라이선스 및 지원

OPNX.Lib는 source-available이지만 permissive 오픈 소스 라이선스가 아닙니다. 상업적 사용, 재배포, OEM 통합 또는 상용 제품 포함에는 OPNX의 사전 서면 허가가 필요합니다. [License.txt](License.txt), [License.ko.txt](License.ko.txt), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 확인하십시오.

- 웹사이트: [https://www.opnx.kr/](https://www.opnx.kr/)
- 문의: `opnx@opnx.kr`
- 보안: [SECURITY.ko.md](SECURITY.ko.md)
- 기여: [CONTRIBUTING.ko.md](CONTRIBUTING.ko.md)

## 관련 프로젝트

- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples) — 실행 가능한 예제
- `OPNX.UI` — 영상 클라이언트용 재사용 UI 구성요소
- `OPNX.V` — OPNX.Lib와 OPNX.UI 기반 영상 플랫폼 애플리케이션 제품군
