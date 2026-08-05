# OPNX.Lib

[English](README.md)

> **라이선스 안내:** OPNX.Lib는 오픈 소스 소프트웨어가 아닌 source-available 소프트웨어입니다. 상업적 사용과 재배포에는 OPNX의 사전 서면 허가가 필요합니다. 자세한 내용은 [License.txt](License.txt)를 확인하십시오.

OPNX.Lib는 VMS, NVR, 스트리밍 서버, 장치 게이트웨이, 미디어 처리 서비스 및 모니터링 애플리케이션과 같은 상태 기반 영상 시스템을 위한 모듈형 .NET 인프라 SDK입니다.

네트워크 통신, 미디어 처리, 실시간 스트리밍, ONVIF 장치 연동 및 데이터베이스 기반 엔티티 관리를 위한 재사용 가능한 기반 기능을 제공합니다. OPNX.Lib는 OPNX.V와 OPNX.UI를 포함한 OPNX 애플리케이션에서 사용하는 핵심 라이브러리 계층입니다.

## 주요 기능

- **공통 인프라** — 수명 주기 관리, 직렬화, 압축, 리플렉션, 공통 모델 및 유틸리티
- **네트워크 및 프로토콜** — TCP, Named Pipe, Shared Memory, 프레이밍, 패킷 및 연결 기반 통신
- **미디어 처리** — FFmpeg, OpenCV 및 SkiaSharp 기반 인코딩, 디코딩, 변환, 필터링, muxing 및 미디어 데이터 처리
- **실시간 스트리밍** — RTSP 중심의 스트리밍 인프라 및 재사용 가능한 미디어 전송 구성 요소
- **ONVIF 장치 연동** — 장치 서비스 초기화, 미디어 프로필, RTSP URI 조회, PTZ, Preset, Imaging 제어, Relay 및 PullPoint Event
- **데이터 및 엔티티 저장소** — PostgreSQL/MySQL 저장, 타입 기반 조회, 엔티티 저장소, 동기·비동기 CRUD, Batch 및 Transaction

## 프로젝트 모듈

| 모듈 | 역할 |
| --- | --- |
| `OPNX.Lib.Common` | 공통 자료형, 수명 주기 관리, 직렬화, 압축, 리플렉션 및 유틸리티 |
| `OPNX.Lib.Network` | TCP, Named Pipe, Shared Memory, 프레이밍, 패킷 및 연결 관리 |
| `OPNX.Lib.Media` | FFmpeg, OpenCV 및 SkiaSharp 기반 미디어 처리 구성 요소 |
| `OPNX.Lib.Streaming` | RTSP 중심 인프라 및 실시간 미디어 전송 구성 요소 |
| `OPNX.Lib.Onvif` | 네트워크 영상 장치를 위한 ONVIF SOAP 클라이언트 서비스 |
| `OPNX.Lib.Data` | PostgreSQL과 MySQL을 위한 EntityStore 및 ORM 스타일 저장 기능 |
| `OPNX.Lib` | 주요 모듈을 포함하는 통합 SDK 패키지 |

## ONVIF 장치 연동

`OPNX.Lib.Onvif`는 현재 다음 기능을 지원합니다.

- Device Service 초기화 및 장치가 제공하는 서비스 주소 확인
- Media Profile 및 RTSP Stream URI 조회
- PTZ 연속 이동, 정지 및 Preset 제어
- Imaging Focus 및 Iris 제어
- DeviceIO Relay 출력
- PullPoint Event 구독
- 애플리케이션 명령 흐름과 범위 변환을 검증하기 위한 모의 카메라

기본 사용 방법:

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

항상 `InitializeAsync`가 확인한 서비스 주소를 사용해야 합니다. 장치가 광고하지 않는 선택 서비스는 `null`로 제공됩니다. 자세한 내용은 [ONVIF 모듈 문서](src/OPNX.Lib.Onvif/README.md)를 참고하십시오.

ONVIF는 ONVIF, Inc.의 상표입니다. 이 프로젝트는 ONVIF와 제휴하거나 ONVIF의 보증을 받은 프로젝트가 아닙니다.

## 데이터베이스 및 엔티티 저장소

`OPNX.Lib.Data`는 다음 기능을 제공합니다.

- PostgreSQL 및 MySQL 데이터베이스 서비스
- 설정 가능한 명명 규칙과 Attribute 기반 테이블·컬럼 매핑
- 메모리 기반 `EntityStore` 동기화
- 동기·비동기 CRUD
- 타입 기반 `Query<T>` 및 `QueryAsync<T>` 변환
- Batch Insert, Update 및 Delete
- 콜백 방식의 동기·비동기 Transaction
- 비동기 작업의 Cancellation 지원

Transaction 사용 예제:

```csharp
await databaseService.ExecuteInTransactionAsync(async (service, cancellationToken) =>
{
    await service.InsertEntityAsync(user, cancellationToken);
    await service.InsertEntityAsync(permission, cancellationToken);
    await service.UpdateEntityAsync(setting, cancellationToken);
});
```

콜백이 정상적으로 완료되면 Transaction을 Commit합니다. Connection 열기, 명령 실행 또는 Commit 과정에서 오류가 발생하면 Rollback한 후 호출자에게 예외를 다시 전달합니다. 하나의 Transaction에 포함된 모든 명령은 동일한 Connection을 사용하므로 순차적으로 `await`해야 합니다. Transaction 내부에서 `Task.WhenAll` 등을 사용한 병렬 명령 실행은 지원하지 않습니다. Transaction 외부의 독립적인 작업은 각각 별도 Connection을 사용하므로 동시에 실행할 수 있습니다.

## 설계 방향

OPNX.Lib는 장시간 실행되는 영상 서버, 클라이언트, 게이트웨이 및 미디어 파이프라인을 위한 인프라 계층입니다.

- 애플리케이션별 제품 로직과 재사용 가능한 플랫폼 인프라를 분리합니다.
- 공개 서비스는 특정 로깅 프레임워크 대신 로깅 추상화에 의존합니다.
- 사용자는 Serilog, NLog, Microsoft.Extensions.Logging Provider 또는 다른 로깅 구현을 선택할 수 있습니다.
- FFmpeg와 같은 Native Runtime은 라이선스 및 배포 책임 측면에서 OPNX 소유 코드와 분리됩니다.
- 필요한 모듈만 개별 참조할 수 있으며 `OPNX.Lib`는 통합 SDK 패키지를 제공합니다.

## 사용 사례

- Video Management System(VMS)
- Network Video Recorder(NVR)
- 네트워크 카메라 및 ONVIF 장치 게이트웨이
- 실시간 영상 스트리밍 서버
- 미디어 처리 및 분석 파이프라인
- 서버와 장치 상태 동기화
- 영상 플랫폼 애플리케이션의 공통 인프라

## 현재 상태

OPNX.Lib는 현재 활발히 개발 중이며 공개 API를 안정화하고 있습니다. 현재 패키지는 production-ready 안정 버전이 아니라 평가, 통합 테스트, 연구, 비상업적 실험 및 초기 피드백을 위한 Preview SDK로 보아야 합니다.

## NuGet 패키지

Preview 패키지 설치:

```powershell
dotnet add package OPNX.Lib --prerelease
```

안정 버전 출시 전까지 API 호환성, 패키지 구조 및 문서가 변경될 수 있습니다.

## 빌드

요구 사항:

- .NET 10 SDK

```powershell
dotnet build OPNX.Lib.slnx -c Debug
```

## 샘플 및 문서

실행 가능한 예제는 [OPNXLabs/opnx-samples](https://github.com/OPNXLabs/opnx-samples)에서 관리합니다. EntityStore, TCP 통신, RTSP 라이브 뷰어 및 재생 Timeline 연동 예제를 포함합니다.

모듈별 문서:

- [ONVIF 클라이언트 서비스](src/OPNX.Lib.Onvif/README.md)
- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples)

## 라이선스

OPNX.Lib는 source-available 형태로 공개하지만 permissive open-source software로 라이선스하지 않습니다. OPNX 소유 코드는 학습, 평가, 연구, 테스트 및 기타 비상업적 목적으로 사용할 수 있습니다.

상업적 사용, 재배포, OEM 통합 또는 상업용 제품과 서비스에 포함하려면 OPNX의 사전 서면 허가가 필요합니다. 자세한 내용은 [License.txt](License.txt)를 확인하십시오. 한글 참고 번역은 [License.ko.txt](License.ko.txt)에서 확인할 수 있습니다.

## 서드파티 컴포넌트

이 저장소는 각자의 라이선스가 적용되는 서드파티 컴포넌트를 사용합니다. Native FFmpeg Binary는 OPNX 라이선스의 적용 대상이 아니며, 배포자는 선택한 FFmpeg Build에 적용되는 라이선스를 준수할 책임이 있습니다.

자세한 내용은 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 확인하십시오.

## 관련 프로젝트

- [OPNX Samples](https://github.com/OPNXLabs/opnx-samples) — OPNX.Lib 및 OPNX.UI 실행 예제
- `OPNX.UI` — 영상 클라이언트 애플리케이션을 위한 재사용 가능한 UI 구성 요소
- `OPNX.V` — OPNX.Lib와 OPNX.UI를 기반으로 하는 영상 플랫폼

## 상업 라이선스 및 OEM 문의

- [https://www.opnx.kr/](https://www.opnx.kr/)
- `opnx@opnx.kr`

## 보안 및 기여

- 보안 문제는 [SECURITY.ko.md](SECURITY.ko.md)의 안내에 따라 비공개로 제보하십시오.
- Issue 등록이나 변경 제안 전 [CONTRIBUTING.ko.md](CONTRIBUTING.ko.md)를 확인하십시오.
