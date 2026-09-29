import { useState } from 'react'
import { api } from '../api'
import type { NewsCluster } from '../types'
import { timeAgo } from '../time'
import { LogoMark, SourceLogo } from './Brand'

const VISIBLE_ARTICLES = 3

function scoreClass(score: number) {
  if (score >= 8) return 'score score-hot'
  if (score >= 6) return 'score score-warm'
  return 'score'
}

interface Props {
  cluster: NewsCluster
  index: number
  /** Thẻ lớn (ảnh to phía trên) cho tin đứng đầu. */
  featured?: boolean
  onChange?: (updated: NewsCluster) => void
  onError?: (err: unknown) => void
}

/** Ảnh sự kiện; không có ảnh hoặc ảnh lỗi thì hiện khung nền kèm logo app. */
function Thumb({ src, category }: { src?: string | null; category: string }) {
  const [failed, setFailed] = useState(false)
  if (!src || failed)
    return (
      <div className={`thumb thumb-empty cat-${category}`}>
        <LogoMark size={40} />
      </div>
    )
  return (
    <div className="thumb">
      <img src={src} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} />
    </div>
  )
}

export default function ClusterCard({ cluster, index, featured = false, onChange, onError }: Props) {
  const [showAll, setShowAll] = useState(false)
  // Bài mới nhất lên đầu
  const articles = [...cluster.articles].reverse()
  const visible = showAll ? articles : articles.slice(0, VISIBLE_ARTICLES)
  const hidden = articles.length - VISIBLE_ARTICLES
  // Mỗi báo một logo (một báo có thể có nhiều bài trong cùng sự kiện)
  const outlets = [...new Map(cluster.articles.map((a) => [a.sourceKey, a])).values()]

  async function run(action: () => Promise<unknown>, patch: Partial<NewsCluster>) {
    onChange?.({ ...cluster, ...patch })
    try {
      await action()
    } catch (err) {
      onChange?.(cluster) // hoàn tác
      onError?.(err)
    }
  }

  const markRead = () => {
    if (!cluster.isRead) run(() => api.markRead(cluster.id), { isRead: true, hasUpdate: false })
  }
  const toggleRead = () =>
    cluster.isRead ? run(() => api.markUnread(cluster.id), { isRead: false, hasUpdate: false }) : markRead()
  const toggleSave = () =>
    cluster.isSaved
      ? run(() => api.unsave(cluster.id), { isSaved: false })
      : run(() => api.save(cluster.id), { isSaved: true })

  return (
    <article className={['card', featured && 'featured', cluster.isRead && 'read'].filter(Boolean).join(' ')}>
      <div className="card-main">
        <a
          className="thumb-link"
          href={articles[0]?.url}
          target="_blank"
          rel="noopener noreferrer"
          onClick={markRead}
          tabIndex={-1}
        >
          <Thumb src={cluster.imageUrl} category={cluster.category} />
          <span className="rank-badge">#{index}</span>
        </a>

        <div className="card-body">
          <div className="card-meta">
            <span className={scoreClass(cluster.score)} title={cluster.aiReason ?? 'Điểm độ hot'}>
              {cluster.score >= 8 ? '🔥 ' : ''}
              {cluster.score.toFixed(1)}
            </span>
            <span className="chip">{cluster.categoryLabel}</span>
            {cluster.trend && <span className="chip chip-trend">📈 Trending</span>}
            {cluster.hasUpdate && <span className="chip chip-update">🔄 Có cập nhật</span>}
            <span className="time">{timeAgo(cluster.lastUpdatedAt)}</span>
          </div>

          <h2 className="card-title">
            <a href={articles[0]?.url} target="_blank" rel="noopener noreferrer" onClick={markRead}>
              {cluster.title}
            </a>
          </h2>

          <div className="outlets">
            <div className="outlet-logos">
              {outlets.slice(0, 8).map((a) => (
                <SourceLogo key={a.sourceKey} name={a.sourceName} logo={a.sourceLogo} size={22} />
              ))}
            </div>
            <span className="outlet-count">{cluster.sourceCount} báo đưa tin</span>
          </div>
        </div>
      </div>

      {articles.length > 1 && (
        <ul className="articles">
          {visible.map((a) => (
            <li key={a.url}>
              <a href={a.url} target="_blank" rel="noopener noreferrer" onClick={markRead}>
                <SourceLogo name={a.sourceName} logo={a.sourceLogo} size={18} />
                <span className="article-title">{a.title}</span>
                <span className="article-time">{timeAgo(a.publishedAt)}</span>
              </a>
            </li>
          ))}
        </ul>
      )}

      <div className="card-actions">
        {articles.length > 1 && !showAll && hidden > 0 && (
          <button className="btn-text" onClick={() => setShowAll(true)}>
            + {hidden} bài khác
          </button>
        )}
        {onChange && (
          <div className="card-buttons">
            <button className={cluster.isSaved ? 'icon-btn on' : 'icon-btn'} onClick={toggleSave} title="Đọc sau">
              {cluster.isSaved ? '★ Đã lưu' : '☆ Đọc sau'}
            </button>
            <button className="icon-btn" onClick={toggleRead}>
              {cluster.isRead ? '↺ Chưa đọc' : '✓ Đã đọc'}
            </button>
          </div>
        )}
      </div>
    </article>
  )
}
