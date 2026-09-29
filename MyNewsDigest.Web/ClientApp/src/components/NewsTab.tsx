import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api } from '../api'
import type { NewsCluster, NewsQuery } from '../types'
import ClusterCard from './ClusterCard'
import type { Shared } from './Shell'
import { SourceLogo } from './Brand'

const HOUR_OPTIONS = [6, 12, 24, 48, 168]
const SCORE_OPTIONS = [0, 5, 6, 7, 8, 9]
const FILTER_KEY = 'mnd.filters'

function loadFilters(defaultMinScore: number, hideRead: boolean): NewsQuery {
  const base: NewsQuery = {
    hours: 24,
    categories: [],
    sources: [],
    scope: 'all',
    minScore: defaultMinScore,
    q: '',
    view: hideRead ? 'unread' : 'all',
  }
  try {
    const saved = JSON.parse(localStorage.getItem(FILTER_KEY) ?? 'null')
    return saved ? { ...base, ...saved, q: '' } : base
  } catch {
    return base
  }
}

function toggle(list: string[], value: string) {
  return list.includes(value) ? list.filter((x) => x !== value) : [...list, value]
}

export default function NewsTab({ view, shared }: { view: 'all' | 'saved'; shared: Shared }) {
  const { categories, sources, settings, reloadKey, onError } = shared
  const [filters, setFilters] = useState<NewsQuery>(() =>
    loadFilters(settings?.defaultMinScore ?? 0, settings?.hideRead ?? false),
  )
  const [search, setSearch] = useState('')
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [items, setItems] = useState<NewsCluster[]>([])
  const [loading, setLoading] = useState(true)

  // Lưu bộ lọc (trừ từ khóa) cho lần mở sau
  useEffect(() => {
    try {
      const { q: _q, ...rest } = filters
      localStorage.setItem(FILTER_KEY, JSON.stringify(rest))
    } catch {
      /* bỏ qua */
    }
  }, [filters])

  // Gõ từ khóa: đợi 400ms mới tìm
  useEffect(() => {
    const t = window.setTimeout(() => setFilters((f) => ({ ...f, q: search })), 400)
    return () => window.clearTimeout(t)
  }, [search])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const query: NewsQuery = view === 'saved' ? { ...filters, view: 'saved' } : filters
      const r = await api.news(query)
      setItems(r.items)
    } catch (err) {
      onError(err)
    } finally {
      setLoading(false)
    }
  }, [filters, view, onError])

  useEffect(() => {
    load()
  }, [load, reloadKey])

  const set = (patch: Partial<NewsQuery>) => setFilters((f) => ({ ...f, ...patch }))

  function updateItem(updated: NewsCluster) {
    setItems((list) =>
      list
        .map((c) => (c.id === updated.id ? updated : c))
        // Đang xem "Đã lưu" mà bỏ lưu → ẩn luôn
        .filter((c) => view !== 'saved' || c.isSaved),
    )
    // Đang bật "Ẩn tin đã đọc": tin vừa đọc mờ đi rồi biến mất
    if (view !== 'saved' && filters.view === 'unread' && updated.isRead)
      window.setTimeout(() => setItems((list) => list.filter((c) => c.id !== updated.id || !c.isRead)), 700)
  }

  function submitSearch(e: FormEvent) {
    e.preventDefault()
    set({ q: search })
  }

  async function markAllRead() {
    const ids = items.filter((c) => !c.isRead).map((c) => c.id)
    if (!ids.length) return
    try {
      await api.markManyRead(ids)
      load()
    } catch (err) {
      onError(err)
    }
  }

  const activeAdvanced = filters.sources.length + (filters.scope !== 'all' ? 1 : 0)

  return (
    <>
      <form className="searchbar" onSubmit={submitSearch}>
        <input
          className="search"
          type="search"
          placeholder="Tìm tin theo từ khóa, không cần gõ dấu (vd: bao lu, iran)…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        {search && (
          <button type="button" className="ghost" onClick={() => setSearch('')} title="Xóa từ khóa">
            ✕
          </button>
        )}
        <button type="submit" className="primary">
          Tìm
        </button>
      </form>

      <div className="filters">
        {view !== 'saved' && (
          <select value={filters.hours} onChange={(e) => set({ hours: Number(e.target.value) })}>
            {HOUR_OPTIONS.map((h) => (
              <option key={h} value={h}>
                {h < 168 ? `${h} giờ qua` : '7 ngày qua'}
              </option>
            ))}
          </select>
        )}
        <select value={filters.minScore} onChange={(e) => set({ minScore: Number(e.target.value) })}>
          {SCORE_OPTIONS.map((s) => (
            <option key={s} value={s}>
              {s === 0 ? 'Mọi mức độ' : `Từ ${s} điểm`}
            </option>
          ))}
        </select>
        {view !== 'saved' && (
          <label className="check">
            <input
              type="checkbox"
              checked={filters.view === 'unread'}
              onChange={(e) => set({ view: e.target.checked ? 'unread' : 'all' })}
            />
            Ẩn tin đã đọc
          </label>
        )}
        <button className="ghost" onClick={() => setShowAdvanced((v) => !v)}>
          Lọc nâng cao{activeAdvanced ? ` (${activeAdvanced})` : ''} {showAdvanced ? '▴' : '▾'}
        </button>
      </div>

      <div className="chips">
        <button className={filters.categories.length === 0 ? 'chip-btn on' : 'chip-btn'} onClick={() => set({ categories: [] })}>
          Tất cả
        </button>
        {categories.map((c) => (
          <button
            key={c.key}
            className={filters.categories.includes(c.key) ? 'chip-btn on' : 'chip-btn'}
            onClick={() => set({ categories: toggle(filters.categories, c.key) })}
          >
            {c.label}
          </button>
        ))}
      </div>

      {showAdvanced && (
        <div className="advanced">
          <div className="row">
            <span className="muted small">Phạm vi:</span>
            {(
              [
                ['all', 'Tất cả'],
                ['domestic', 'Tin trong nước'],
                ['international', 'Tin quốc tế'],
              ] as const
            ).map(([value, label]) => (
              <button
                key={value}
                className={filters.scope === value ? 'chip-btn on' : 'chip-btn'}
                onClick={() => set({ scope: value })}
              >
                {label}
              </button>
            ))}
          </div>
          <div className="row">
            <span className="muted small">Báo:</span>
            {sources
              .filter((s) => s.enabled)
              .map((s) => (
                <button
                  key={s.key}
                  className={filters.sources.includes(s.key) ? 'chip-btn on' : 'chip-btn'}
                  onClick={() => set({ sources: toggle(filters.sources, s.key) })}
                >
                  <SourceLogo name={s.name} logo={s.logo} size={16} />
                  {s.name}
                </button>
              ))}
            {filters.sources.length > 0 && (
              <button className="chip-btn" onClick={() => set({ sources: [] })}>
                ✕ Bỏ chọn
              </button>
            )}
          </div>
        </div>
      )}

      <div className="list-header">
        <span className="muted small">
          {loading ? 'Đang tải…' : `${items.length} sự kiện`}
          {filters.q && !loading && <> cho “{filters.q}”</>}
        </span>
        {view !== 'saved' && items.some((c) => !c.isRead) && (
          <button className="ghost small" onClick={markAllRead}>
            ✓ Đánh dấu đã đọc tất cả
          </button>
        )}
      </div>

      {!loading && items.length === 0 ? (
        <div className="empty">
          <p>{view === 'saved' ? 'Chưa lưu tin nào.' : 'Không có tin nào khớp bộ lọc.'}</p>
        </div>
      ) : (
        <main className="list">
          {items.map((c, i) => (
            <ClusterCard
              key={c.id}
              cluster={c}
              index={i + 1}
              featured={i === 0 && view !== 'saved'}
              onChange={updateItem}
              onError={onError}
            />
          ))}
        </main>
      )}
    </>
  )
}
