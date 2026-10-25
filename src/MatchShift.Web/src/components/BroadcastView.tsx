import { useEffect, useState } from 'react'
import { api, sessionId, subscribeDemo, type Snapshot, type Lang } from '../api'
import { isLang, localeOf } from '../languages'
import { t } from '../i18n'
import { broadcastCopy } from '../broadcastCopy'
import { broadcastHref, broadcastRoute } from '../broadcastRoute'
import { Pitch } from './Pitch'

export function BroadcastView() {
  const [snap, setSnap] = useState<Snapshot | null>(null)
  const preview = broadcastRoute(location.pathname, import.meta.env.BASE_URL) === 'broadcast'
  const requested = new URLSearchParams(location.search).get('lang')
  const lang: Lang = isLang(requested) ? requested : 'en'
    useEffect(() => {
        document.body.classList.add('broadcast-body')
        document.documentElement.lang = localeOf(lang)

        const profile = { mode: 'Studio' as const, language: lang }

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
    }, [lang])
  return <main className={`broadcast-view ${preview ? 'preview' : ''}`}>
    {preview && <header className="broadcast-intro">
      <h1>{t(lang).broadcast}</h1>
      <p>{broadcastCopy[lang].description}</p>
      <p>{broadcastCopy[lang].note} <a href={broadcastHref('overlay', import.meta.env.BASE_URL, location.search, sessionId, lang)} target="_blank" rel="noreferrer">{broadcastCopy[lang].overlay}</a></p>
    </header>}
    {preview && snap && <>
      <div className="broadcast-score"><span>{snap.home.shortName} {snap.score.home} – {snap.score.away} {snap.away.shortName}</span><span>{snap.clock.label} · Synthetic demonstration</span></div>
      <Pitch home={snap.home} away={snap.away} trail={snap.recentEvents} focus={null} ariaLabel="Original synthetic event visualization" />
    </>}
    {snap?.overlay && <aside className="broadcast-overlay" key={`${snap.epoch}:${snap.overlay.insightId}`}>
      <small>MatchShift · {snap.clock.label} · Synthetic · {snap.overlay.source === 'mock' ? 'Mock — no model call' : snap.overlay.source}</small>
      <p>{snap.overlay.text}</p>
    </aside>}
  </main>
}
