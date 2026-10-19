import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on :5002 in development (dotnet run --project src/MatchShift.Api).
export default defineConfig(({ mode }) => ({
  base: mode === 'pages' ? '/MatchShift-Demo/' : '/',
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5002', changeOrigin: true },
      '/health': { target: 'http://localhost:5002', changeOrigin: true },
    },
  },
  build: {
    outDir: mode === 'pages' ? 'dist' : '../MatchShift.Api/wwwroot',
    emptyOutDir: true,
  },
}))
