import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Trang HTML do ASP.NET MVC (_Layout.cshtml) phục vụ; Vite chỉ build JS/CSS ra wwwroot/app
export default defineConfig({
  plugins: [react()],
  base: '/app/',
  build: {
    outDir: '../wwwroot/app',
    emptyOutDir: true,
    manifest: 'manifest.json',
    rollupOptions: { input: 'src/main.tsx' },
  },
  server: {
    port: 5173,
    strictPort: true,
    origin: 'http://localhost:5173',
    cors: true,
  },
})
