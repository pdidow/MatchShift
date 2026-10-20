import { useEffect, useState } from 'react'
import { api, subscribeDemo, type Snapshot, type Lang } from '../api'
import { isLang } from '../languages'
import { Pitch } from './Pitch'

export function BroadcastView() {
  const [snap, setSnap] = useState<Snapshot | null>(null)
  const preview = location.pathname.endsWith('/broadcast')
    useEffect(() => {
        document.body.classList.add('broadcast-body')

        const requested = new URLSearchParams(location.search).get('lang')
        const profile = { mode: 'Studio' as const, language: (isLang(requested) ? requested : 'en') as Lang }

        // Static GitHub Pages demo
        if (subscribeDemo) {
            const unsubscribe = subscribeDemo(
                profile,
                next => setSnap(next),
                ok => {
                    if (!ok) setSnap(null)
                },
            )

            return () => {
                unsubscribe()
                document.body.classList.remove('broadcast-body')
            }
        }

        // Normal server/Azure mode
        let stream: EventSource | undefined
        let watchdog: number | undefined

        const disconnect = () => {
            stream?.close()
            clearTimeout(watchdog)
            setSnap(null)
        }

        const connect = () => {
            disconnect()
            if (!navigator.onLine) return

            stream = new EventSource(api.streamUrl(profile))

            stream.addEventListener('snapshot', event => {
                setSnap(JSON.parse((event as MessageEvent).data) as Snapshot)
                clearTimeout(watchdog)
                watchdog = window.setTimeout(() => setSnap(null), 12000)
            })

            stream.onerror = () => setSnap(null)
        }

        window.addEventListener('offline', disconnect)
        window.addEventListener('online', connect)

        connect()

        return () => {
            stream?.close()
            clearTimeout(watchdog)

            window.removeEventListener('offline', disconnect)
            window.removeEventListener('online', connect)

            document.body.classList.remove('broadcast-body')
        }
    }, [])
  return <main className={`broadcast-view ${preview ? 'preview' : ''}`}>
    {preview && snap && <>
      <div className="broadcast-score">{snap.home.shortName} {snap.score.home} – {snap.score.away} {snap.away.shortName} · {snap.clock.label} · Synthetic demonstration</div>
      <Pitch home={snap.home} away={snap.away} trail={snap.recentEvents} focus={null} ariaLabel="Original synthetic event visualization" />
    </>}
    {snap?.overlay && <aside className="broadcast-overlay" key={`${snap.epoch}:${snap.overlay.insightId}`}>
      <small>MatchShift · {snap.clock.label} · Synthetic · {snap.overlay.source === 'mock' ? 'Mock — no model call' : snap.overlay.source}</small>
      <p>{snap.overlay.text}</p>
    </aside>}
  </main>
}
