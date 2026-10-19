import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { HighlightCard, InsightCard, InsightEvents, Lang, MatchDetail, MatchSummary, Meta, Profile, Snapshot, ViewerMode } from './api'
import { api, profileQuery, sessionId, subscribeDemo } from './api'
import { t } from './i18n'
import { MomentumChart, PressureChart } from './components/Charts'
import { InsightCardView } from './components/InsightCard'
import { MetricsStrip } from './components/MetricsStrip'
import { HighlightsPanel, Logo, RecapPanel, Splash } from './components/Panels'
import { AgentWorkflowPanel } from './components/AgentWorkflow'
import { AWAY_COLOR, HOME_COLOR, Pitch } from './components/Pitch'
import { PlayerPanel } from './components/PlayerPanel'
import { Transport } from './components/Transport'
import { WhyPanel } from './components/WhyPanel'
import { isTypingTarget, useA11y } from './a11y/A11yContext'
import { AccessibilityCenter } from './a11y/AccessibilityCenter'
import { A11yButton, KeyboardHelp, SpeechBar } from './a11y/Widgets'
import { a11yStrings, speechLang } from './a11y/strings'
import { SHIFT_TYPES, describeFocus, goalAnnouncement, insightSpeech, matchStatus, periodicDue, priorityOf, shouldSpeak, simplifiedText, whatChanged, whyNarration } from './a11y/describe'
import { applyPreset, DEFAULT_PREFS, loadPrefs, speechEnabled } from './a11y/prefs'
import { cueFor } from './a11y/sounds'

type Tab = 'live' | 'highlights' | 'recap' | 'agents'
const TABS: Tab[] = ['live', 'highlights', 'recap', 'agents']
const AUDIO_PERSONA = '__accessibleAudio'
const BEFORE_PERSONA_KEY = 'matchshift.a11y.beforePersona'

// Three.js is only downloaded when a replay is opened.
const ReplayView = lazy(() => import('./replay/ReplayView'))

const loadProfile = (): Profile => {
  // URL parameters (?lang=fr&mode=TeachMe&team=WHA) override the remembered profile - handy for shareable links.
  const q = new URLSearchParams(window.location.search)
  if (q.get('lang') || q.get('mode')) {
    return {
      language: (q.get('lang') as Lang) ?? 'en',
      mode: (q.get('mode') as ViewerMode) ?? 'Analyst',
      focusTeamId: q.get('team'),
      focusPlayerId: q.get('player'),
    }
  }
  try {
    const raw = localStorage.getItem('matchshift.profile')
    if (raw) return JSON.parse(raw) as Profile
  } catch { /* storage unavailable: use defaults */ }
  return { mode: 'Analyst', language: 'en' }
}

export default function App() {
  const [meta, setMeta] = useState<Meta | null>(null)
  const [matches, setMatches] = useState<MatchSummary[]>([])
  const [detail, setDetail] = useState<MatchDetail | null>(null)
  const [profile, setProfile] = useState<Profile>(loadProfile)
  const [snap, setSnap] = useState<Snapshot | null>(null)
  const currentSnapshot = useRef(snap)
  currentSnapshot.current = snap
  const [connected, setConnected] = useState(false)
  const [tab, setTab] = useState<Tab>(() => (new URLSearchParams(window.location.search).get('tab') as Tab) ?? 'live')
  const [selected, setSelected] = useState<string | null>(() => new URLSearchParams(window.location.search).get('select'))
  const [why, setWhy] = useState<InsightCard | null>(null)
  const [focus, setFocus] = useState<{ events: InsightEvents; caption: string; teamId?: string; insightId?: string } | null>(null)
  const [replayId, setReplayId] = useState<string | null>(null)
  const s = t(profile.language)

  // Optional accessibility layer (everything off by default).
  const a11y = useA11y()
  const { prefs } = a11y
  // Ask the agent workflow for accessible (spoken / nonvisual) output only when this viewer uses those features.
  const wantsAccessible = speechEnabled(prefs) || prefs.describeVisuals || prefs.simplified
  const streamProfile = useMemo<Profile>(() => ({ ...profile, accessible: wantsAccessible }), [profile, wantsAccessible])
  const as = a11yStrings(profile.language)
  const voiceLang = speechLang(profile.language)
  const [centerOpen, setCenterOpen] = useState(false)
  const [keysOpen, setKeysOpen] = useState(false)
  const [description, setDescription] = useState<string | null>(null)
  // The intro splash is decided once at load: toggling motion options later must not replay it over the app.
  const [showSplash] = useState(() => !a11y.reducedMotion)

  useEffect(() => {
    try { localStorage.setItem('matchshift.profile', JSON.stringify(profile)) } catch { /* ignore */ }
  }, [profile])

  useEffect(() => {
    api.meta().then(setMeta).catch(() => undefined)
    api.matches().then(setMatches).catch(() => undefined)
  }, [])

// Static GitHub Pages demo uses an in-browser playback subscription.
  // Normal server/Azure mode continues to use SSE with polling fallback.
  const [polling, setPolling] = useState(() =>
    new URLSearchParams(window.location.search).get('transport') === 'poll'
  )

  useEffect(() => {
    // GitHub Pages / static demo mode
    if (subscribeDemo) {
      return subscribeDemo(
        streamProfile,
        (next) => {
          setSnap(next)
          setConnected(true)
        },
        (ok) => setConnected(ok),
      )
    }

    // Normal backend polling mode
    if (polling) {
      let stop = false

      const tick = async () => {
        try {
          const res = await fetch(`/api/demo/state?${profileQuery(streamProfile)}`)
          if (res.ok) {
            const next = (await res.json()) as Snapshot
            if (!stop) {
              setSnap(next)
              setConnected(true)
            }
          }
        } catch {
          setConnected(false)
        }

        if (!stop) window.setTimeout(tick, 400)
      }

      void tick()
      return () => {
        stop = true
      }
    }

    // Normal backend SSE mode
    let errors = 0
    const source = new EventSource(api.streamUrl(streamProfile))

    source.addEventListener('snapshot', (e) => {
      errors = 0
      setSnap(JSON.parse((e as MessageEvent).data) as Snapshot)
    })

    source.onopen = () => setConnected(true)

    source.onerror = () => {
      setConnected(false)
      if (++errors >= 3) {
        source.close()
        setPolling(true)
      }
    }

    return () => source.close()
  }, [streamProfile, polling])
  useEffect(() => {
    if (snap?.matchId && detail?.summary.id !== snap.matchId) api.match(snap.matchId).then(setDetail).catch(() => undefined)
  }, [snap?.matchId, detail?.summary.id])

  useEffect(() => {
    if (!detail || detail.summary.id !== snap?.matchId) return
    const squads = [...detail.homeSquad, ...detail.awaySquad]
    if (profile.focusTeamId && ![detail.summary.home.id, detail.summary.away.id].includes(profile.focusTeamId))
      setProfile(p => ({ ...p, focusTeamId: null, focusPlayerId: null }))
    else if (profile.focusPlayerId && !squads.some(p => p.id === profile.focusPlayerId && p.teamId === profile.focusTeamId))
      setProfile(p => ({ ...p, focusPlayerId: null }))
  }, [detail, snap?.matchId, profile.focusTeamId, profile.focusPlayerId])

  useEffect(() => { setWhy(null); setFocus(null); setSelected(null); setReplayId(null) }, [snap?.matchId, snap?.epoch])
  useEffect(() => { setDescription(null) }, [focus])
  useEffect(() => { setWhy(null); setFocus(null) }, [profile])

  useEffect(() => { document.documentElement.lang = profile.language }, [profile.language])

  const update = (patch: Partial<Profile>) => setProfile((p) => ({ ...p, ...patch }))

  const teamName = useCallback((id?: string) => (snap ? (id === snap.home.id ? snap.home.shortName : id === snap.away.id ? snap.away.shortName : '') : ''), [snap])
  const opponentOf = useCallback((id?: string) => (snap ? (id === snap.home.id ? snap.away.shortName : id === snap.away.id ? snap.home.shortName : '') : ''), [snap])

  const showInsight = async (card: InsightCard) => {
    const events = await api.insightEvents(card.id)
    if (currentSnapshot.current?.matchId !== snap?.matchId || currentSnapshot.current?.epoch !== snap?.epoch) return
    setSelected(card.id)
    setWhy(null)
    setFocus({ events, caption: `${card.minute}' · ${card.narrative.headline}`, teamId: card.teamId, insightId: card.id })
  }

  const openReplay = (insightId: string) => { setSelected(insightId); setWhy(null); setReplayId(insightId) }

  const showHighlight = async (h: HighlightCard) => {
    const card = snap?.insights.find((i) => i.id === h.insightId)
    if (card) return showInsight(card)
    const events = await api.highlightEvents(h.id)
    if (currentSnapshot.current?.matchId !== snap?.matchId || currentSnapshot.current?.epoch !== snap?.epoch) return
    setFocus({ events, caption: `${h.minute}' · ${h.headline}`, teamId: h.teamId })
  }

  // Deep links: ?why=<insightId> opens the evidence, ?show=<insightId> draws it on the pitch.
  const [deepLinkDone, setDeepLinkDone] = useState(false)
  useEffect(() => {
    if (!snap || deepLinkDone) return
    const params = new URLSearchParams(window.location.search)
    const whyId = params.get('why')
    const showId = params.get('show')
    const replayParam = params.get('replay')
    const card = snap.insights.find((i) => i.id === (whyId ?? showId ?? replayParam))
    if (card) {
      setSelected(card.id)
      if (whyId) setWhy(card)
      else if (replayParam) setReplayId(card.id)
      else void showInsight(card)
    }
    if (card || (!whyId && !showId && !replayParam)) setDeepLinkDone(true)
  }, [snap, deepLinkDone]) // eslint-disable-line react-hooks/exhaustive-deps

  const players = useMemo(() => {
    if (!detail || !profile.focusTeamId) return []
    return profile.focusTeamId === detail.summary.home.id ? detail.homeSquad : detail.awaySquad
  }, [detail, profile.focusTeamId])

  // ---------------------------------------------------------------- accessibility: new insights
  // Only insights that arrive while watching are news. On load, a new match, a reset or a seek, the existing
  // insights are marked as seen so nothing is read out as a backlog.
  const seenInsights = useRef<{ key: string; ids: Set<string> } | null>(null)
  useEffect(() => {
    if (!snap) return
    const key = `${snap.matchId}:${snap.epoch}`
    if (seenInsights.current?.key !== key) {
      seenInsights.current = { key, ids: new Set(snap.insights.map(i => i.id)) }
      a11y.speech.resetKeys('insight:')
      return
    }
    const seen = seenInsights.current.ids
    const fresh = snap.insights.filter(i => !seen.has(i.id))
    if (fresh.length === 0) return
    for (const i of fresh) seen.add(i.id)
    // A burst (fast playback) is trimmed to the most important few; the queue discards anything that goes stale.
    const ranked = [...fresh].sort((a, b) => priorityOf(b) - priorityOf(a) || b.importance - a.importance).slice(0, 3)
    a11y.cue(cueFor(ranked[0].type))
    if (prefs.spokenInsights) {
      for (const card of ranked) {
        if (!shouldSpeak(card, prefs.speechDetail)) continue
        if (prefs.audioMatchMode && card.type === 'GOAL') continue // Audio Match Mode already announces goals with the score.
        a11y.say({
          text: insightSpeech(card, prefs.speechDetail, prefs.simplified, as, teamName, opponentOf),
          priority: priorityOf(card), kind: 'insight', label: card.narrative.typeLabel, key: `insight:${card.id}`,
          clock: `${card.minute}'`, lang: voiceLang,
        })
      }
    } else {
      // Screen readers: one concise announcement; assertive only for critical moments.
      const top = ranked[0]
      a11y.announce(`${top.minute}' ${top.narrative.typeLabel}: ${top.narrative.headline}`, priorityOf(top) === 2)
    }
    if (prefs.periodicRecap === 'shifts' && ranked.some(c => SHIFT_TYPES.has(c.type) && c.severity === 'High')) {
      a11y.say({ text: matchStatus(snap, as), priority: 0, kind: 'recap', key: `recap:${key}:${snap.clock.minute}`, clock: snap.clock.label, lang: voiceLang, label: as.opt.periodicRecap[0] })
    }
  }, [snap?.insights, snap?.matchId, snap?.epoch]) // eslint-disable-line react-hooks/exhaustive-deps

  // ---------------------------------------------------------------- accessibility: score, half-time, recaps
  const matchState = useRef<{ key: string; minute: number; home: number; away: number; finished: boolean } | null>(null)
  useEffect(() => {
    if (!snap) return
    const key = `${snap.matchId}:${snap.epoch}`
    const prev = matchState.current
    const now = { key, minute: snap.clock.minute, home: snap.score.home, away: snap.score.away, finished: snap.finished }
    matchState.current = now
    if (!prev || prev.key !== key) return
    const status = (k: string, text: string, priority: 0 | 1 | 2, kind = 'status') =>
      a11y.say({ text, priority, kind, key: k, clock: snap.clock.label, lang: voiceLang, label: as.opt.audioMatchMode[0] })
    if (now.home + now.away > prev.home + prev.away) {
      const text = goalAnnouncement(snap, now.home > prev.home ? snap.home.id : snap.away.id, as)
      a11y.announce(text, true)
      if (prefs.audioMatchMode) status(`goal:${key}:${now.home}-${now.away}`, text, 2)
    }
    if (!prev.finished && now.finished) {
      a11y.announce(as.status_.fulltime)
      if (prefs.audioMatchMode || prefs.periodicRecap !== 'off') status(`ft:${key}`, `${as.status_.fulltime} ${matchStatus(snap, as)}`, 1)
    } else if (prev.minute < 45 && now.minute >= 45) {
      if (prefs.audioMatchMode || prefs.periodicRecap !== 'off') status(`ht:${key}`, `${as.status_.halftime} ${matchStatus(snap, as)}`, 1)
    } else if (prefs.periodicRecap !== 'halftime' && periodicDue(prefs.periodicRecap, prev.minute, now.minute)) {
      a11y.speech.clearKind('recap') // an older recap still waiting is stale
      status(`recap:${key}:${now.minute}`, matchStatus(snap, as), 0, 'recap')
    }
  }, [snap]) // eslint-disable-line react-hooks/exhaustive-deps

  const speakStatus = () => {
    if (snap) a11y.say({ text: matchStatus(snap, as), priority: 1, kind: 'status', clock: snap.clock.label, lang: voiceLang, label: as.opt.audioMatchMode[0] }, { allowRepeat: true })
  }
  // Turning Audio Match Mode on gives an immediate status, so the user knows it works.
  const audioModeWasOn = useRef(prefs.audioMatchMode)
  useEffect(() => {
    if (prefs.audioMatchMode && !audioModeWasOn.current) speakStatus()
    audioModeWasOn.current = prefs.audioMatchMode
  }, [prefs.audioMatchMode]) // eslint-disable-line react-hooks/exhaustive-deps

  // "?" opens keyboard help (never while typing, never with modifier keys).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== '?' || e.ctrlKey || e.metaKey || e.altKey || isTypingTarget(e.target)) return
      e.preventDefault()
      setKeysOpen(true)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // "Accessible audio" persona: audio-first options plus concise wording; leaving it restores the previous options.
  const chooseMode = (value: string) => {
    if (value === AUDIO_PERSONA) {
      if (!prefs.audioPersona) { try { localStorage.setItem(BEFORE_PERSONA_KEY, JSON.stringify(prefs)) } catch { /* ignore */ } }
      a11y.setPrefs(p => ({ ...applyPreset('audioFirst', p), audioPersona: true }))
      update({ mode: 'WhatMatters' })
      return
    }
    if (prefs.audioPersona) {
      let restored = { ...DEFAULT_PREFS }
      try {
        const raw = localStorage.getItem(BEFORE_PERSONA_KEY)
        if (raw) { localStorage.setItem('matchshift.a11y', raw); restored = loadPrefs() }
      } catch { /* ignore */ }
      a11y.setPrefs({ ...restored, audioPersona: false })
    }
    update({ mode: value as ViewerMode })
  }

  const readAloud = (text: string, kind: string, label?: string) =>
    a11y.say({ text, priority: 1, kind, label, lang: voiceLang, clock: snap?.clock.label }, { allowRepeat: true })

  if (!snap) {
    return (
      <>
        {showSplash && <Splash tagline={s.tagline} synthetic={s.synthetic} />}
        <p className="empty">{connected ? '…' : 'Connecting to the MatchShift API…'}</p>
      </>
    )
  }

  return (
    <div className="app">
      {showSplash && <Splash tagline={s.tagline} synthetic={s.synthetic} />}
      <header className="topbar">
        <div className="brand">
          <Logo />
          <span className="brand-name">MatchShift</span>
          <span className="badge-synthetic" title={meta?.dataNotice}>{s.synthetic}</span>
        </div>

        {/* Not a live region: the clock changes every second. Goals are announced through the shared announcer. */}
        <div className="scoreboard" role="group" aria-label={`${snap.home.shortName} ${snap.score.home}, ${snap.away.shortName} ${snap.score.away}, ${snap.clock.label}`}>
          <span className="team-name"><span className="team-swatch" style={{ background: HOME_COLOR }} />{snap.home.shortName}</span>
          <span className="score">{snap.score.home} – {snap.score.away}</span>
          <span className="team-name">{snap.away.shortName}<span className="team-swatch swatch-away" style={{ background: AWAY_COLOR }} /></span>
          <span className="clock" aria-label={`${s.minute} ${snap.clock.minute}`}>{snap.clock.label}</span>
        </div>

        <div className="controls-right">
          <A11yButton lang={profile.language} onOpen={() => setCenterOpen(true)} />
    < a
className = "chip"
href = {`${import.meta.env.BASE_URL}broadcast?session=${sessionId}&lang=${profile.language}`}
target = "_blank"
rel = "noreferrer"
    >
    Broadcast
    </a>
          <label className="control-label">{s.match}
            <select value={snap.matchId} onChange={(e) => { setFocus(null); setSelected(null); void api.reset(e.target.value) }}>
              {matches.map((m) => <option key={m.id} value={m.id}>{m.home.shortName} v {m.away.shortName} · {m.scenario.split(':')[0]}</option>)}
            </select>
          </label>
          <label className="control-label">{s.mode}
            <select value={prefs.audioPersona ? AUDIO_PERSONA : profile.mode} onChange={(e) => chooseMode(e.target.value)}>
              {(meta?.modes ?? ['TeachMe', 'WhatMatters', 'Analyst', 'MyTeam', 'Studio']).map((m) => <option key={m} value={m}>{s.modes[m]}</option>)}
              <option value={AUDIO_PERSONA}>{as.persona}</option>
            </select>
          </label>
          {profile.mode === 'MyTeam' && (
            <>
              <label className="control-label">{s.focus}
                <select value={profile.focusTeamId ?? ''} onChange={(e) => update({ focusTeamId: e.target.value || null, focusPlayerId: null })}>
                  <option value="">{s.noFocus}</option>
                  <option value={snap.home.id}>{snap.home.shortName}</option>
                  <option value={snap.away.id}>{snap.away.shortName}</option>
                </select>
              </label>
              {players.length > 0 && (
                <label className="control-label">&nbsp;
                  <select value={profile.focusPlayerId ?? ''} onChange={(e) => update({ focusPlayerId: e.target.value || null })}>
                    <option value="">—</option>
                    {players.map((p) => <option key={p.id} value={p.id}>{p.shirtNumber} {p.name}</option>)}
                  </select>
                </label>
              )}
            </>
          )}
          <label className="control-label">{s.language}
            <select value={profile.language} onChange={(e) => update({ language: e.target.value as Lang })}>
              {(meta?.languages ?? [{ code: 'en', name: 'English' }]).map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
            </select>
          </label>
        </div>
      </header>

      <main className="main">
        <div className="left">
          <div className="pitch-wrap">
            <Pitch home={snap.home} away={snap.away} trail={snap.recentEvents} focus={focus?.events ?? null}
              focusTeamId={focus?.teamId} ariaLabel={focus ? focus.caption : `${snap.home.shortName} v ${snap.away.shortName}`} />
            {focus && (
              <div className="pitch-overlay-bar">
                <span className="pitch-caption"><strong>{s.showMe}:</strong> {focus.caption}</span>
                <span style={{ display: 'flex', gap: 6 }}>
                  {prefs.describeVisuals && (
                    <button className="chip" onClick={() => {
                      const text = describeFocus(focus.events, focus.teamId, snap.home.id, teamName, as)
                      setDescription(text)
                      readAloud(text, 'describe', as.describe.title)
                    }}>{as.describe.button}</button>
                  )}
                  {focus.insightId && <button className="chip replay-btn" onClick={() => openReplay(focus.insightId!)}>{s.replay.button}</button>}
                  <button className="primary" onClick={() => { setFocus(null) }}>{s.backToLive}</button>
                </span>
              </div>
            )}
            <div className="pitch-legend">
              <span className="legend-key"><i style={{ background: HOME_COLOR }} />{snap.home.name}</span>
              <span className="legend-key"><i className="swatch-away" style={{ background: AWAY_COLOR }} />{snap.away.name}</span>
              {focus && <span>{s.showingEvents} ({focus.events.events.length})</span>}
            </div>
            {description && focus && <p className="pitch-description"><strong>{as.describe.title}:</strong> {description}</p>}
          </div>
          {snap.focusedPlayer && <PlayerPanel player={snap.focusedPlayer} lang={profile.language}
            name={players.find(p => p.id === snap.focusedPlayer?.playerId)?.name ?? snap.focusedPlayer.playerId} />}
          <MetricsStrip snap={snap} s={s} />
        </div>

        <aside className="right">
          <div className="tabs" role="tablist" onKeyDown={(e) => {
            // Arrow keys move between tabs (WAI-ARIA tabs pattern).
            const i = TABS.indexOf(tab)
            const next = e.key === 'ArrowRight' ? TABS[(i + 1) % TABS.length] : e.key === 'ArrowLeft' ? TABS[(i + TABS.length - 1) % TABS.length]
              : e.key === 'Home' ? TABS[0] : e.key === 'End' ? TABS[TABS.length - 1] : null
            if (!next) return
            e.preventDefault()
            setTab(next)
            document.getElementById(`tab-${next}`)?.focus()
          }}>
            {TABS.map((k) => (
              <button key={k} id={`tab-${k}`} role="tab" aria-selected={tab === k} aria-controls="tab-panel" tabIndex={tab === k ? 0 : -1}
                className={tab === k ? 'active' : ''} onClick={() => setTab(k)}>
                {k === 'live' ? `${s.live} (${snap.insights.length})` : s[k]}
              </button>
            ))}
          </div>
          <div className="tab-body" role="tabpanel" id="tab-panel" aria-labelledby={`tab-${tab}`}>
            {tab === 'live' && (snap.insights.length === 0
              ? <p className="empty">{s.waiting}</p>
              : snap.insights.map((card) => (
                <InsightCardView key={card.id} card={card} mode={profile.mode} s={s} selected={selected === card.id} teamName={teamName}
                  simplified={prefs.simplified ? { text: simplifiedText(card, as, teamName, opponentOf), more: as.simplified.more, less: as.simplified.less } : null}
                  onSelect={() => setSelected(card.id)} onWhy={() => { setSelected(card.id); setWhy(card) }} onShow={() => void showInsight(card)} onReplay={() => openReplay(card.id)} />
              )))}
            {tab === 'highlights' && <HighlightsPanel snap={snap} s={s} onShow={(h) => void showHighlight(h)} onReplay={openReplay} />}
            {tab === 'recap' && <RecapPanel snap={snap} s={s} profile={profile} styles={meta?.recapStyles ?? ['Casual', 'Analyst', 'Broadcast', 'Social']} />}
            {tab === 'agents' && <AgentWorkflowPanel key={`${snap.matchId}:${snap.epoch}`} insightId={selected} profile={streamProfile} defaultAccessible />}
          </div>
        </aside>
      </main>

      <footer className="bottom">
        <Transport snap={snap} speeds={meta?.speeds ?? [1, 2, 5, 10]} s={s} onHighlight={(h) => void showHighlight(h)} />
        <div className="charts">
          <MomentumChart points={snap.timeline} home={snap.home} away={snap.away} s={s} />
          <PressureChart points={snap.timeline} home={snap.home} away={snap.away} s={s} />
        </div>
      </footer>

      {why && (() => {
        const card = snap.insights.find(i => i.id === why.id) ?? why
        const changed = whatChanged(card, as)
        return (
          <WhyPanel card={card} s={s} onClose={() => setWhy(null)} onShow={() => void showInsight(why)} onReplay={() => openReplay(why.id)}
            extraActions={speechEnabled(prefs) ? (
              <>
                <button className="chip" onClick={() => readAloud(whyNarration(card, as), 'why', card.narrative.typeLabel)}>{as.why.read}</button>
                {changed && <button className="chip" onClick={() => readAloud(changed, 'why', card.narrative.typeLabel)}>{as.why.readChange}</button>}
              </>
            ) : undefined} />
        )
      })()}

      {replayId && (
        <Suspense fallback={<div className="replay-root"><div className="replay-status">{s.replay.loading}</div></div>}>
          <ReplayView insightId={replayId} card={snap.insights.find(i => i.id === replayId)} home={snap.home} away={snap.away} s={s}
            lang={profile.language} onClose={() => setReplayId(null)} />
        </Suspense>
      )}

      <SpeechBar lang={profile.language} overReplay={!!replayId} onStatus={prefs.audioMatchMode ? speakStatus : undefined} />
      {centerOpen && <AccessibilityCenter lang={profile.language} onClose={() => setCenterOpen(false)} onKeyboardHelp={() => { setCenterOpen(false); setKeysOpen(true) }} />}
      {keysOpen && <KeyboardHelp lang={profile.language} onClose={() => setKeysOpen(false)} />}
    </div>
  )
}
