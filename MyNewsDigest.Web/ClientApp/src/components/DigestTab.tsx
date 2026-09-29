import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import type { DigestDetail, DigestSummary, NewsCluster } from '../types'
import ClusterCard from './ClusterCard'
import type { Shared } from './Shell'

function digestLabel(d: { localDate: string; slot: string; isManual: boolean; createdAt: string }) {
  const [y, m, day] = d.localDate.split('-')
  const time = d.isManual
    ? new Date(d.createdAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })
    : d.slot
  return `${time} · ${day}/${m}/${y}${d.isManual ? ' (tạo tay)' : ''}`
}

export default function DigestTab({ shared }: { shared: Shared }) {
  const { settings, reloadKey, onError } = shared
  const [list, setList] = useState<DigestSummary[]>([])
  const [selected, setSelected] = useState<number | null>(null)
  const [detail, setDetail] = useState<DigestDetail | null>(null)
  const [creating, setCreating] = useState(false)

  const loadList = useCallback(async () => {
    try {
      const r = await api.digests()
      setList(r)
      setSelected((cur) => cur ?? r[0]?.id ?? null)
    } catch (err) {
      onError(err)
    }
  }, [onError])

  useEffect(() => {
    loadList()
  }, [loadList, reloadKey])

  useEffect(() => {
    if (selected === null) {
      setDetail(null)
      return
    }
    api.digest(selected).then(setDetail).catch(onError)
  }, [selected, reloadKey, onError])

  async function createNow() {
    setCreating(true)
    try {
      const r = await api.createDigest()
      setSelected(r.id)
      await loadList()
    } catch (err) {
      onError(err)
    } finally {
      setCreating(false)
    }
  }

  function updateCluster(updated: NewsCluster) {
    setDetail((d) =>
      d && { ...d, items: d.items.map((i) => (i.cluster?.id === updated.id ? { ...i, cluster: updated } : i)) },
    )
  }

  return (
    <>
      <div className="filters">
        <select value={selected ?? ''} onChange={(e) => setSelected(Number(e.target.value))} disabled={!list.length}>
          {list.length === 0 && <option value="">Chưa có bản tin</option>}
          {list.map((d) => (
            <option key={d.id} value={d.id}>
              {digestLabel(d)} — {d.itemCount} tin
            </option>
          ))}
        </select>
        <button onClick={createNow} disabled={creating}>
          {creating ? 'Đang tạo…' : '＋ Tạo bản tin ngay'}
        </button>
      </div>
      <p className="muted small">
        Khung giờ bản tin: {settings?.digestTimes.length ? settings.digestTimes.join(', ') : '(chưa đặt giờ)'}. Bản tin (top{' '}
        {settings?.digestSize ?? 15} sự kiện hot nhất kể từ bản tin trước) được chốt ở lần bấm “Cập nhật ngay” đầu tiên
        sau mỗi khung giờ. Đổi giờ trong tab Cài đặt.
      </p>

      {detail && (
        <main className="list">
          {detail.items.map((item) =>
            item.cluster ? (
              <ClusterCard
                key={item.rank}
                cluster={item.cluster}
                index={item.rank}
                featured={item.rank === 1}
                onChange={updateCluster}
                onError={onError}
              />
            ) : (
              // Sự kiện đã bị xóa theo hạn lưu: hiện bản chụp lúc chốt
              <article key={item.rank} className="card">
                <div className="card-meta">
                  <span className="rank">#{item.rank}</span>
                  <span className="score">{item.score.toFixed(1)}</span>
                  <span className="chip">{item.categoryLabel}</span>
                  <span className="chip">{item.sourceCount} báo</span>
                </div>
                <h2 className="card-title">
                  {item.url ? (
                    <a href={item.url} target="_blank" rel="noopener noreferrer">
                      {item.title}
                    </a>
                  ) : (
                    item.title
                  )}
                </h2>
              </article>
            ),
          )}
        </main>
      )}
    </>
  )
}
