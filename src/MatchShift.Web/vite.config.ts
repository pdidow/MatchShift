import react from '@vitejs/plugin-react'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'

// A short content hash per crowd clip, baked into the app: the clip URL changes whenever the file changes, so no browser
// can keep playing a cached older clip after a redeploy (public/ files are not fingerprinted by Vite).
const audioDir = fileURLToPath(new URL('./public/audio/', import.meta.url))
const crowdAudioVersions = Object.fromEntries(['crowd-goal.wav', 'crowd-chance.wav', 'crowd-murmur.wav']
  .map(file => [file, createHash('sha256').update(readFileSync(audioDir + file)).digest('hex').slice(0, 12)]))

// The API runs on :5002 in development (dotnet run --project src/MatchShift.Api).
export default defineConfig(({ mode }) => ({
  base: mode === 'pages' ? '/MatchShift/' : '/',
  plugins: [react()],
  define: { __CROWD_AUDIO_VERSIONS__: JSON.stringify(crowdAudioVersions) },
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
