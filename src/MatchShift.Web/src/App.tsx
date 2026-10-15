import { useCallback, useEffect, useMemo, useState } from 'react'
import type { HighlightCard, InsightCard, InsightEvents, Lang, MatchDetail, MatchSummary, Meta, Profile, Snapshot, ViewerMode } from './api'
import { api, profileQuery } from './api'
import { t } from './i18n'
import { MomentumChart, PressureChart } from './components/Charts'
import { InsightCardView } from './components/InsightCard'
import { MetricsStrip } from './components/MetricsStrip'
import { AgentPanel, HighlightsPanel, Logo, RecapPanel, Splash } from './components/Panels'
import { AWAY_COLOR, HOME_COLOR, Pitch } from './components/Pitch'
import { Transport } from './components/Transport'
import { WhyPanel } from './components/WhyPanel'

type Tab = 'live' | 'highlights' | 'recap' | 'agents'

const loadProfile = (): Profile => {
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
  const [connected, setConnected] = useState(false)
  const [tab, setTab] = useState<Tab>('live')
  const [selected, setSelected] = useState<string | null>(null)
  const [why, setWhy] = useState<InsightCard | null>(null)
  const [focus, setFocus] = useState<{ events: InsightEvents; caption: string; teamId?: string } | null>(null)
  const s = t(profile.language)

  useEffect(() => {
    try { localStorage.setItem('matchshift.profile', JSON.stringify(profile)) } catch { /* ignore */ }
  }, [profile])

  useEffect(() => {
    api.meta().then(setMeta).catch(() => undefined)
    api.matches().then(setMatches).catch(() => undefined)
  }, [])

  // Live snapshots over Server-Sent Events (reconnecting when the viewer profile changes).
  // Falls back to polling if SSE is blocked, or when ?transport=poll is set.
  const [polling, setPolling] = useState(() => new URLSearchParams(window.location.search).get('transport') === 'poll')
  useEffect(() => {
    if (polling) {
      let stop = false
      const tick = async () => {
        try {
          const res = await fetch(`/api/demo/state?${profileQuery(profile)}`)
          if (res.ok && !stop) { setSnap((await res.json()) as Snapshot); setConnected(true) }
        } catch { setConnected(false) }
        if (!stop) window.setTimeout(tick, 400)
      }
      void tick()
      return () => { stop = true }
    }
    let errors = 0
    const source = new EventSource(api.streamUrl(profile))
    source.addEventListener('snapshot', (e) => { errors = 0; setSnap(JSON.parse((e as MessageEvent).data) as Snapshot) })
    source.onopen = () => setConnected(true)
    source.onerror = () => {
      setConnected(false)
      if (++errors >= 3) { source.close(); setPolling(true) }
    }
    return () => source.close()
  }, [profile, polling])

  useEffect(() => {
    if (snap?.matchId && detail?.summary.id !== snap.matchId) api.match(snap.matchId).then(setDetail).catch(() => undefined)
  }, [snap?.matchId, detail?.summary.id])

  useEffect(() => { document.documentElement.lang = profile.language }, [profile.language])

  const update = (patch: Partial<Profile>) => setProfile((p) => ({ ...p, ...patch }))

  const teamName = useCallback((id?: string) => (snap ? (id === snap.home.id ? snap.home.shortName : id === snap.away.id ? snap.away.shortName : '') : ''), [snap])

  const showInsight = async (card: InsightCard) => {
    const events = await api.insightEvents(card.id)
    setSelected(card.id)
    setWhy(null)
    setFocus({ events, caption: `${card.minute}' · ${card.narrative.headline}`, teamId: card.teamId })
  }

  const showHighlight = async (h: HighlightCard) => {
    const card = snap?.insights.find((i) => i.id === h.insightId)
    if (card) return showInsight(card)
    const events = await api.highlightEvents(h.id)
    setFocus({ events, caption: `${h.minute}' · ${h.headline}`, teamId: h.teamId })
  }

  // Deep links: ?why=<insightId> opens the evidence, ?show=<insightId> draws it on the pitch.
  const [deepLinkDone, setDeepLinkDone] = useState(false)
  useEffect(() => {
    if (!snap || deepLinkDone) return
    const params = new URLSearchParams(window.location.search)
    const whyId = params.get('why')
    const showId = params.get('show')
    const card = snap.insights.find((i) => i.id === (whyId ?? showId))
    if (card) {
      setSelected(card.id)
      if (whyId) setWhy(card)
      else void showInsight(card)
    }
    setDeepLinkDone(true)
  }, [snap, deepLinkDone]) // eslint-disable-line react-hooks/exhaustive-deps

  const players = useMemo(() => {
    if (!detail || !profile.focusTeamId) return []
    return profile.focusTeamId === detail.summary.home.id ? detail.homeSquad : detail.awaySquad
  }, [detail, profile.focusTeamId])

  if (!snap) {
    return (
      <>
        <Splash tagline={s.tagline} synthetic={s.synthetic} />
        <p className="empty">{connected ? '…' : 'Connecting to the MatchShift API…'}</p>
      </>
    )
  }

  return (
    <div className="app">
      <Splash tagline={s.tagline} synthetic={s.synthetic} />
      <header className="topbar">
        <div className="brand">
          <Logo />
          <span className="brand-name">MatchShift</span>
          <span className="badge-synthetic" title={meta?.dataNotice}>{s.synthetic}</span>
        </div>

        <div className="scoreboard" aria-live="polite">
          <span className="team-name"><span className="team-swatch" style={{ background: HOME_COLOR }} />{snap.home.shortName}</span>
          <span className="score">{snap.score.home} – {snap.score.away}</span>
          <span className="team-name">{snap.away.shortName}<span className="team-swatch" style={{ background: AWAY_COLOR }} /></span>
          <span className="clock" aria-label={`${s.minute} ${snap.clock.minute}`}>{snap.clock.label}</span>
        </div>

        <div className="controls-right">
          <label className="control-label">{s.match}
            <select value={snap.matchId} onChange={(e) => { setFocus(null); setSelected(null); void api.reset(e.target.value) }}>
              {matches.map((m) => <option key={m.id} value={m.id}>{m.home.shortName} v {m.away.shortName} · {m.scenario.split(':')[0]}</option>)}
            </select>
          </label>
          <label className="control-label">{s.mode}
            <select value={profile.mode} onChange={(e) => update({ mode: e.target.value as ViewerMode })}>
              {(meta?.modes ?? ['TeachMe', 'WhatMatters', 'Analyst', 'MyTeam', 'Studio']).map((m) => <option key={m} value={m}>{s.modes[m]}</option>)}
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
                <button className="primary" onClick={() => { setFocus(null) }}>{s.backToLive}</button>
              </div>
            )}
            <div className="pitch-legend">
              <span className="legend-key"><i style={{ background: HOME_COLOR }} />{snap.home.name}</span>
              <span className="legend-key"><i style={{ background: AWAY_COLOR }} />{snap.away.name}</span>
              {focus && <span>{s.showingEvents} ({focus.events.events.length})</span>}
            </div>
          </div>
          <MetricsStrip snap={snap} s={s} />
        </div>

        <aside className="right">
          <div className="tabs" role="tablist">
            {(['live', 'highlights', 'recap', 'agents'] as Tab[]).map((k) => (
              <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'active' : ''} onClick={() => setTab(k)}>
                {k === 'live' ? `${s.live} (${snap.insights.length})` : s[k]}
              </button>
            ))}
          </div>
          <div className="tab-body" role="tabpanel">
            {tab === 'live' && (snap.insights.length === 0
              ? <p className="empty">{s.waiting}</p>
              : snap.insights.map((card) => (
                <InsightCardView key={card.id} card={card} mode={profile.mode} s={s} selected={selected === card.id} teamName={teamName}
                  onSelect={() => setSelected(card.id)} onWhy={() => { setSelected(card.id); setWhy(card) }} onShow={() => void showInsight(card)} />
              )))}
            {tab === 'highlights' && <HighlightsPanel snap={snap} s={s} onShow={(h) => void showHighlight(h)} />}
            {tab === 'recap' && <RecapPanel snap={snap} s={s} profile={profile} styles={meta?.recapStyles ?? ['Casual', 'Analyst', 'Broadcast', 'Social']} />}
            {tab === 'agents' && <AgentPanel insightId={selected} profile={profile} s={s} />}
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

      {why && <WhyPanel card={why} s={s} onClose={() => setWhy(null)} onShow={() => void showInsight(why)} />}
    </div>
  )
}
