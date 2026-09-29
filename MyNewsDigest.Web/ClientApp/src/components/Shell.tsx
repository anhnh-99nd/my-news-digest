import { useCallback, useEffect, useRef, useState } from 'react'
import { api, UnauthorizedError } from '../api'
import type { AppSettings, Category, RefreshStatus, SourceMeta } from '../types'
import { timeAgo } from '../time'
import NewsTab from './NewsTab'
import DigestTab from './DigestTab'
import SourcesTab from './SourcesTab'
import SettingsTab from './SettingsTab'
import { Brand } from './Brand'

type Tab = 'news' | 'digest' | 'saved' | 'sources' | 'settings'

interface Props {
  username: string
  onLogout: () => void
  onUnauthorized: () => void
}

export interface Shared {
  categories: Category[]
  sources: SourceMeta[]
  settings: AppSettings | null
  /** Tăng lên mỗi khi dữ liệu tin thay đổi (cập nhật xong, đổi nguồn…) để các tab tải lại. */
  reloadKey: number
  onError: (err: unknown) => void
}

function readTab(): Tab {
  const t = window.location.hash.replace('#', '')
  return (['news', 'digest', 'saved', 'sources', 'settings'] as const).find((x) => x === t) ?? 'news'
}

export default function Shell({ username, onLogout, onUnauthorized }: Props) {
  const [tab, setTab] = useState<Tab>(readTab)
  const [categories, setCategories] = useState<Category[]>([])
  const [sources, setSources] = useState<SourceMeta[]>([])
  const [settings, setSettings] = useState<AppSettings | null>(null)
  const [status, setStatus] = useState<RefreshStatus | null>(null)
  const [reloadKey, setReloadKey] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const pollRef = useRef<number | null>(null)

  const onError = useCallback(
    (err: unknown) => {
      if (err instanceof UnauthorizedError) onUnauthorized()
      else setError(err instanceof Error ? err.message : String(err))
    },
    [onUnauthorized],
  )

  const loadMeta = useCallback(() => {
    api
      .meta()
      .then((m) => {
        setCategories(m.categories)
        setSources(m.sources)
      })
      .catch(onError)
    api
      .settings()
      .then((r) => setSettings(r.settings))
      .catch(onError)
  }, [onError])

  const stopPolling = () => {
    if (pollRef.current !== null) window.clearInterval(pollRef.current)
    pollRef.current = null
  }

  const startPolling = useCallback(() => {
    if (pollRef.current !== null) return
    pollRef.current = window.setInterval(async () => {
      try {
        const s = await api.refreshStatus()
        setStatus(s)
        if (!s.isRunning) {
          stopPolling()
          setReloadKey((k) => k + 1)
        }
      } catch (err) {
        stopPolling()
        onError(err)
      }
    }, 2000)
  }, [onError])

  useEffect(() => {
    loadMeta()
    api
      .refreshStatus()
      .then((s) => {
        setStatus(s)
        if (s.isRunning) startPolling()
      })
      .catch(onError)
    return stopPolling
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const onHash = () => setTab(readTab())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  async function refreshNow() {
    try {
      setStatus(await api.startRefresh())
      startPolling()
    } catch (err) {
      onError(err)
    }
  }

  const running = status?.isRunning ?? false
  const viewMode = settings?.viewMode ?? 'both'
  const tabs: [Tab, string][] = [
    ...(viewMode !== 'digest' ? ([['news', 'Tin hot']] as [Tab, string][]) : []),
    ...(viewMode !== 'list' ? ([['digest', 'Bản tin']] as [Tab, string][]) : []),
    ['saved', 'Đã lưu'],
    ['sources', 'Nguồn tin'],
    ['settings', 'Cài đặt'],
  ]
  const activeTab = tabs.some(([t]) => t === tab) ? tab : tabs[0][0]

  const shared: Shared = { categories, sources, settings, reloadKey, onError }

  return (
    <div className="page">
      <header className="topbar">
        <a href="#news" className="brand-link">
          <Brand />
        </a>
        <div className="topbar-actions">
          <button className="primary" onClick={refreshNow} disabled={running}>
            {running ? <span className="spinner" /> : '↻'}
            <span className="hide-sm">{running ? 'Đang cập nhật…' : 'Cập nhật ngay'}</span>
          </button>
          <button className="ghost" onClick={onLogout} title={`Đăng xuất ${username}`}>
            <span className="hide-sm">Đăng xuất</span>
            <span className="show-sm">⎋</span>
          </button>
        </div>
      </header>

      <nav className="tabs">
        {tabs.map(([t, label]) => (
          <a key={t} href={`#${t}`} className={t === activeTab ? 'tab active' : 'tab'}>
            {label}
          </a>
        ))}
      </nav>

      {status && (running || status.stage === 'error' || status.finishedAt) && (
        <div className={`status ${status.stage === 'error' ? 'status-error' : ''}`}>
          {running && <span className="spinner" />}
          <span>{status.message}</span>
          {!running && status.finishedAt && <span className="muted"> · {timeAgo(status.finishedAt)}</span>}
        </div>
      )}

      {error && (
        <div className="error" onClick={() => setError(null)}>
          {error} <span className="muted">(bấm để ẩn)</span>
        </div>
      )}

      {activeTab === 'news' && <NewsTab key="news" view="all" shared={shared} />}
      {activeTab === 'saved' && <NewsTab key="saved" view="saved" shared={shared} />}
      {activeTab === 'digest' && <DigestTab shared={shared} />}
      {activeTab === 'sources' && (
        <SourcesTab
          shared={shared}
          onChanged={() => {
            loadMeta()
            setReloadKey((k) => k + 1)
          }}
        />
      )}
      {activeTab === 'settings' && <SettingsTab shared={shared} onSaved={(s) => setSettings(s)} />}
    </div>
  )
}
