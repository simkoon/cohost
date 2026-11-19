// All admin UI text lives here (Korean). Keep user-facing strings out of other files for future i18n.
export const t = {
  appName: 'CoHost',
  serverVersion: '서버 버전',
  protocolVersion: '프로토콜 버전',
  certFingerprint: 'TLS 인증서 지문 (SHA-256)',
  fingerprintHint: '에이전트와 클라이언트를 설치할 때 이 지문을 SERVER_FINGERPRINT로 지정합니다.',
  database: '데이터베이스',
  dbOk: '연결됨',
  dbMigrating: '마이그레이션 적용 중',
  dbDown: '연결 안 됨',
  defaultDbPasswordWarning:
    '기본 DB 비밀번호(cohost-change-me)를 사용하고 있습니다. 운영 전에 .env의 COHOST_DB_PASSWORD를 바꾸세요.',
  dashboardTitle: '대시보드',
  dashboardEmpty:
    '등록된 장비가 아직 없습니다. 에이전트 설치와 장비 승인 기능은 다음 버전에서 제공됩니다.',
  loading: '불러오는 중…',
  loadFailed:
    '서버 정보를 불러오지 못했습니다. 서버가 실행 중인지 확인한 뒤 페이지를 새로고침하세요.',
  dbDownHint: '서버 로그에서 데이터베이스 연결 오류를 확인하세요. 확인 명령:',
  dbDownCommand: 'docker compose logs cohost-server',
} as const
