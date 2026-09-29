import { useState } from 'react'

/** Logo app: tờ báo trên nền xanh lá + tia sét vàng (tin nóng). Giống wwwroot/favicon.svg. */
export function LogoMark({ size = 36 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 64 64" aria-hidden="true">
      <defs>
        <linearGradient id="logo-g" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#2fd46b" />
          <stop offset="1" stopColor="#138a3e" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width="60" height="60" rx="16" fill="url(#logo-g)" />
      <rect x="13" y="17" width="31" height="34" rx="6" fill="#0a130e" />
      <rect x="18.5" y="23" width="13" height="4.5" rx="2.25" fill="#facc15" />
      <rect x="18.5" y="31" width="20" height="3" rx="1.5" fill="#e7f3eb" />
      <rect x="18.5" y="37" width="20" height="3" rx="1.5" fill="#e7f3eb" opacity=".7" />
      <rect x="18.5" y="43" width="12" height="3" rx="1.5" fill="#e7f3eb" opacity=".45" />
      <circle cx="46" cy="18" r="11" fill="#facc15" stroke="#0a130e" strokeWidth="2.5" />
      <path d="M47.6 10.5 40.8 19.6h4.9l-1.6 6.4 6.9-9.2h-4.9z" fill="#0a130e" />
    </svg>
  )
}

export function Brand({ large = false }: { large?: boolean }) {
  return (
    <div className={large ? 'brand brand-large' : 'brand'}>
      <LogoMark size={large ? 56 : 38} />
      <div className="brand-text">
        <span className="brand-name">
          Tổng hợp <em>tin tức</em>
        </span>
        <span className="brand-owner">Nguyễn Hải Anh</span>
      </div>
    </div>
  )
}

/** Logo báo (favicon). Lỗi tải ảnh thì hiện chữ cái đầu. Tên báo nằm ở tooltip. */
export function SourceLogo({ name, logo, size = 20 }: { name: string; logo?: string | null; size?: number }) {
  const [failed, setFailed] = useState(false)
  if (!logo || failed)
    return (
      <span className="source-logo fallback" style={{ width: size, height: size, fontSize: size * 0.5 }} title={name}>
        {name.charAt(0)}
      </span>
    )
  return (
    <img
      className="source-logo"
      src={logo}
      alt={name}
      title={name}
      width={size}
      height={size}
      loading="lazy"
      referrerPolicy="no-referrer"
      onError={() => setFailed(true)}
    />
  )
}
