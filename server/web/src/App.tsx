import { useEffect, useState } from 'react'
import type { ServerInfo } from './generated/ServerInfo'
import { t } from './strings'

type LoadState =
  | { kind: 'loading' }
  | { kind: 'failed' }
  | { kind: 'ready'; info: ServerInfo }

function databaseStatus(status: ServerInfo['database']): { label: string; modifier: string } {
  switch (status) {
    case 'ok':
      return { label: t.dbOk, modifier: 'ok' }
    case 'migrations_pending':
      return { label: t.dbMigrating, modifier: 'migrating' }
    case 'unavailable':
      return { label: t.dbDown, modifier: 'down' }
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
    return (
      <main className="page">
        <p role="status">{t.loading}</p>
      </main>
    )
  }
  if (state.kind === 'failed') {
    return (
      <main className="page">
        <h1 className="app-name">{t.appName}</h1>
        <div role="alert" className="banner banner--error">
          {t.loadFailed}
        </div>
      </main>
    )
  }

  const { info } = state
  const db = databaseStatus(info.database)
  return (
    <main className="page">
      <h1 className="app-name">{info.appName || t.appName}</h1>
      {info.defaultDbPassword && (
        <div role="alert" className="banner banner--warning">
          {t.defaultDbPasswordWarning}
        </div>
      )}
      <dl className="panel info">
        <dt>{t.serverVersion}</dt>
        <dd>{info.serverVersion}</dd>
        <dt>{t.protocolVersion}</dt>
        <dd>{info.protocolVersion}</dd>
        <dt>{t.certFingerprint}</dt>
        <dd>
          <code className="fingerprint">{info.certSha256}</code>
          <p className="hint">{t.fingerprintHint}</p>
        </dd>
        <dt>{t.database}</dt>
        <dd>
          <span className={`status status--${db.modifier}`}>{db.label}</span>
          {info.database === 'unavailable' && (
            <p className="hint">
              {t.dbDownHint} <code>{t.dbDownCommand}</code>
            </p>
          )}
        </dd>
      </dl>
      <section className="dashboard">
        <h2 className="section-title">{t.dashboardTitle}</h2>
        <p className="panel placeholder">{t.dashboardEmpty}</p>
      </section>
    </main>
  )
}
