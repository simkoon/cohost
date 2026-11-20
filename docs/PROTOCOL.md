# CoHost 프로토콜 v0.1

CoHost의 서버, 에이전트, 클라이언트가 주고받는 메시지의 정본 문서다. 서버 WebSocket 위의 JSON 메시지(에이전트 `/ws/agent`, 클라이언트 `/ws/client`, 어드민 `/ws/admin`)와, 클라이언트가 장비의 에이전트에 직접 붙는 원격 화면 연결의 바이너리 프레임을 다룬다. 현재 버전은 `0.1`이다.

## 1. 범위와 정본

- 이 문서는 서버 WebSocket JSON 메시지(SPEC 5.1)와 원격 바이너리 프레임(SPEC 5.2)의 정본이다.
- 코드(`CoHost.Contracts`, `CoHost.Remote.Protocol`)와 이 문서는 항상 일치해야 한다. `ProtocolDocTests`가 메시지 판별자, `FrameType` 이름, 모든 enum 와이어 값, 종료 코드, 필수 절 제목이 문서에 있는지 검사하고, 빠지면 테스트가 실패한다.
- 코드와 문서가 어긋나면 코드와 `protocol/compat/` 샘플이 기준이고 문서를 고친다.
- 장비 등록 요청(AGT-02)과 접속 토큰 발급 `POST /api/devices/{id}/connect`(RMT-01)는 HTTP API이며, 해당 phase에서 이 문서에 추가한다. 이 문서의 HTTP 엔드포인트는 P0 범위의 네 개뿐이다(9절).
- 프레임별 payload의 바이너리 레이아웃은 PROTO-03(Phase 3)에서 7절에 추가한다.

## 2. 버전 규칙

- `protocolVersion`은 문자열 `"MAJOR.MINOR"`이다. 현재 값은 `0.1`이고, 코드에는 정수 상수 두 개(`ProtocolInfo.Major`, `ProtocolInfo.Minor`)와 문자열 상수 `ProtocolInfo.Version`이 있다.
- MINOR는 메시지나 필드를 추가할 때만 올린다. 삭제하거나 의미를 바꾸면 MAJOR를 올린다.
- MINOR에서 추가한 필드는 수신 측에서 선택 사항으로 다룬다(없으면 기본값).
- C# 계약에서는 MINOR에서 추가하는 필드를 기본값이 있는 생성자 매개변수(예: `string? Note = null`)로 둔다. 기본값이 없는 매개변수는 필수 필드가 되어, 그 필드를 모르는 구버전 송신자의 메시지가 해석 실패가 된다. v0.1의 필드는 모두 필수다.
- 기존 enum에 값을 추가하면 구버전 수신자가 그 메시지를 해석하지 못한다. 그래서 enum 값 추가는 MAJOR 변경으로 취급한다. 새 값이 필요하면 새 필드를 추가한다.
- 배포 순서는 server, agent, client이다. 서버는 직전 MAJOR까지 받는다.
- 서버는 최소 버전(`minProtocolVersion`)을 `hello.ack`로 알린다. 최소 버전 미만이면 4절의 `4426` 절차를 따른다.
- 원격 화면 연결의 버전 협상은 `Hello` 프레임에서 한다(Phase 3).

## 3. 공통 규칙

### 3.1 봉투

모든 JSON 메시지는 같은 봉투를 쓴다. 루트 키는 `type`, `id`, `ts`, `payload` 네 개다.

```json
{
  "type": "heartbeat",
  "id": "11111111-1111-1111-1111-111111111111",
  "ts": "2026-11-19T05:00:00Z",
  "payload": { }
}
```

- `type`: 소문자 점 구분 문자열이다. SPEC 표기 `local.forceEnd`는 그대로 쓴다. 같은 `type`이 채널마다 다른 방향으로 쓰일 수 있으므로(예: `hello`) 해석은 채널 단위로 한다. JSON 안에서 `type`의 위치는 상관없다.
- `id`: 메시지마다 새로 만드는 UUID다.
- `ts`: UTC 시각, RFC 3339. 송신은 `Z` 또는 `+00:00` 오프셋 표기 모두 가능하고, 수신은 `Z`와 오프셋을 둘 다 허용한다.
- 필드 이름은 camelCase다.
- enum은 snake_case 소문자 문자열이다(6절). 정수 값은 거절한다.
- null은 생략하지 않고 `null`로 명시해서 쓴다. 수신 측도 이를 강제한다. v0.1에서 정의된 payload 필드는 키가 항상 있어야 하고, null 가능 필드도 키를 생략하면 해석 실패다. MINOR에서 추가된 필드는 2절에 따라 생략할 수 있다.

### 3.2 모르는 입력

- 모르는 `type`은 무시한다. 로그에는 type 이름만 남긴다. `ContractJson.Parse`가 돌려주는 type 이름은 로그에 그대로 써도 되는 값이다. 소문자로 시작하는 ASCII 영숫자 구간을 점으로 이은 64자 이하 문자열(예: `local.forceEnd`)이면 그대로 돌려주고, 아니면 `<invalid>`를, `type`이 없으면 `<missing>`을 돌려준다.
- 모르는 필드는 무시한다.
- 해석 실패는 깨진 JSON, 봉투 필드(`id`, `ts`, `payload`)의 누락이나 형식 오류, 모르는 enum 값이나 정수 enum 값, 필수 필드 누락(키가 없음), null 불가 필드의 `null`(컬렉션 필드 포함, 예: `remoteSessions: null`), `payload` 자체의 `null`이다. `ContractJson.Parse`는 이때 `ContractParseException`(`JsonException` 파생)을 던진다. 수신 측은 예외의 `MessageType`(type 이름)과 `Path`(JSON 경로)만 로그로 남기고 메시지를 버린다. 연결은 유지한다. 누락된 값을 기본값(예: `installKind`의 `msi`, UUID의 0)으로 채워 처리하지 않는다. 이 검사는 `ContractJson.Options`의 `RespectRequiredConstructorParameters`와 `RespectNullableAnnotations`가 한다.
  - 배열 원소의 `null`(예: `remotePeers: [null]`)은 파서가 거르지 않는다. 배열 원소는 null이면 안 되며, 수신 핸들러가 검사해서 버린다.
  - 모르는 `type`이어도 봉투의 `id`와 `ts`는 있어야 하고 형식이 맞아야 한다. 봉투가 잘못되면 모르는 type으로 무시하지 않고 해석 실패로 처리한다. `payload`는 알려진 `type`에서만 필수다.
  - 모르는 `type`과 모르는 필드는 해석 실패가 아니다(위 두 항목).
- `payload`는 `agentSecret`, `accessToken` 같은 비밀을 담을 수 있으므로 어떤 경우에도 로그에 쓰지 않는다. 예외 메시지에도 넣지 않는다. `ContractParseException`에는 type 이름(`MessageType`), JSON 경로(`Path`), 위치(`LineNumber`, `BytePositionInLine`)만 들어가고 내부 예외는 없다. 예외 메시지도 `Invalid '<type>' message at <path> (line <n>, byte <n>).` 형식으로 이 값들만 담는다. System.Text.Json이 원래 던지는 예외의 메시지는 깨진 리터럴(예: `fals`, `nul`)부터 버퍼 끝까지의 원문을 담는다. 그래서 뒤에 오는 `agentSecret`이나 `accessToken`까지 드러날 수 있고, `ContractJson.Parse`는 그 예외를 버리고 다시 던진다. `MessageType`은 위 type 이름 규칙을 따르고, type을 읽지 못하면 `<unknown>`이다. `Path`는 `$`로 시작하고 ASCII 식별자 속성 이름(`.name`)과 숫자 첨자(`[0]`)로만 된 256자 이하 경로일 때만 그대로 두고, 아니면 `$`로 바꾼다. 수신 측은 `JsonSerializer`로 메시지를 직접 해석하지 않고 `ContractJson.Parse`를 쓴다.

### 3.3 크기 상한

- WebSocket 텍스트 메시지 하나의 상한은 1 MiB(1048576바이트)다. 넘으면 해석하지 않고 버린다(`ContractJson.MaxMessageBytes`).
- 바이너리 프레임 payload의 상한은 16 MiB다(7절, `FrameHeader.MaxPayload`).

### 3.4 인증서 지문 표기

인증서 지문은 콜론 없는 대문자 hex SHA-256으로 쓴다. 비교할 때는 콜론과 대소문자를 무시한다. 호스트 이름을 바꿔도 자체 서명 인증서는 재생성되지 않으며, 인증서를 새로 만들면 지문이 바뀌어 핀닝한 에이전트가 모두 끊긴다.

## 4. 연결 수명과 종료 코드

1. 연결한 쪽(에이전트, 클라이언트)의 첫 메시지는 `hello`다.
2. 서버는 인증한 뒤 `hello.ack`를 보낸다. payload는 `serverTime`, `protocolVersion`, `minProtocolVersion`, `heartbeatIntervalSec`이고, `heartbeatIntervalSec`의 기본값은 15다(`ProtocolInfo.DefaultHeartbeatIntervalSec`). `hello.ack`는 **v0.1 확장 (D24)** 이다.
3. 에이전트는 `hello.ack`를 받을 때마다(재연결 포함) `state.snapshot`을 보내고, 이후 `heartbeatIntervalSec`마다 `heartbeat`를 보낸다. 서버가 재시작해도 이 스냅샷으로 실시간 상태를 다시 채운다.
4. 서버가 연결을 닫을 때 쓰는 응용 종료 코드는 다음과 같다. 세 코드 모두 SPEC 5.1에 없던 것이며 **v0.1 확장 (D24)** 이다.

| 코드 | 상수 | 의미 | 처리 |
|---|---|---|---|
| `4401` | `WsCloseCodes.AuthFailed` | 인증 실패 | `agentSecret` 또는 JWT가 맞지 않을 때 닫는다. |
| `4409` | `WsCloseCodes.DuplicateConnection` | 중복 연결 | 같은 장비의 새 연결을 받아들이고 기존 연결을 `4409`로 닫는다. |
| `4426` | `WsCloseCodes.UpgradeRequired` | 업그레이드 필요 | `protocolVersion`이나 `appVersion`이 최소 버전 미만이면 서버가 `update.available`을 먼저 보낸 뒤 `4426`으로 닫는다. |

`/ws/admin`은 v0.1에서 `hello`가 없다. 인증은 HTTP 업그레이드 요청에서 하며 AUTH-01(Phase 2)에서 구현한다.

## 5. 서버 WebSocket 메시지

방향 표기: A는 에이전트, S는 서버, C는 클라이언트, Admin은 어드민 화면이다. 필드 표의 "null"은 `null`이 올 수 있는지를 뜻한다. 이 절의 표에 있는 필드(v0.1)는 모두 키가 항상 있어야 한다. "불가" 필드에 `null`이 오거나 키가 빠지면 해석 실패다(3.2).

### 5.1 /ws/agent

| 방향 | `type` | C# 클래스 | payload 타입 |
|---|---|---|---|
| A→S | `hello` | `AgentHello` | `AgentHelloPayload` |
| S→A | `hello.ack` | `AgentHelloAck` | `HelloAckPayload` |
| A→S | `heartbeat` | `AgentHeartbeat` | `HeartbeatPayload` |
| A→S | `state.snapshot` | `AgentStateSnapshot` | `StateSnapshotPayload` |
| A→S | `local.started` | `AgentLocalStarted` | `LocalUsage` |
| A→S | `local.extended` | `AgentLocalExtended` | `LocalExtendedPayload` |
| A→S | `local.ended` | `AgentLocalEnded` | `LocalEndedPayload` |
| A→S | `remote.started` | `AgentRemoteStarted` | `RemotePeer` |
| A→S | `remote.ended` | `AgentRemoteEnded` | `RemoteEndedPayload` |
| A→S | `bypass.detected` | `AgentBypassDetected` | `BypassDetectedPayload` |
| S→A | `local.forceEnd` | `AgentLocalForceEnd` | `LocalForceEndPayload` |
| S→A | `update.available` | `AgentUpdateAvailable` | `UpdateAvailablePayload` |

#### `hello` (A→S)

연결 직후 첫 메시지다. 에이전트의 `installKind` 보고는 이 문서가 정하는 확장이다(아래 "installKind 보고 경로" 절).

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `deviceId` | UUID 문자열 | 불가 | 장비 id |
| `agentSecret` | 문자열 | 불가 | 승인 때 발급된 장비 비밀. 로그에 남기지 않는다. |
| `protocolVersion` | 문자열 | 불가 | 예: `0.1` |
| `appVersion` | 문자열 | 불가 | 에이전트 버전 |
| `installKind` | `InstallKind` | 불가 | `msi` / `zip` / `dev` |

```json
{
  "type": "hello",
  "id": "11111111-1111-1111-1111-111111111111",
  "ts": "2026-11-19T05:00:00Z",
  "payload": {
    "deviceId": "22222222-2222-2222-2222-222222222222",
    "agentSecret": "example-agent-secret",
    "protocolVersion": "0.1",
    "appVersion": "0.1.0",
    "installKind": "msi"
  }
}
```

#### `hello.ack` (S→A)

v0.1 확장 (D24). 필드는 6절의 `HelloAckPayload`와 같다. 에이전트는 이 메시지를 받으면 `state.snapshot`을 보내고 heartbeat 주기를 `heartbeatIntervalSec`으로 맞춘다.

#### `heartbeat` (A→S)

`heartbeatIntervalSec`(기본 15초)마다 보낸다(AGT-03).

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `cpuPercent` | 숫자 | 불가 | CPU 사용률(%) |
| `memoryPercent` | 숫자 | 불가 | 메모리 사용률(%) |
| `consoleUser` | 문자열 | 가능 | 현재 콘솔에 로그온한 사용자. 없으면 `null` |
| `idleSeconds` | 정수 | 불가 | 키보드와 마우스 입력이 없는 시간(초) |

#### `state.snapshot` (A→S)

전체 상태다. 재연결할 때마다(`hello.ack`를 받을 때마다) 보낸다.

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `appVersion` | 문자열 | 불가 | 에이전트 버전 |
| `consoleUser` | 문자열 | 가능 | 콘솔 사용자 |
| `local` | `LocalUsage` | 가능 | 현장 사용 중이면 그 정보, 아니면 `null` |
| `remoteSessions` | `RemotePeer[]` | 불가 | 현재 원격 접속자 목록(없으면 빈 배열) |

```json
{
  "type": "state.snapshot",
  "id": "11111111-1111-1111-1111-111111111111",
  "ts": "2026-11-19T05:00:00+00:00",
  "payload": {
    "appVersion": "0.1.0",
    "consoleUser": "operator01",
    "local": {
      "operatorId": "33333333-3333-3333-3333-333333333333",
      "operatorName": "김철수",
      "task": "레시피 수정",
      "startedAt": "2026-11-19T05:00:00Z",
      "expectedEnd": "2026-11-19T06:00:00Z",
      "authLevel": "pin"
    },
    "remoteSessions": [
      {
        "sid": "55555555-5555-5555-5555-555555555555",
        "userId": "44444444-4444-4444-4444-444444444444",
        "userName": "홍길동",
        "clientIp": "192.0.2.10",
        "startedAt": "2026-11-19T05:00:00Z"
      }
    ]
  }
}
```

#### `local.started` (A→S)

현장 작업자가 '사용 중'을 시작했다. payload는 `LocalUsage`이고 `expectedEnd`는 예상 종료 시각이다(D13). `authLevel`은 인증 수준이다(D8, v0.1에서는 `none`).

#### `local.extended` (A→S)

예상 종료 시각을 연장했다. payload는 `LocalExtendedPayload`다.

#### `local.ended` (A→S)

'사용 중'이 끝났다. payload는 `LocalEndedPayload`이고 `endReason`은 `manual`(작업자가 직접 해제), `expired`(예상 종료 시각이 지나 만료), `admin`(관리자 강제 해제)이다(D13).

#### `remote.started` (A→S)

원격 접속자가 붙었다. payload는 `RemotePeer`다. 같은 장비에 여러 명이 동시에 붙을 수 있다(D4).

#### `remote.ended` (A→S)

원격 세션이 끝났다. payload는 `RemoteEndedPayload`이고 `endReason`은 `viewer`(접속자가 끊음), `local_kick`(현장에서 끊음, D15), `network`(네트워크 단절, Ping/Pong 15초 무응답)이다.

#### `bypass.detected` (A→S)

CoHost를 거치지 않은 접속(`rdp`, `radmin`)을 감지했다(D5). payload는 `BypassDetectedPayload`이고 `detail`에 감지 근거를 문자열로 담는다.

#### `local.forceEnd` (S→A)

관리자의 강제 해제 요청이다(D13). payload는 `LocalForceEndPayload`이고 `reason`은 `null`일 수 있다. 에이전트는 현장 사용을 해제하고 `local.ended`를 `endReason` `admin`으로 답한다.

#### `update.available` (S→A)

업데이트 제안이다. payload는 `UpdateAvailablePayload`(6절)다. 전송 조건과 멱등 규칙은 "update.available 재전송 규칙" 절에 있다.

### 5.2 /ws/client

| 방향 | `type` | C# 클래스 | payload 타입 |
|---|---|---|---|
| C→S | `hello` | `ClientHello` | `ClientHelloPayload` |
| S→C | `hello.ack` | `ClientHelloAck` | `HelloAckPayload` |
| C→S | `subscribe` | `ClientSubscribe` | `SubscribePayload` |
| S→C | `update.available` | `ClientUpdateAvailable` | `UpdateAvailablePayload` |
| S→C | `device.updated` | `ClientDeviceUpdated` | `DeviceState` |

#### `hello` (C→S)

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `accessToken` | 문자열 | 불가 | 로그인으로 받은 access JWT. 로그에 남기지 않는다. |
| `protocolVersion` | 문자열 | 불가 | 예: `0.1` |
| `appVersion` | 문자열 | 불가 | 클라이언트 버전 |
| `installKind` | `InstallKind` | 불가 | `msi` / `zip` / `dev` |

#### `hello.ack` (S→C)

에이전트 채널과 같다(v0.1 확장, D24).

#### `subscribe` (C→S)

받을 장비 범위를 정한다. `deviceIds`와 `groupId`가 둘 다 `null`이면 전체다. 구독하면 서버는 해당 범위의 현재 상태를 `device.updated`로 보내고 이후 변경이 있을 때마다 보낸다.

#### `update.available` (S→C)

에이전트와 같은 payload다. 클라이언트도 같은 재전송 규칙을 따른다.

#### `device.updated` (S→C)

장비 한 대의 상태다. 목록과 대시보드 갱신에 쓴다. payload는 `DeviceState`(6절)다.

```json
{
  "type": "device.updated",
  "id": "11111111-1111-1111-1111-111111111111",
  "ts": "2026-11-19T05:00:00Z",
  "payload": {
    "id": "22222222-2222-2222-2222-222222222222",
    "name": "EQ-001",
    "status": "approved",
    "groupId": "66666666-6666-6666-6666-666666666666",
    "groupName": "라인 A",
    "online": true,
    "local": {
      "operatorId": "33333333-3333-3333-3333-333333333333",
      "operatorName": "김철수",
      "task": "레시피 수정",
      "startedAt": "2026-11-19T05:00:00Z",
      "expectedEnd": "2026-11-19T06:00:00Z",
      "authLevel": "pin"
    },
    "remotePeers": [
      {
        "sid": "55555555-5555-5555-5555-555555555555",
        "userId": "44444444-4444-4444-4444-444444444444",
        "userName": "홍길동",
        "clientIp": "192.0.2.10",
        "startedAt": "2026-11-19T05:00:00Z"
      }
    ],
    "appVersion": "0.1.0",
    "lastSeenAt": "2026-11-19T05:00:00Z"
  }
}
```

### 5.3 /ws/admin

| 방향 | `type` | C# 클래스 | payload 타입 |
|---|---|---|---|
| Admin→S | `subscribe` | `AdminSubscribe` | `SubscribePayload` |
| S→Admin | `device.updated` | `AdminDeviceUpdated` | `DeviceState` |

의미는 `/ws/client`의 같은 이름 메시지와 같다. `hello`와 `hello.ack`는 없다(4절).

## 6. 공용 타입과 enum

### 6.1 공용 타입

#### LocalUsage

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `operatorId` | UUID 문자열 | 불가 | 작업자 id |
| `operatorName` | 문자열 | 불가 | 작업자 이름 |
| `task` | 문자열 | 불가 | 작업 내용 |
| `startedAt` | RFC 3339 시각 | 불가 | 시작 시각 |
| `expectedEnd` | RFC 3339 시각 | 불가 | 예상 종료 시각 |
| `authLevel` | `AuthLevel` | 불가 | `none` / `pin` / `tag` |

#### RemotePeer

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `sid` | UUID 문자열 | 불가 | 원격 세션 id |
| `userId` | UUID 문자열 | 불가 | 접속자 사용자 id |
| `userName` | 문자열 | 불가 | 접속자 표시 이름 |
| `clientIp` | 문자열 | 불가 | 접속한 클라이언트 IP |
| `startedAt` | RFC 3339 시각 | 불가 | 접속 시작 시각 |

#### DeviceState

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `id` | UUID 문자열 | 불가 | 장비 id |
| `name` | 문자열 | 불가 | 장비 이름 |
| `status` | `DeviceStatus` | 불가 | `pending` / `approved` / `disabled` |
| `groupId` | UUID 문자열 | 가능 | 그룹 id |
| `groupName` | 문자열 | 가능 | 그룹 이름 |
| `online` | 불리언 | 불가 | 에이전트 연결 여부 |
| `local` | `LocalUsage` | 가능 | 현장 사용 정보 |
| `remotePeers` | `RemotePeer[]` | 불가 | 원격 접속자 목록 |
| `appVersion` | 문자열 | 가능 | 에이전트 버전 |
| `lastSeenAt` | RFC 3339 시각 | 가능 | 마지막으로 연결된 시각 |

#### HelloAckPayload

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `serverTime` | RFC 3339 시각 | 불가 | 서버 현재 시각 |
| `protocolVersion` | 문자열 | 불가 | 서버의 프로토콜 버전 |
| `minProtocolVersion` | 문자열 | 불가 | 서버가 받아들이는 최소 버전 |
| `heartbeatIntervalSec` | 정수 | 불가 | heartbeat 주기(초), 기본 15 |

#### UpdateAvailablePayload

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `component` | `ComponentKind` | 불가 | `agent` / `client` |
| `kind` | `PackageKind` | 불가 | `msi` / `zip` |
| `version` | 문자열 | 불가 | 대상 버전 |
| `url` | 문자열 | 불가 | 패키지 다운로드 주소 |
| `sha256` | 문자열 | 불가 | 패키지 SHA-256 |
| `signature` | 문자열 | 불가 | Ed25519 서명(base64) |

#### SubscribePayload

| 필드 | 와이어 타입 | null | 설명 |
|---|---|---|---|
| `deviceIds` | UUID 문자열 배열 | 가능 | 받을 장비 |
| `groupId` | UUID 문자열 | 가능 | 받을 그룹 |

둘 다 `null`이면 전체다.

#### 그 밖의 payload

| 타입 | 필드 |
|---|---|
| `LocalExtendedPayload` | `expectedEnd` |
| `LocalEndedPayload` | `endReason`(`LocalEndReason`), `endedAt` |
| `RemoteEndedPayload` | `sid`, `endReason`(`RemoteEndReason`), `endedAt` |
| `BypassDetectedPayload` | `kind`(`BypassKind`), `detail` |
| `LocalForceEndPayload` | `reason`(문자열, null 가능) |

### 6.2 enum 와이어 값

모든 enum은 snake_case 소문자 문자열로 직렬화한다. 아래 값 이외의 문자열과 정수는 해석 실패로 취급한다(3.2).

| enum | 와이어 값 |
|---|---|
| `InstallKind` | `msi`, `zip`, `dev` |
| `PackageKind` | `msi`, `zip` |
| `ComponentKind` | `agent`, `client` |
| `LocalEndReason` | `manual`, `expired`, `admin` |
| `RemoteEndReason` | `viewer`, `local_kick`, `network` |
| `BypassKind` | `rdp`, `radmin` |
| `AuthLevel` | `none`, `pin`, `tag` |
| `DeviceStatus` | `pending`, `approved`, `disabled` |
| `UserRole` | `admin`, `user` |
| `DatabaseStatus` | `ok`, `migrations_pending`, `unavailable` |

## installKind 보고 경로

규칙 (a). 에이전트와 클라이언트는 모두 `hello.payload.installKind`(`msi`, `zip`, `dev`)로 설치 방식을 보고한다.

- SPEC 5.1에는 클라이언트 `hello`에만 이 필드가 있었다. 에이전트 `hello`에 `installKind`를 추가한 것이 이 문서가 정하는 확장이다. v0.1이 첫 버전이므로 두 채널 모두 필수 필드다.
- `installKind`가 빠진 `hello`는 해석 실패로 버린다(3.2). 기본값 `msi`로 채우지 않는다. 그래서 필드를 빠뜨린 개발 빌드가 MSI 업데이트 대상이 되는 일이 없다.
- 값의 출처는 설치 때 설정에 기록된 방식이다. MSI로 설치하면 `msi`, `install.ps1`로 설치하면 `zip`, 설정에 기록이 없으면 `dev`다. 이 기록은 AGT-04(Phase 2)에서 구현한다.
- 서버는 이 값을 연결 메모리에 둔다. Phase 2(P1)에서 `devices.install_kind`와 클라이언트 세션 정보에 저장한다.
- 연결 중에는 바뀌지 않는다. 업데이트는 프로세스를 다시 시작하므로 재연결할 때 다시 보고한다.
- 서버는 `installKind`에 맞는 패키지 kind만 `update.available`로 보낸다(`msi`는 kind `msi`, `zip`은 kind `zip`). `dev`에는 업데이트를 보내지 않는다. 개발 빌드가 스스로를 덮어쓰지 않게 하기 위해서다.

## update.available 재전송 규칙

규칙 (b), D17. 연결이 유지되는 동안에도 서버는 `update.available`을 다시 보낼 수 있다.

**서버가 보내는 경우**

1. `hello`의 `appVersion`이 대상 버전보다 낮을 때.
2. 패키지가 새로 올라오거나 rollout이 바뀌어, 이미 연결된 대상이 새로 포함될 때. 재연결을 기다리지 않고 즉시 보낸다.
3. 받은 쪽이 그 버전으로 올라오지 않은 채 재연결했을 때. 구현상 (1)과 같은 경로(`hello` 처리)다.

**동일성과 멱등**

- 제안의 동일성은 (`component`, `kind`, `version`, `sha256`)이다. `id`는 보낼 때마다 새로 만든다.
- 받은 쪽은 같은 동일성의 제안을 이미 다운로드 중이거나 설치 중이거나 설치했으면 무시한다.
- 더 높은 버전의 제안이 오면 이전 제안을 대체한다.
- rollout 포함 여부는 `hash(deviceId) % 100 < percent`처럼 결정적으로 계산한다. 다시 평가해도 같은 장비가 들락날락하지 않아야 한다. 구현은 OPS-01(Phase 4)에서 한다.

**원격 세션 중**

- 에이전트가 원격 세션 중이면 제안을 메모리에 보관했다가 세션이 끝난 뒤 처리한다. 서버는 그 사이에 다시 보내지 않는다.
- 재연결하면 위 (1)로 다시 받는다.

**최소 버전 미만**

- `protocolVersion`이나 `appVersion`이 최소 버전 미만이면 서버는 `update.available`을 먼저 보내고 그다음에 `4426`으로 연결을 닫는다(D24).

**검증**

- 받은 쪽은 설치하기 전에 `sha256`과 Ed25519 `signature`를 검증한다. 맞지 않으면 설치하지 않는다(D20). 재전송된 메시지를 다시 재생하거나 위조해도 검증을 통과하지 못하면 설치로 이어지지 않는다.
- 클라이언트도 같은 규칙을 따른다.

## 7. 원격 연결 바이너리 프레임

클라이언트가 장비의 에이전트에 TLS(:7443)로 직접 붙은 뒤 주고받는 프레임이다(SPEC 5.2, D4, D21).

### 7.1 프레임 형식

```
[type:u8][flags:u8][length:u32 LE][payload]
```

- 헤더는 6바이트(`FrameHeader.Size`)이고 `length`는 little-endian이다.
- payload 상한은 16 MiB(`FrameHeader.MaxPayload`)다. 넘으면 이후 프레임의 경계를 알 수 없어 재동기화할 수 없으므로 연결을 끊는다. 송신 측은 상한을 넘는 프레임을 만들지 않는다.
- 모르는 `type`은 `length`만큼 읽어서 버리고 계속한다.
- `flags`의 비트 의미는 `type`별로 PROTO-03(Phase 3)에서 정한다. 정의되지 않은 비트는 0으로 보내고, 수신 측은 무시한다.

### 7.2 FrameType 14종

| 이름 | 값 | 방향 | 내용 |
|---|---|---|---|
| `Hello` | 1 | C→A | 접속 토큰, protocolVersion, 화면 능력 |
| `HelloAck` | 2 | A→C | 모니터 목록, 코덱, 현재 다른 접속자 |
| `Video` | 3 | A→C | H.264 Annex-B NAL, 키프레임 플래그, 타임스탬프 |
| `CursorShape` | 4 | A→C | 커서 모양. 커서는 영상과 분리한다. |
| `CursorPos` | 5 | A→C | 커서 위치 |
| `Mouse` | 6 | C→A | 정규화 좌표(0~65535), 버튼, 휠 |
| `Key` | 7 | C→A | 스캔코드, down/up, extended |
| `Sas` | 8 | C→A | Ctrl+Alt+Del |
| `SelectMonitor` | 9 | C→A | 볼 모니터 선택 |
| `RequestKeyframe` | 10 | C→A | 키프레임 요청 |
| `Peers` | 11 | A→C | 접속자 목록 변경(D16 알림) |
| `Kicked` | 12 | A→C | 현장 종료(D15)와 끊은 사람 |
| `Ping` | 13 | 양방향 | 5초 간격 |
| `Pong` | 14 | 양방향 | `Ping`에 대한 응답 |

`Ping`과 `Pong`은 5초 간격이고, 15초 동안 응답이 없으면 연결을 끊는다. 이때 원격 세션의 `endReason`은 `network`다.

### 7.3 토큰 검증

에이전트는 `Hello`의 접속 토큰을 다음으로 검증한다. 토큰이 없는 연결은 거절한다.

- 서명: 캐시해 둔 JWKS로 검증한다. 서버와 연결이 끊겨 있어도 동작한다.
- `dev`: 자기 장비의 id인지 확인한다.
- `exp`: 만료되지 않았는지 확인한다.
- `jti`: 재사용 여부를 확인한다(5분 캐시).

각 프레임 payload의 바이너리 레이아웃은 PROTO-03(Phase 3)에서 이 절에 추가한다.

## 8. 접속 토큰 클레임

원격 접속 토큰은 ES256 JWT다(`ConnectTokenClaims.Algorithm`).

| 클레임 | 의미 |
|---|---|
| `sub` | 접속자 userId |
| `name` | 접속자 표시 이름 |
| `dev` | 대상 장비 deviceId |
| `sid` | 원격 세션 id |
| `jti` | 토큰 id. 에이전트가 5분(300초) 동안 재사용을 막는다. |
| `exp` | 만료 시각. 발급 시각 + 60초 |

공개키는 `GET /api/keys`의 JWKS로 받는다. `kid`는 공개키의 RFC 7638 JWK thumbprint다. 접속 토큰을 발급하는 HTTP API(RMT-01)는 해당 phase에서 추가한다.

## 9. HTTP 엔드포인트 (P0)

### `GET /api/server-info`

서버 정보다. 인증 없이 읽는다. 응답은 `ServerInfo`이고 필드는 camelCase다.

| 필드 | 와이어 타입 | 설명 |
|---|---|---|
| `appName` | 문자열 | 앱 이름 |
| `serverVersion` | 문자열 | 서버 버전 |
| `protocolVersion` | 문자열 | 서버의 프로토콜 버전(`0.1`) |
| `certSha256` | 문자열 | TLS 인증서 지문. 콜론 없는 대문자 hex(3.4) |
| `database` | `DatabaseStatus` | `ok` / `migrations_pending` / `unavailable` |
| `defaultDbPassword` | 불리언 | 기본 DB 비밀번호를 쓰고 있으면 `true`(경고 표시용) |

### `GET /api/keys`

접속 토큰 검증용 JWKS다. 키는 EC P-256 공개키 하나다.

```json
{
  "keys": [
    {
      "kty": "EC",
      "crv": "P-256",
      "x": "example-x-coordinate",
      "y": "example-y-coordinate",
      "kid": "example-key-id",
      "use": "sig",
      "alg": "ES256"
    }
  ]
}
```

### `GET /healthz/live`

프로세스가 살아 있는지만 확인한다. 의존성은 검사하지 않는다.

### `GET /healthz`

DB 연결과 마이그레이션 상태를 확인한다. 정상(Healthy)과 마이그레이션 대기(Degraded)는 200, DB에 닿지 못하면(Unhealthy) 503을 돌려준다.

## 10. 하위 호환 테스트

- 하위 호환 샘플은 `protocol/compat/v{MAJOR.MINOR}/{channel}.{type}.json` 규칙으로 저장한다. 예: `protocol/compat/v0.1/agent.hello.json`.
- 릴리스된 버전의 샘플은 수정하지 않는다. 새 버전은 새 폴더를 추가한다.
- 모든 판별자에 샘플이 하나씩 있어야 하고, 이후 버전의 코드가 이전 버전의 샘플을 계속 해석해야 한다.
- 호환 테스트(`CompatTests`)는 server, agent, client 세 워크플로에서 모두 돈다.

예시 값은 `EQ-001`, `라인 A`, `김철수`, `홍길동`, `cohost.local`, `192.0.2.10` 같은 일반 값만 쓴다. 실제 회사나 현장, 장비 정보, 사설 IP, 실제 키는 문서와 샘플에 넣지 않는다.
