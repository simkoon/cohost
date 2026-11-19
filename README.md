# CoHost (코호스트)

CoHost는 장비를 지금 누가 쓰고 있는지, 곧 현장 작업자와 원격 접속자를 실시간으로 보여 주는 장비 사용 가시화·원격 제어 도구다. 서로 모른 채 같은 장비를 조작하는 사고를 막는 것이 목표다. 구성 요소는 세 가지다.

- **server**: 인증, 장비 상태, 이력을 맡는 서버와 어드민 화면(한 Docker 이미지)
- **agent**: 각 장비(Windows)에서 도는 서비스. 화면 캡처와 원격 입력, 사용 상태 보고
- **client**: 원격 접속자가 쓰는 Windows 클라이언트(WPF)

현재 단계는 P0(기반)이다. 서버는 빈 어드민과 상태 확인 API까지 동작한다.

## 레포 구성

- `protocol/`: 서버·에이전트·클라이언트가 공유하는 메시지 계약(`CoHost.Contracts`), 원격 바이너리 프레임(`CoHost.Remote.Protocol`), 호환성 테스트
- `server/`: ASP.NET Core 서버(`CoHost.Server`), 어드민 웹(`server/web`, React + Vite), `Dockerfile`
- `agent/`: 장비용 Windows 서비스, 캡처 헬퍼, 트레이 앱
- `client/`: 원격 접속 클라이언트(WPF)
- `docs/`: 설계 문서와 프로토콜 정본
- `scripts/`: 검사·smoke 스크립트
- `.github/workflows/`: 컴포넌트별 CI

## 서버 실행

```bash
docker compose up -d
```

첫 실행에는 이미지를 빌드하므로 몇 분이 걸린다. 뜨고 나면 `https://localhost:8443`에 접속한다. 자체 서명 인증서이므로 브라우저 경고를 통과해야 한다. 어드민에서 서버 버전, protocolVersion, 인증서 지문, DB 상태를 볼 수 있다.

인증서 SHA-256 지문은 서버 로그에서도 확인할 수 있다.

```bash
docker compose logs cohost-server | grep "TLS certificate SHA-256 fingerprint"
```

자체 서명 인증서는 처음 접속하는 쪽이 진짜 서버인지 알 수 없다. 지문을 서버 화면이나 로그에서 직접 확인해 대역 외(메신저·구두 등 이 연결과 다른 경로)로 전달하고, 에이전트와 클라이언트를 설치할 때 `SERVER_FINGERPRINT`로 지정한다. 지정한 지문과 다른 인증서를 내는 서버에는 접속하지 않는다.

멈출 때는 `docker compose down`이다. 볼륨은 남으므로 다시 올려도 지문과 서명키가 그대로다.

## 설정

`.env.example`을 `.env`로 복사해서 값을 정한다. `.env`가 없어도 기본값으로 뜬다.

```bash
cp .env.example .env
```

| 변수 | 기본값 | 설명 |
|---|---|---|
| `COHOST_PORT` | `8443` | 호스트에 게시하는 HTTPS 포트. API, WebSocket, 어드민이 이 포트 하나를 쓴다 |
| `COHOST_HOSTNAMES` | (비어 있음) | 인증서 SAN에 넣을 서버 이름·IP를 쉼표로 구분(예: `cohost.local,192.0.2.10`). `localhost`는 항상 들어간다 |
| `COHOST_DB_PASSWORD` | `cohost-change-me` | PostgreSQL 비밀번호 |
| `COHOST_VERSION` | `dev` | 서버 이미지 태그 |

**기본 DB 비밀번호 경고.** `COHOST_DB_PASSWORD`를 비워 두면 기본값 `cohost-change-me`가 쓰이고, 서버 로그와 어드민 화면에 경고가 나온다. 운영에서는 반드시 긴 임의 값으로 바꾼다. postgres는 데이터 볼륨을 처음 만들 때만 비밀번호를 설정하므로 첫 기동 전에 정한다. 이미 띄운 뒤에는 `.env`만 바꿔서는 비밀번호가 바뀌지 않는다.

postgres 포트(5432)는 호스트에 열지 않는다. 서버 컨테이너만 compose 내부망으로 접근한다.

## 호스트 이름과 인증서

호스트 이름은 최초 기동 전에 정한다. 인증서는 처음 한 번만 만들어지고 이후 `COHOST_HOSTNAMES`를 바꿔도 다시 만들어지지 않는다.

이름을 바꿔야 한다면 `cohost-data` 볼륨의 `/data/certs`를 지우고 다시 띄운다. 지문이 바뀌므로 모든 에이전트와 클라이언트의 `SERVER_FINGERPRINT`를 갱신해야 한다.

직접 발급한 인증서를 쓰려면 `cert.pem`과 `key.pem`을 `/data/certs`에 **둘 다** 넣는다. 하나만 있으면 지문이 몰래 바뀌는 일을 막기 위해 서버가 시작을 멈춘다.

## 폐쇄망 반입

인터넷이 되는 곳에서 이미지를 빌드해 파일로 만든다.

```bash
docker compose build
docker save cohost-server:dev postgres:18-alpine | gzip > cohost-images.tar.gz
```

폐쇄망 서버로 `cohost-images.tar.gz`, `docker-compose.yml`, `.env`를 옮긴 뒤 다음을 실행한다. `COHOST_VERSION`을 바꿔서 빌드했다면 태그를 같게 맞춘다.

```bash
docker load -i cohost-images.tar.gz
docker compose up -d --no-build
```

외부 다운로드 없이 뜬다. `scripts/smoke-compose.sh`가 이 경로(`docker save`, `docker load`, `--no-build --pull never`)를 실제로 확인한다.

## 개발 환경

- .NET SDK 10.0.404(`global.json`)
- Node.js 22.12 이상(CI와 Docker 이미지는 24)
- Docker Compose v2

.NET이 PATH에 없으면 다음을 먼저 실행한다.

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

빌드와 테스트:

```bash
dotnet build server.slnf
dotnet test --solution server.slnf
dotnet build agent.slnf     # macOS에서도 빌드된다. 실행과 테스트는 Windows에서 한다
dotnet build client.slnf    # 위와 같다
```

어드민 웹:

```bash
cd server/web
npm ci && npm run gen && npm run lint && npm run build
```

npm 의존성은 정확한 버전으로 고정하고 lockfile을 쓴다. 설치는 `npm ci`만 쓴다.

Docker 없이 서버만 실행하려면 다음과 같이 한다. `.data/`는 gitignore 대상이다. PostgreSQL이 없으면 어드민에 'DB 연결 안 됨'으로 나온다.

```bash
COHOST_DATA_DIR=.data dotnet run --project server/CoHost.Server
```

## 검사 스크립트

- `scripts/smoke-compose.sh`: 빈 환경에서 compose를 올려 지문·키 영속성·기본 비밀번호 경고·postgres 미게시·non-root·폐쇄망 save/load까지 검증한다
- `scripts/smoke-server-local.sh`: Docker 없이 서버 부트스트랩(TLS, 서명키, 헬스체크)을 검증한다
- `scripts/check-contract-types.sh`: 계약에서 TypeScript 타입이 생성되는지 확인한다
- `scripts/check-hygiene.sh`: 비밀·키·식별 정보가 레포에 들어갔는지 검사한다
- `scripts/check-licenses.sh`: NuGet·npm 의존성 라이선스가 허용 목록 안인지 검사한다
- `scripts/check-workflows.sh`: GitHub Actions 워크플로 구성을 검사한다

## CI

컴포넌트별 워크플로가 있다. server는 ubuntu, agent와 client는 windows에서 돈다. 각 컴포넌트 경로나 `protocol/**`이 바뀔 때만 실행된다(경로 필터).

릴리스 태그 접두어는 `server-v*`, `agent-v*`, `client-v*`이다.

경로 필터로 건너뛴 워크플로는 필수 체크에서 Pending으로 남는다. 그래서 이 워크플로들을 브랜치 보호의 필수 체크로 지정하지 않는다.

## 문서

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): 아키텍처
- [docs/DECISIONS.md](docs/DECISIONS.md): 결정 기록
- [docs/PROTOCOL.md](docs/PROTOCOL.md): 프로토콜 v0.1 정본
- [docs/LICENSE-POLICY.md](docs/LICENSE-POLICY.md): 서드파티 라이선스 정책

## 보안

취약점은 공개 이슈로 올리지 말고 [SECURITY.md](SECURITY.md)의 절차에 따라 신고한다.

## 라이선스

Apache-2.0. [LICENSE](LICENSE)와 [NOTICE](NOTICE)를 본다.
