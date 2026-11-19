# 라이선스 정책

CoHost 레포는 Apache License 2.0(`LICENSE`)으로 공개한다. 이 문서는 서드파티 의존성에 적용하는 기준(D22, D25)과 새 의존성을 추가할 때의 절차를 정한다. 기준은 `scripts/check-licenses.sh`가 CI(server 워크플로)에서 강제한다.

## 배포물 허용 목록

배포물은 레포를 받아 실행하는 쪽에 전달되는 모든 것이다. 서버 런타임 의존성(NuGet), 어드민 웹 production 의존성(npm), Docker 이미지, 어드민 `dist`, 이후의 MSI/ZIP이 여기에 속한다. 허용하는 라이선스는 다음과 같다.

- MIT
- BSD-2-Clause, BSD-3-Clause
- Apache-2.0
- ISC
- 0BSD
- PostgreSQL (Npgsql이 이 라이선스다)

`A OR B` 식은 한쪽이라도 허용 목록에 있으면 통과하고, `A AND B` 식은 모든 구성 요소가 허용 목록에 있어야 통과한다. 라이선스 식이 없는 패키지는 통과하지 못한다.

## 빌드 전용 도구 확장 허용

배포물에 들어가지 않고 수정·재배포하지 않는 빌드 전용 도구(npm devDependencies 트리)에는 위 목록에 더해 다음을 허용한다. 현재 필요한 이유를 함께 적는다.

| 라이선스 | 현재 필요한 패키지 | 비고 |
|----------|--------------------|------|
| MPL-2.0 | lightningcss (Vite가 의존) | 수정하지 않고 빌드에만 쓴다 |
| Python-2.0, PSF-2.0 | argparse (json-schema-to-typescript 등의 하위 의존) | 빌드 도구 내부에서만 쓴다 |
| BlueOak-1.0.0 | glob 계열 | D25의 허용형 범주(허용 라이선스에 준하는 관대한 라이선스)로 해석 |
| CC0-1.0 | 공개 도메인 헌정 | D25의 허용형 범주 |
| CC-BY-3.0 | spdx-exceptions (SPDX 데이터 표기) | `(MIT AND CC-BY-3.0)`처럼 복합식으로 나타난다 |

이 확장 목록은 npm의 production 아닌 트리에만 적용한다. 배포물 검사에는 적용하지 않는다. 새 라이선스를 이 표에 추가하려면 어떤 패키지 때문에 필요한지, 왜 배포물에 들어가지 않는지를 이 문서에 먼저 적는다.

## 쓰지 않는 라이선스

GPL, LGPL, AGPL, SSPL 계열은 배포물에도 빌드 도구에도 쓰지 않는다. 두 검사 목록 어디에도 없으므로 CI가 실패한다.

## 코덱

영상 코덱은 OS에 내장된 Media Foundation만 쓴다. FFmpeg와 x264는 번들하지 않는다(D22).

## 새 의존성을 추가할 때

1. 버전을 정확히 고정한다. .NET은 `Directory.Packages.props`에서 10.0.x 범위의 정확한 버전, npm은 배포된 지 14일이 지난 버전을 `package.json`에 정확히 적고 lockfile을 갱신한다. 설치는 항상 `npm ci`다(D26).
2. 라이선스를 확인한다. nuspec의 `license` 식이나 npm 패키지의 `license` 필드를 본다.
3. `scripts/check-licenses.sh`를 통과시킨다. 통과하지 못하면 대체 패키지를 찾거나, 위 확장 목록에 넣을 근거가 있는지 이 문서에서 먼저 논의한다.
