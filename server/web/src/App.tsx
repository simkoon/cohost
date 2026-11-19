import { useEffect, useState } from 'react'
import type { ServerInfo } from './generated/ServerInfo'
import { t } from './strings'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'failed' }
  | { kind: 'ready'; info: ServerInfo }

function databaseLabel(status: ServerInfo['database']): string {
  switch (status) {
    case 'ok':
      return t.dbOk
    case 'migrations_pending':
      return t.dbMigrating
    case 'unavailable':
      return t.dbDown
    default: {
      // A new DatabaseStatus value in the contract makes this assignment a compile error.
      const unhandled: never = status
      return unhandled
    }
  }
}

export default function App() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    fetch('/api/server-info', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`)
        }
        return response.json() as Promise<ServerInfo>
      })
      .then((info) => setState({ kind: 'ready', info }))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        setState({ kind: 'failed' })
      })
    return () => controller.abort()
  }, [])

  if (state.kind === 'loading') {
    return <p>{t.loading}</p>
  }
  if (state.kind === 'failed') {
    return <p role="alert">{t.loadFailed}</p>
  }

  const { info } = state
  return (
    <main>
      <h1>{info.appName || t.appName}</h1>
      <dl>
        <dt>{t.serverVersion}</dt>
        <dd>{info.serverVersion}</dd>
        <dt>{t.protocolVersion}</dt>
        <dd>{info.protocolVersion}</dd>
        <dt>{t.certFingerprint}</dt>
        <dd>
          <code>{info.certSha256}</code>
          <p>{t.fingerprintHint}</p>
        </dd>
        <dt>{t.database}</dt>
        <dd>{databaseLabel(info.database)}</dd>
      </dl>
      {info.defaultDbPassword && <div role="alert">{t.defaultDbPasswordWarning}</div>}
      <section>
        <h2>{t.dashboardTitle}</h2>
        <p>{t.dashboardEmpty}</p>
      </section>
    </main>
  )
}
