export interface ArticleLink {
  title: string
  url: string
  sourceKey: string
  sourceName: string
  sourceLogo?: string | null
  publishedAt: string
  isInternational: boolean
  imageUrl?: string | null
}

export interface NewsCluster {
  id: number
  title: string
  imageUrl?: string | null
  category: string
  categoryLabel: string
  score: number
  rank: number
  aiReason?: string | null
  sourceCount: number
  articleCount: number
  trend: boolean
  isScored: boolean
  isRead: boolean
  hasUpdate: boolean
  isSaved: boolean
  firstSeenAt: string
  lastUpdatedAt: string
  articles: ArticleLink[]
}

export interface RefreshStatus {
  isRunning: boolean
  stage: string
  message?: string | null
  trigger?: string | null
  startedAt?: string | null
  finishedAt?: string | null
  lastError?: string | null
}

export interface Category {
  key: string
  label: string
}

export interface SourceMeta {
  key: string
  name: string
  isInternational: boolean
  enabled: boolean
  logo?: string | null
}

export interface SourceInfo extends SourceMeta {
  id: number
  feedUrl: string
  lastFetchedAt?: string | null
  lastItemCount: number
  lastError?: string | null
  articleCount: number
}

export interface AppSettings {
  retentionDays: number
  defaultMinScore: number
  aiProviders: string[]
  digestTimes: string[]
  digestSize: number
  viewMode: 'list' | 'digest' | 'both'
  hideRead: boolean
}

export interface SettingsResponse {
  settings: AppSettings
  aiConfigured: { cloudflare: boolean; gemini: boolean }
}

export interface DigestSummary {
  id: number
  localDate: string
  slot: string
  isManual: boolean
  periodStart: string
  createdAt: string
  itemCount: number
}

export interface DigestItem {
  rank: number
  title: string
  category: string
  categoryLabel: string
  score: number
  sourceCount: number
  url?: string | null
  cluster?: NewsCluster | null
}

export interface DigestDetail extends Omit<DigestSummary, 'itemCount'> {
  items: DigestItem[]
}

export interface NewsQuery {
  hours: number
  categories: string[]
  sources: string[]
  scope: 'all' | 'domestic' | 'international'
  minScore: number
  q: string
  view: 'all' | 'unread' | 'saved'
}
