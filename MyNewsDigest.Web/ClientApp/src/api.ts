import type {
  AppSettings,
  Category,
  DigestDetail,
  DigestSummary,
  NewsCluster,
  NewsQuery,
  RefreshStatus,
  SettingsResponse,
  SourceInfo,
  SourceMeta,
} from './types'

export class UnauthorizedError extends Error {}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    credentials: 'same-origin',
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (res.status === 401) throw new UnauthorizedError('Chưa đăng nhập')
  if (!res.ok) {
    let message = `Lỗi ${res.status}`
    try {
      const body = await res.json()
      message = body.message ?? body.detail ?? body.title ?? message
    } catch {
      /* không phải JSON */
    }
    throw new Error(message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
const put = <T>(path: string, body: unknown) => request<T>(path, { method: 'PUT', body: JSON.stringify(body) })
const del = <T>(path: string) => request<T>(path, { method: 'DELETE' })

export interface SourceInput {
  name: string
  feedUrl: string
  isInternational: boolean
  enabled: boolean
}

export const api = {
  me: () => request<{ username: string }>('/api/auth/me'),
  login: (username: string, password: string) => post<{ username: string }>('/api/auth/login', { username, password }),
  logout: () => post<void>('/api/auth/logout'),

  news: (query: NewsQuery) => {
    const q = new URLSearchParams({ hours: String(query.hours), view: query.view, scope: query.scope })
    if (query.categories.length) q.set('categories', query.categories.join(','))
    if (query.sources.length) q.set('sources', query.sources.join(','))
    if (query.minScore) q.set('minScore', String(query.minScore))
    if (query.q.trim()) q.set('q', query.q.trim())
    return request<{ generatedAt: string; total: number; items: NewsCluster[] }>(`/api/news?${q}`)
  },
  meta: () => request<{ categories: Category[]; sources: SourceMeta[] }>('/api/news/meta'),

  markRead: (id: number) => post<unknown>(`/api/clusters/${id}/read`),
  markUnread: (id: number) => del<unknown>(`/api/clusters/${id}/read`),
  markManyRead: (ids: number[]) => post<unknown>('/api/clusters/read', { ids }),
  save: (id: number) => post<unknown>(`/api/clusters/${id}/save`),
  unsave: (id: number) => del<unknown>(`/api/clusters/${id}/save`),

  startRefresh: () => post<RefreshStatus>('/api/refresh'),
  refreshStatus: () => request<RefreshStatus>('/api/refresh/status'),

  sources: () => request<SourceInfo[]>('/api/sources'),
  createSource: (s: SourceInput) => post<SourceInfo>('/api/sources', s),
  updateSource: (id: number, s: SourceInput) => put<SourceInfo>(`/api/sources/${id}`, s),
  deleteSource: (id: number) => del<void>(`/api/sources/${id}`),
  testSource: (feedUrl: string) =>
    post<{ ok: boolean; count: number; newest?: string | null; samples?: string[]; error?: string | null }>('/api/sources/test', { feedUrl }),

  settings: () => request<SettingsResponse>('/api/settings'),
  saveSettings: (s: AppSettings) => put<SettingsResponse>('/api/settings', s),

  digests: () => request<DigestSummary[]>('/api/digests'),
  digest: (id: number) => request<DigestDetail>(`/api/digests/${id}`),
  createDigest: () => post<{ id: number }>('/api/digests'),
}
