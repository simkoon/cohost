# Security Policy

**Summary (English).** Please do not report vulnerabilities in public issues. Use the "Report a vulnerability" button on the repository's Security tab (GitHub private vulnerability reporting). We aim to respond within 7 days. CoHost is a remote-control tool, so authentication bypass, token forgery or reuse, hijacking of remote connections, and forged update packages are treated as the highest priority. The operational hardening notes below (Korean) apply to every deployment.

## 지원 범위

`main` 브랜치와 최신 릴리스의 server, agent, client, protocol이 대상이다.

## 취약점 신고

공개 이슈에 올리지 않는다. 이 저장소의 Security 탭에서 "Report a vulnerability"(비공개 취약점 신고)를 눌러 보낸다. 개인 이메일 주소로는 받지 않는다.

> 운영 메모: 레포를 공개한 뒤 저장소 설정(Settings > Code security)에서 private vulnerability reporting을 켠다.

1차 응답 목표는 접수 후 7일 이내다. 재현 방법, 영향 범위, 확인한 버전을 함께 적어 주면 처리가 빨라진다.

### 우선 다루는 유형

CoHost는 원격 제어 도구라서 다음 유형을 가장 먼저 본다.

- 인증 우회
- 연결 토큰의 위조 또는 재사용
- 원격 연결 가로채기(장비 직접 연결 구간의 중간자 공격 포함)
- 업데이트 패키지 위조

## 운영 보안 수칙

- 기본 DB 비밀번호 `cohost-change-me`를 운영 전에 `.env`의 `COHOST_DB_PASSWORD`로 바꾼다. postgres는 볼륨을 처음 만들 때만 비밀번호를 설정하므로 첫 기동 전에 정한다.
- 서버 인증서 지문을 서버 화면이나 로그에서 확인해 대역 외(이 연결과 다른 경로)로 전달하고, 에이전트와 클라이언트에 `SERVER_FINGERPRINT`로 핀닝한다.
- `cohost-data` 볼륨(`/data`의 서명키와 TLS 키)은 백업하고 접근을 제한한다. 이 볼륨이 유출되면 토큰을 위조할 수 있다.
- postgres 포트를 호스트에 열지 않는다. 기본 compose 구성은 이미 열지 않는다.
- 방화벽은 서버 8443과 장비 7443만 연다.
- 키, 인증서, `.env`를 레포에 커밋하지 않는다. `scripts/check-hygiene.sh`가 CI에서 이를 검사한다.
