import { useEffect, useState } from 'react'
import { api } from '../api'
import type { AppSettings, SettingsResponse } from '../types'
import type { Shared } from './Shell'

const RETENTION_CHOICES = [7, 14, 30, 60, 90]

export default function SettingsTab({ shared, onSaved }: { shared: Shared; onSaved: (s: AppSettings) => void }) {
  const { onError } = shared
  const [data, setData] = useState<SettingsResponse | null>(null)
  const [form, setForm] = useState<AppSettings | null>(null)
  const [newTime, setNewTime] = useState('12:00')
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    api
      .settings()
      .then((r) => {
        setData(r)
        setForm(r.settings)
      })
      .catch(onError)
  }, [onError])

  if (!form || !data) return <div className="center muted">Đang tải…</div>

  const set = (patch: Partial<AppSettings>) => {
    setForm({ ...form, ...patch })
    setSaved(false)
  }

  async function save() {
    if (!form) return
    try {
      const r = await api.saveSettings(form)
      setForm(r.settings)
      onSaved(r.settings)
      setSaved(true)
    } catch (err) {
      onError(err)
    }
  }

  const primaryAi = form.aiProviders[0] ?? 'cloudflare'
  const aiLabel = (p: string) =>
    `${p === 'cloudflare' ? 'Cloudflare Workers AI' : 'Gemini'}${
      data.aiConfigured[p as 'cloudflare' | 'gemini'] ? '' : ' (chưa có key)'
    }`

  return (
    <div className="card form settings">
      <h3>Cập nhật tin</h3>
      <p className="muted small">Tin chỉ được cập nhật khi bạn bấm “Cập nhật ngay” (tiết kiệm quota AI).</p>
      <label>
        Lưu tin trong
        <select value={form.retentionDays} onChange={(e) => set({ retentionDays: Number(e.target.value) })}>
          {RETENTION_CHOICES.map((d) => (
            <option key={d} value={d}>
              {d} ngày
            </option>
          ))}
        </select>
        <span className="muted small">Tin “Đọc sau” được giữ vĩnh viễn.</span>
      </label>

      <h3>Hiển thị</h3>
      <label>
        Chế độ đọc
        <select value={form.viewMode} onChange={(e) => set({ viewMode: e.target.value as AppSettings['viewMode'] })}>
          <option value="both">Cả danh sách tin hot và bản tin</option>
          <option value="list">Chỉ danh sách tin hot</option>
          <option value="digest">Chỉ bản tin</option>
        </select>
      </label>
      <label>
        Ngưỡng điểm mặc định
        <select value={form.defaultMinScore} onChange={(e) => set({ defaultMinScore: Number(e.target.value) })}>
          {[0, 5, 6, 7, 8, 9].map((s) => (
            <option key={s} value={s}>
              {s === 0 ? 'Mọi mức độ' : `Từ ${s} điểm`}
            </option>
          ))}
        </select>
      </label>
      <label className="check">
        <input type="checkbox" checked={form.hideRead} onChange={(e) => set({ hideRead: e.target.checked })} />
        Mặc định ẩn tin đã đọc
      </label>

      <h3>Bản tin tổng hợp</h3>
      <div>
        <span className="small">Giờ chốt bản tin (giờ Việt Nam):</span>
        <div className="row">
          {form.digestTimes.map((t) => (
            <span key={t} className="chip">
              {t}{' '}
              <button className="link" onClick={() => set({ digestTimes: form.digestTimes.filter((x) => x !== t) })}>
                ✕
              </button>
            </span>
          ))}
          <input type="time" value={newTime} onChange={(e) => setNewTime(e.target.value)} />
          <button
            onClick={() => newTime && set({ digestTimes: [...new Set([...form.digestTimes, newTime])].sort() })}
          >
            ＋ Thêm giờ
          </button>
        </div>
      </div>
      <label>
        Số sự kiện mỗi bản tin
        <input
          type="number"
          min={3}
          max={50}
          value={form.digestSize}
          onChange={(e) => set({ digestSize: Number(e.target.value) })}
        />
      </label>

      <h3>AI chấm điểm</h3>
      <label>
        Dùng trước
        <select
          value={primaryAi}
          onChange={(e) => set({ aiProviders: [e.target.value, e.target.value === 'cloudflare' ? 'gemini' : 'cloudflare'] })}
        >
          <option value="cloudflare">{aiLabel('cloudflare')}</option>
          <option value="gemini">{aiLabel('gemini')}</option>
        </select>
        <span className="muted small">Nếu lỗi hoặc hết quota sẽ tự chuyển sang {aiLabel(form.aiProviders[1] ?? 'gemini')}.</span>
      </label>

      <div className="row">
        <button className="primary" onClick={save}>
          Lưu cài đặt
        </button>
        {saved && <span className="muted small">✓ Đã lưu</span>}
      </div>
    </div>
  )
}
