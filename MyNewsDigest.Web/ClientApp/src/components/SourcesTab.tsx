import { useCallback, useEffect, useState } from 'react'
import { api, type SourceInput } from '../api'
import type { SourceInfo } from '../types'
import { timeAgo } from '../time'
import type { Shared } from './Shell'
import { SourceLogo } from './Brand'

const EMPTY: SourceInput = { name: '', feedUrl: '', isInternational: false, enabled: true }

export default function SourcesTab({ shared, onChanged }: { shared: Shared; onChanged: () => void }) {
  const { onError } = shared
  const [sources, setSources] = useState<SourceInfo[]>([])
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const [form, setForm] = useState<SourceInput>(EMPTY)
  const [testResult, setTestResult] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => api.sources().then(setSources).catch(onError), [onError])

  useEffect(() => {
    load()
  }, [load, shared.reloadKey])

  function startEdit(s: SourceInfo | null) {
    setEditing(s ? s.id : 'new')
    setForm(s ? { name: s.name, feedUrl: s.feedUrl, isInternational: s.isInternational, enabled: s.enabled } : EMPTY)
    setTestResult(null)
  }

  async function test() {
    setTestResult('Đang thử…')
    try {
      const r = await api.testSource(form.feedUrl)
      setTestResult(
        r.ok
          ? `✓ Đọc được ${r.count} bài, bài mới nhất ${r.newest ? timeAgo(r.newest) : '(không rõ ngày)'}. Ví dụ: ${r.samples?.slice(0, 2).join(' | ')}${r.error ? ` — Lỗi một số link: ${r.error}` : ''}`
          : `✗ Không đọc được${r.error ? `: ${r.error}` : ' (0 bài)'}`,
      )
    } catch (err) {
      setTestResult(`✗ ${err instanceof Error ? err.message : err}`)
    }
  }

  async function save() {
    setBusy(true)
    try {
      if (editing === 'new') await api.createSource(form)
      else if (editing !== null) await api.updateSource(editing, form)
      setEditing(null)
      await load()
      onChanged()
    } catch (err) {
      onError(err)
    } finally {
      setBusy(false)
    }
  }

  async function toggleEnabled(s: SourceInfo) {
    try {
      await api.updateSource(s.id, { name: s.name, feedUrl: s.feedUrl, isInternational: s.isInternational, enabled: !s.enabled })
      await load()
      onChanged()
    } catch (err) {
      onError(err)
    }
  }

  async function remove(s: SourceInfo) {
    if (!window.confirm(`Xóa nguồn "${s.name}" và ${s.articleCount} bài của nguồn này?`)) return
    try {
      await api.deleteSource(s.id)
      await load()
      onChanged()
    } catch (err) {
      onError(err)
    }
  }

  const formView = (
    <div className="card form">
      <label>
        Tên báo
        <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
      </label>
      <label>
        Link RSS (có thể nhiều link, mỗi link một dòng)
        <textarea
          rows={3}
          value={form.feedUrl}
          onChange={(e) => setForm({ ...form, feedUrl: e.target.value })}
          placeholder="https://…/rss"
        />
      </label>
      <label className="check">
        <input
          type="checkbox"
          checked={form.isInternational}
          onChange={(e) => setForm({ ...form, isInternational: e.target.checked })}
        />
        Báo quốc tế
      </label>
      <label className="check">
        <input type="checkbox" checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
        Đang bật
      </label>
      {testResult && <p className="small">{testResult}</p>}
      <div className="row">
        <button onClick={test} disabled={!form.feedUrl}>
          Thử link
        </button>
        <button className="primary" onClick={save} disabled={busy || !form.name}>
          Lưu
        </button>
        <button className="ghost" onClick={() => setEditing(null)}>
          Hủy
        </button>
      </div>
    </div>
  )

  return (
    <>
      <div className="list-header">
        <span className="muted small">
          {sources.filter((s) => s.enabled).length}/{sources.length} nguồn đang bật
        </span>
        <button onClick={() => startEdit(null)}>＋ Thêm nguồn</button>
      </div>
      {editing === 'new' && formView}

      <main className="list">
        {sources.map((s) =>
          editing === s.id ? (
            <div key={s.id}>{formView}</div>
          ) : (
            <article key={s.id} className={s.enabled ? 'card' : 'card read'}>
              <div className="card-meta">
                <SourceLogo name={s.name} logo={s.logo} size={24} />
                <strong>{s.name}</strong>
                <span className="chip">{s.isInternational ? 'Quốc tế' : 'Trong nước'}</span>
                <span className="chip">{s.enabled ? 'Đang bật' : 'Đã tắt'}</span>
                <span className="muted time">{s.articleCount} bài trong DB</span>
              </div>
              <p className="muted small break pre">{s.feedUrl || '(chưa có link RSS)'}</p>
              <p className="small">
                {s.lastError ? (
                  <span className="text-error">✗ Lỗi: {s.lastError}</span>
                ) : s.lastFetchedAt ? (
                  <>
                    ✓ Lần tải cuối {timeAgo(s.lastFetchedAt)}: {s.lastItemCount} bài
                    {s.enabled && s.lastItemCount === 0 && <span className="text-error"> (không đọc được bài nào)</span>}
                  </>
                ) : (
                  <span className="muted">Chưa tải lần nào</span>
                )}
              </p>
              <div className="card-actions">
                <button className="ghost small" onClick={() => toggleEnabled(s)} disabled={!s.feedUrl && !s.enabled}>
                  {s.enabled ? 'Tắt' : 'Bật'}
                </button>
                <button className="ghost small" onClick={() => startEdit(s)}>
                  Sửa
                </button>
                <button className="ghost small" onClick={() => remove(s)}>
                  Xóa
                </button>
              </div>
            </article>
          ),
        )}
      </main>
    </>
  )
}
