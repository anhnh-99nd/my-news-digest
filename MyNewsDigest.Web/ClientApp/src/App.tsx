import { useEffect, useState } from 'react'
import { api } from './api'
import Login from './components/Login'
import Shell from './components/Shell'

export default function App() {
  // undefined = đang kiểm tra phiên, null = chưa đăng nhập
  const [user, setUser] = useState<string | null | undefined>(undefined)

  useEffect(() => {
    api.me().then((r) => setUser(r.username)).catch(() => setUser(null))
  }, [])

  if (user === undefined) return <div className="center muted">Đang tải…</div>
  if (user === null) return <Login onLoggedIn={setUser} />
  return (
    <Shell
      username={user}
      onLogout={async () => {
        await api.logout().catch(() => undefined)
        setUser(null)
      }}
      onUnauthorized={() => setUser(null)}
    />
  )
}
