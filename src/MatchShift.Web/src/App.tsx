import { lazy, Suspense, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { HighlightCard, InsightCard, InsightEvents, Lang, MatchDetail, MatchSummary, Meta, Profile, Snapshot, ViewerMode } from './api'
import { api, profileQuery, sessionId, subscribeDemo } from './api'
import { t } from './i18n'
import { MomentumChart, PressureChart } from './components/Charts'
import { InsightCardView } from './components/InsightCard'
import { MetricsStrip } from './components/MetricsStrip'
import { HighlightsPanel, Logo, RecapPanel } from './components/Panels'
import { AgentWorkflowPanel } from './components/AgentWorkflow'
import { AWAY_COLOR, HOME_COLOR, Pitch } from './components/Pitch'
import { PlayerPanel } from './components/PlayerPanel'
import { Transport } from './components/Transport'
import { WhyPanel } from './components/WhyPanel'
import { EmptyFeed, FeedIntro } from './components/Home'
import { isTypingTarget, useA11y } from './a11y/A11yContext'
import { AccessibilityCenter } from './a11y/AccessibilityCenter'
import { A11yButton, KeyboardHelp, SpeechBar } from './a11y/Widgets'
import { a11yStrings, speechLang } from './a11y/strings'
import { SHIFT_TYPES, describeFocus, goalAnnouncement, insightSpeech, matchStatus, periodicDue, priorityOf, shouldSpeak, simplifiedText, whatChanged, whyNarration } from './a11y/describe'
import { applyPreset, DEFAULT_PREFS, loadPrefs, speechEnabled } from './a11y/prefs'
import { cueFor } from './a11y/sounds'
import { JudgeGuide } from './guide/JudgeGuide'
import type { GuideStep } from './guide/guide'
import { FLAGSHIP, GUIDE_START, GUIDE_STEPS, findFlagship, wantsGuideFromUrl } from './guide/guide'
import { evidenceLegend, splitEvidence } from './evidence'
import { isLang, spokenClock } from './languages'
import { broadcastHref } from './broadcastRoute'
import { capDetail, insightCueClass, matchNarrationPolicy, matchTimelineLive } from './a11y/narrationPolicy'
import { ReplayAudioFocus } from './a11y/audioFocus'
import type { CueClass } from './a11y/speech'

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
      language: isLang(q.get('lang')) ? (q.get('lang') as Lang) : 'en',
      mode: (q.get('mode') as ViewerMode) ?? 'Analyst',
      focusTeamId: q.get('team'),
      focusPlayerId: q.get('player'),
    }
  }
  try {
    const raw = localStorage.getItem('matchshift.profile')
    if (raw) {
      const saved = JSON.parse(raw) as Profile
      return { ...saved, language: isLang(saved.language) ? saved.language : 'en' }
    }
  } catch { /* storage unavailable: use defaults */ }
  return { mode: 'Analyst', language: 'en' }
}

export default function App() {
  const [meta, setMeta] = useState<Meta | null>(null)
  const [matches, setMatches] = useState<MatchSummary[]>([])
  const [detail, setDetail] = useState<MatchDetail | null>(null)
  const [profile, setProfile] = useState<Profile>(loadProfile)
  const [received, setSnap] = useState<Snapshot | null>(null)
  // Keep the existing match view mounted while its translated snapshot arrives. Swap the data and UI labels together;
  // narration is held until the snapshot matches the newly selected language.
  const snap = received
  const snapshotReady = !snap || snap.profile.language === profile.language
  const displayedLanguage = snap?.profile.language ?? profile.language
  const displayedProfile = displayedLanguage === profile.language ? profile : { ...profile, language: displayedLanguage }
  const currentLanguage = useRef(profile.language)
  const currentSnapshot = useRef(snap)
  currentSnapshot.current = snapshotReady ? snap : null
  const [connected, setConnected] = useState(false)
  const [tab, setTab] = useState<Tab>(() => (new URLSearchParams(window.location.search).get('tab') as Tab) ?? 'live')
  const [selected, setSelected] = useState<string | null>(() => new URLSearchParams(window.location.search).get('select'))
  const [why, setWhy] = useState<InsightCard | null>(null)
  const [focus, setFocus] = useState<{ events: InsightEvents; caption: string; teamId?: string; insightId?: string } | null>(null)
  const [replayId, setReplayId] = useState<string | null>(null)
  const s = t(displayedLanguage)

  // Optional accessibility layer (everything off by default).
  const a11y = useA11y()
  const { prefs, setLanguage } = a11y
  // Ask the agent workflow for accessible (spoken / nonvisual) output only when this viewer uses those features.
  const wantsAccessible = speechEnabled(prefs) || prefs.describeVisuals || prefs.simplified
  const streamProfile = useMemo<Profile>(() => ({ ...profile, accessible: wantsAccessible }), [profile, wantsAccessible])
  const as = a11yStrings(displayedLanguage)
  const voiceLang = speechLang(displayedLanguage)
  // Switching language drops and stops queued narration in the old language, so it never keeps playing.
  useLayoutEffect(() => {
    currentLanguage.current = profile.language
    setLanguage(speechLang(profile.language))
    document.documentElement.lang = speechLang(displayedLanguage)
  }, [setLanguage, profile.language, displayedLanguage])
  const [centerOpen, setCenterOpen] = useState(false)
  const [keysOpen, setKeysOpen] = useState(false)
  const [description, setDescription] = useState<string | null>(null)

  // Guided demo (optional): a step over the real product, started from the top bar, the empty state or ?demo=judge.
  const [guide, setGuide] = useState<GuideStep | null>(() => (wantsGuideFromUrl(window.location.search) ? GUIDE_START : null))
  const [awaitFlagship, setAwaitFlagship] = useState(false)

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
    const acceptSnapshot = (next: Snapshot) => {
      if (next.profile.language === currentLanguage.current) setSnap(next)
    }
    // GitHub Pages / static demo mode
    if (subscribeDemo) {
      return subscribeDemo(
        streamProfile,
        (next) => {
          acceptSnapshot(next)
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
              acceptSnapshot(next)
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
      acceptSnapshot(JSON.parse((e as MessageEvent).data) as Snapshot)
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

  useEffect(() => { setWhy(null); setFocus(null); setSelected(null); setReplayId(null) }, [received?.matchId, received?.epoch])
  useEffect(() => { setDescription(null) }, [focus])
  // SHOW ME can be below the feed on a phone. Move both the viewport and keyboard focus to its evidence.
  useEffect(() => {
    if (!focus) return
    const frame = requestAnimationFrame(() => {
      const pitch = document.getElementById('match-evidence')
      pitch?.focus({ preventScroll: true })
      pitch?.scrollIntoView({ block: 'nearest', behavior: 'instant' })
    })
    return () => cancelAnimationFrame(frame)
  }, [focus])
  useEffect(() => { setWhy(null); setFocus(null) }, [profile.mode, profile.focusTeamId, profile.focusPlayerId])
  useEffect(() => { setDescription(null) }, [displayedLanguage])

  const flagship = snap ? findFlagship(snap.matchId, snap.insights) : undefined
  const scrollToCard = (id: string) => window.setTimeout(() =>
    document.getElementById(`card-${id}`)?.scrollIntoView({ block: 'nearest', behavior: a11y.reducedMotion ? 'auto' : 'smooth' }), 60)

  // After the guide jumps to the flagship moment, select the insight once the new snapshot contains it. Declared after the
  // effects that clear the selection on a new epoch, so it runs after them.
  useEffect(() => {
    if (!awaitFlagship || !flagship) return
    setAwaitFlagship(false)
    setSelected(flagship.id)
    scrollToCard(flagship.id)
  }, [awaitFlagship, flagship?.id, snap?.epoch]) // eslint-disable-line react-hooks/exhaustive-deps

  const update = (patch: Partial<Profile>) => {
    if (patch.language) {
      // Cancel speech and delayed live-region announcements synchronously with the selector interaction.
      currentLanguage.current = patch.language
      setLanguage(patch.language)
    }
    setProfile((p) => ({ ...p, ...patch }))
  }
  const openVerification = (insightId?: string) => {
    if (insightId) setSelected(insightId)
    setWhy(null)
    setTab('agents')
    requestAnimationFrame(() => document.getElementById('tab-agents')?.focus())
  }
  useEffect(() => { document.getElementById('tab-panel')?.scrollTo(0, 0) }, [tab])

  const teamName = useCallback((id?: string) => (snap ? (id === snap.home.id ? snap.home.shortName : id === snap.away.id ? snap.away.shortName : '') : ''), [snap])
  const opponentOf = useCallback((id?: string) => (snap ? (id === snap.home.id ? snap.away.shortName : id === snap.away.id ? snap.home.shortName : '') : ''), [snap])

  const showInsight = async (card: InsightCard) => {
    const events = await api.insightEvents(card.id)
    if (currentLanguage.current !== profile.language || currentSnapshot.current?.matchId !== snap?.matchId || currentSnapshot.current?.epoch !== snap?.epoch) return
    setSelected(card.id)
    setWhy(null)
    setFocus({ events, caption: `${card.minute}' · ${card.narrative.headline}`, teamId: card.teamId, insightId: card.id })
  }

  const openReplay = (insightId: string) => { setSelected(insightId); setWhy(null); setReplayId(insightId) }

  // Evidence Replay owns the audio and the clock while it is open. Opening it (from a card, WHY?, SHOW ME, highlights or the
  // guided demo) pauses the underlying match, stops and clears match narration and gives the replay exclusive focus.
  // Closing it releases focus (old match narration is not resumed) and restores the match's play/pause state; the
  // playback speed is never touched. A new match or reset while it was open is never auto-resumed.
  const replayFocus = useRef(new ReplayAudioFocus())
  useEffect(() => {
    const deps = { speech: a11y.speech, pause: () => void api.pause(), play: () => void api.play() }
    if (replayId) replayFocus.current.open(currentSnapshot.current, deps)
    else replayFocus.current.close(currentSnapshot.current, deps)
  }, [replayId]) // eslint-disable-line react-hooks/exhaustive-deps

  const showHighlight = async (h: HighlightCard) => {
    const card = snap?.insights.find((i) => i.id === h.insightId)
    if (card) return showInsight(card)
    const events = await api.highlightEvents(h.id)
    if (currentLanguage.current !== profile.language || currentSnapshot.current?.matchId !== snap?.matchId || currentSnapshot.current?.epoch !== snap?.epoch) return
    setFocus({ events, caption: `${h.minute}' · ${h.headline}`, teamId: h.teamId })
  }

  /** Moves the guided demo to a step by performing that step's REAL product action. */
  const goGuide = (step: GuideStep) => {
    setGuide(step)
    if (step === 'intro' || step === 'viewers') return // explanation only: the real controls are highlighted, nothing is changed
    if (!flagship) {
      // Jump to the flagship moment: the flagship match, paused just after the insight is detected.
      setWhy(null); setFocus(null); setReplayId(null); setTab('live')
      setAwaitFlagship(true)
      void (async () => { await api.reset(FLAGSHIP.matchId); await api.seek(FLAGSHIP.seekMinute) })()
      if (step !== 'insight') setGuide('insight')
      return
    }
    const card = flagship
    setSelected(card.id)
    if (step !== 'replay') setReplayId(null)
    if (step !== 'why') setWhy(null)
    if (step === 'agents' || step === 'trust') {
      setTab('agents')
      if (step === 'trust') window.setTimeout(() => document.getElementById('eval-proof')?.scrollIntoView({ block: 'nearest', behavior: a11y.reducedMotion ? 'auto' : 'smooth' }), 80)
      return
    }
    setTab('live')
    if (step === 'insight') { setFocus(null); scrollToCard(card.id) }
    else if (step === 'why') setWhy(card)
    else if (step === 'show') void showInsight(card)
    else if (step === 'replay') openReplay(card.id)
  }

  // The guide also follows the viewer's own clicks on the flagship insight (forward only), so using the real buttons
  // (including from the keyboard inside WHY?) keeps the explanation in step.
  useEffect(() => {
    if (!guide || guide === 'intro' || guide === 'viewers' || !flagship) return
    const id = flagship.id
    const derived: GuideStep | null = tab === 'agents' && selected === id ? 'agents'
      : replayId === id ? 'replay' : focus?.insightId === id ? 'show' : why?.id === id ? 'why' : null
    if (derived && GUIDE_STEPS.indexOf(derived) > GUIDE_STEPS.indexOf(guide)) setGuide(derived)
  }, [tab, selected, replayId, focus, why, flagship?.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const guideNext = guide === 'insight' ? 'why' : guide === 'why' ? 'show' : guide === 'show' ? 'replay' : null

  // Deep links: ?why=<insightId> opens the evidence, ?show=<insightId> draws it on the pitch.
  const [deepLinkDone, setDeepLinkDone] = useState(false)
  useEffect(() => {
    if (!snap || !snapshotReady || deepLinkDone) return
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

  // ---------------------------------------------------------------- speech follows the match clock
  // The coordinator learns the current match second first, so every cue below is judged against the live clock: anything
  // the match has already moved past is dropped instead of being spoken late. Speech never slows the match down. A
  // finished match no longer moves, so its final cues (the goal on the whistle, full time) may still speak.
  useEffect(() => {
    if (snap && snapshotReady) a11y.speech.setTimeline('match', snap.clock.second, matchTimelineLive(snap))
  }, [snap?.clock.second, snap?.isPlaying, snap?.finished, snap?.epoch, snapshotReady]) // eslint-disable-line react-hooks/exhaustive-deps
  const policy = matchNarrationPolicy(snap?.speed ?? 2)

  // ---------------------------------------------------------------- accessibility: new insights
  // Only insights that arrive while watching are news. On load, a new match, a reset or a seek, the existing
  // insights are marked as seen so nothing is read out as a backlog.
  const seenInsights = useRef<{ key: string; ids: Set<string> } | null>(null)
  useEffect(() => {
    if (!snap || !snapshotReady) return
    const key = `${snap.matchId}:${snap.epoch}`
    if (seenInsights.current?.key !== key) {
      seenInsights.current = { key, ids: new Set(snap.insights.map(i => i.id)) }
      a11y.speech.resetKeys('insight:')
      a11y.speech.clearSource('match')   // a seek, reset or new match: anything said about the old position is stale
      return
    }
    const seen = seenInsights.current.ids
    const fresh = snap.insights.filter(i => !seen.has(i.id))
    if (fresh.length === 0) return
    for (const i of fresh) seen.add(i.id)
    // A burst (fast playback) is trimmed to the most important few; the queue discards anything that goes stale.
    const ranked = [...fresh].sort((a, b) => priorityOf(b) - priorityOf(a) || b.importance - a.importance).slice(0, 3)
    a11y.cue(cueFor(ranked[0].type))
    if (prefs.spokenInsights || prefs.audioMatchMode) {
      // Faster playback says less, never later: the speed policy caps detail and drops lower-priority insights.
      const detail = capDetail(prefs.speechDetail, policy.detailCap)
      for (const card of ranked) {
        if (!shouldSpeak(card, detail) || priorityOf(card) < policy.minPriority) continue
        if (prefs.audioMatchMode && card.type === 'GOAL') continue // Audio Match Mode already announces goals with the score.
        a11y.say({
          text: insightSpeech(card, detail, prefs.simplified, as, teamName, opponentOf),
          priority: priorityOf(card), kind: 'insight', label: card.narrative.typeLabel, key: `insight:${card.id}`,
          clock: `${card.minute}'`, lang: voiceLang, source: 'match', at: card.second, cueClass: insightCueClass(card),
        })
      }
    } else {
      // Screen readers: one concise announcement; assertive only for critical moments.
      const top = ranked[0]
      a11y.announce(`${top.minute}' ${top.narrative.typeLabel}: ${top.narrative.headline}`, priorityOf(top) === 2)
    }
    if (prefs.periodicRecap === 'shifts' && policy.minPriority === 0 && ranked.some(c => SHIFT_TYPES.has(c.type) && c.severity === 'High')) {
      a11y.say({ text: matchStatus(snap, as), priority: 0, kind: 'recap', key: `recap:${key}:${snap.clock.minute}`, clock: snap.clock.label, lang: voiceLang, label: as.opt.periodicRecap[0], source: 'match', at: snap.clock.second })
    }
  }, [snap?.insights, snap?.matchId, snap?.epoch, snapshotReady]) // eslint-disable-line react-hooks/exhaustive-deps

  // A language switch changes the wording of insights already on screen without producing a new match event.
  // Answer that explicit viewer action once, after its localized snapshot arrives; never replay the entire feed.
  const narratedLanguage = useRef(profile.language)
  useEffect(() => {
    if (!snap || !snapshotReady || narratedLanguage.current === profile.language) return
    narratedLanguage.current = profile.language
    if (replayId || !speechEnabled(prefs)) return
    const recent = snap.insights.filter(i => i.second <= snap.clock.second && snap.clock.second - i.second <= 180)
      .sort((a, b) => b.second - a.second)
    const card = (prefs.spokenInsights || prefs.audioMatchMode) ? (
      recent.find(i => i.id === selected && shouldSpeak(i, prefs.speechDetail)) ??
      recent.find(i => shouldSpeak(i, prefs.speechDetail))
    ) : undefined
    if (card) {
      a11y.say({
        text: insightSpeech(card, capDetail(prefs.speechDetail, policy.detailCap), prefs.simplified, as, teamName, opponentOf),
        priority: 1, kind: 'insight', label: card.narrative.typeLabel,
        clock: `${card.minute}'`, lang: voiceLang, source: 'ui',
      }, { allowRepeat: true })
    } else if (prefs.audioMatchMode || prefs.spokenInsights) {
      a11y.say({ text: matchStatus(snap, as), priority: 1, kind: 'status',
        clock: snap.clock.label, lang: voiceLang, label: as.opt.audioMatchMode[0], source: 'ui',
      }, { allowRepeat: true })
    }
  }, [snap?.profile.language, profile.language, snap?.matchId, snap?.epoch, snapshotReady]) // eslint-disable-line react-hooks/exhaustive-deps

  // ---------------------------------------------------------------- accessibility: score, half-time, recaps
  const matchState = useRef<{ key: string; minute: number; home: number; away: number; finished: boolean } | null>(null)
  useEffect(() => {
    if (!snap || !snapshotReady) return
    const key = `${snap.matchId}:${snap.epoch}`
    const prev = matchState.current
    const now = { key, minute: snap.clock.minute, home: snap.score.home, away: snap.score.away, finished: snap.finished }
    matchState.current = now
    if (!prev || prev.key !== key) return
    const status = (k: string, text: string, priority: 0 | 1 | 2, kind = 'status', cueClass?: CueClass) => {
      if (priority < policy.minPriority) return
      a11y.say({ text, priority, kind, key: k, clock: snap.clock.label, lang: voiceLang, label: as.opt.audioMatchMode[0], source: 'match', at: snap.clock.second, cueClass })
    }
    if (now.home + now.away > prev.home + prev.away) {
      const text = goalAnnouncement(snap, now.home > prev.home ? snap.home.id : snap.away.id, as)
      a11y.announce(text, true)
      if (prefs.audioMatchMode) status(`goal:${key}:${now.home}-${now.away}`, text, 2, 'status', 'goal')
    }
    if (!prev.finished && now.finished) {
      a11y.announce(as.status_.fulltime)
      if (prefs.audioMatchMode || prefs.periodicRecap !== 'off') status(`ft:${key}`, `${as.status_.fulltime} ${matchStatus(snap, as)}`, 2, 'status', 'boundary')
    } else if (prev.minute < 45 && now.minute >= 45) {
      if (prefs.audioMatchMode || prefs.periodicRecap !== 'off') status(`ht:${key}`, `${as.status_.halftime} ${matchStatus(snap, as)}`, 2, 'status', 'boundary')
    } else if (prefs.periodicRecap !== 'halftime' && periodicDue(prefs.periodicRecap, prev.minute, now.minute)) {
      a11y.speech.clearKind('recap') // an older recap still waiting is stale
      status(`recap:${key}:${now.minute}`, matchStatus(snap, as), 0, 'recap')
    }
  }, [snap, snapshotReady]) // eslint-disable-line react-hooks/exhaustive-deps

  const speakStatus = () => {
    if (snap && snapshotReady) a11y.say({ text: matchStatus(snap, as), priority: 1, kind: 'status', clock: snap.clock.label, lang: voiceLang, label: as.opt.audioMatchMode[0] }, { allowRepeat: true })
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
    snapshotReady && a11y.say({ text, priority: 1, kind, label, lang: voiceLang, clock: snap?.clock.label }, { allowRepeat: true })

  if (!snap) {
    return (
      <>
        <p className="empty" role="status">MatchShift · {connected ? '…' : s.connecting}</p>
      </>
    )
  }

  const focusCard = focus?.insightId ? snap.insights.find(i => i.id === focus.insightId) : undefined
  const focusCaption = focusCard ? `${focusCard.minute}' · ${focusCard.narrative.headline}` : focus?.caption

  return (
    <div className="app" aria-busy={!snapshotReady}>
      {/* Keyboard and screen-reader users can go straight to the insights, past the match chrome. */}
      <a className="skip-link" href="#tab-panel" onClick={(e) => {
        e.preventDefault()
        setTab('live')
        document.getElementById('tab-panel')?.focus()
      }}>{s.home.skip}</a>
      <header className="topbar">
        <div className="brand">
          <Logo />
          <h1 className="brand-name">MatchShift</h1>
          <span className="badge-synthetic" title={meta?.dataNotice}>{s.synthetic}</span>
          <span className="brand-tagline">{s.tagline}</span>
        </div>

        {/* Not a live region: the clock changes every second. Goals are announced through the shared announcer. */}
        <div className="scoreboard" role="group" aria-label={`${snap.home.shortName} ${snap.score.home}, ${snap.away.shortName} ${snap.score.away}, ${spokenClock(snap.clock.second, displayedLanguage)}`}>
          <span className="team-name"><span className="team-swatch" style={{ background: HOME_COLOR }} />{snap.home.shortName}</span>
          <span className="score">{snap.score.home} – {snap.score.away}</span>
          <span className="team-name">{snap.away.shortName}<span className="team-swatch swatch-away" style={{ background: AWAY_COLOR }} /></span>
          <span className="clock" aria-label={spokenClock(snap.clock.second, displayedLanguage)}>{snap.clock.label}</span>
        </div>

        <div className="controls-right">
          <div className="viewer-utilities">
            {!guide && <button className="chip guide-entry" onClick={() => goGuide(GUIDE_START)} title={`${s.home.tour} ${s.guide.entryHint}`}>{s.guide.entry}</button>}
            <A11yButton lang={displayedLanguage} onOpen={() => setCenterOpen(true)} highlight={guide === 'viewers'} />
            <label className={`control-label ${guide === 'viewers' ? 'guide-target' : ''}`}>{s.language}
              <select value={profile.language} onChange={(e) => update({ language: e.target.value as Lang })}>
                {(meta?.languages ?? [{ code: 'en', name: 'English' }]).map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
              </select>
            </label>
          </div>
          <div className="match-controls">
            <label className="control-label">{s.match}
              <select value={snap.matchId} onChange={(e) => { setFocus(null); setSelected(null); void api.reset(e.target.value) }}>
                {matches.map((m) => <option key={m.id} value={m.id}>{m.home.shortName} v {m.away.shortName}</option>)}
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
            <a className="broadcast-link" href={broadcastHref('broadcast', import.meta.env.BASE_URL, location.search, sessionId, displayedLanguage)} target="_blank" rel="noreferrer">{s.broadcast}</a>
          </div>
        </div>
      </header>

      <main className="main">
        <aside className="right">
          <div className="tabs" role="tablist" aria-label={s.home.navigation} onKeyDown={(e) => {
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
                className={`${tab === k ? 'active' : ''} ${guide === 'replay' && k === 'agents' ? 'guide-target' : ''}`} onClick={() => setTab(k)}
                title={k === 'agents' ? s.home.agentsHint : undefined} aria-description={k === 'agents' ? s.home.agentsHint : undefined}>
                {k === 'live' ? `${s.live} (${snap.insights.length})` : s[k]}
              </button>
            ))}
          </div>
          <div className="tab-body" role="tabpanel" id="tab-panel" aria-labelledby={`tab-${tab}`} tabIndex={-1}>
            {tab === 'live' && <FeedIntro s={s} onTrust={() => openVerification(selected ?? snap.insights[0]?.id)} />}
            {tab === 'live' && (snap.insights.length === 0
              ? <EmptyFeed s={s} playing={snap.isPlaying} finished={snap.finished} started={snap.clock.second > 0} onStart={() => void api.play()} onTour={guide ? undefined : () => goGuide(GUIDE_START)} />
              : snap.insights.map((card, index) => (
                <InsightCardView key={card.id} card={card} mode={profile.mode} s={s} selected={selected === card.id} teamName={teamName} latest={index === 0}
                  guideTarget={!!guide && card.id === flagship?.id} guideNext={card.id === flagship?.id ? guideNext : null}
                  simplified={prefs.simplified ? { text: simplifiedText(card, as, teamName, opponentOf), more: as.simplified.more, less: as.simplified.less } : null}
                  onSelect={() => setSelected(card.id)} onWhy={() => { setSelected(card.id); setWhy(card) }} onShow={() => void showInsight(card)} onReplay={() => openReplay(card.id)}
                  onAgents={() => openVerification(card.id)} />
              )))}
            {tab === 'highlights' && <HighlightsPanel snap={snap} s={s} onShow={(h) => void showHighlight(h)} onReplay={openReplay} />}
            {tab === 'recap' && <RecapPanel snap={snap} s={s} profile={displayedProfile} styles={meta?.recapStyles ?? ['Casual', 'Analyst', 'Broadcast', 'Social']} />}
            {tab === 'agents' && (() => {
              // The insight being explained, with the same evidence actions as its card (WHY? > SHOW ME > Evidence Replay).
              const card = snap.insights.find(i => i.id === selected)
              return <AgentWorkflowPanel key={`${snap.matchId}:${snap.epoch}`} insightId={selected} card={card} profile={{ ...streamProfile, language: displayedLanguage }} defaultAccessible s={s}
                onWhy={card ? () => setWhy(card) : undefined} onShow={card ? () => void showInsight(card) : undefined} onReplay={card ? () => openReplay(card.id) : undefined} />
            })()}
          </div>
        </aside>

        <div className="left">
          <div className="pitch-wrap" id="match-evidence" tabIndex={-1} role="region" aria-label={focus ? `${s.showMe}: ${focusCaption}` : `${snap.home.name} v ${snap.away.name}`}>
            <Pitch home={snap.home} away={snap.away} trail={snap.recentEvents} focus={focus?.events ?? null}
              focusTeamId={focus?.teamId} ariaLabel={focus ? (focusCaption ?? '') : `${snap.home.shortName} v ${snap.away.shortName}`} />
            {focus && (
              <div className="pitch-overlay-bar">
                <span className="pitch-caption"><strong>{s.showMe}:</strong> {focusCaption}</span>
                <span style={{ display: 'flex', gap: 6 }}>
                  {prefs.describeVisuals && (
                    <button className="chip" onClick={() => {
                      const text = describeFocus(focus.events, focus.teamId, snap.home.id, teamName, as)
                      setDescription(text)
                      readAloud(text, 'describe', as.describe.title)
                    }}>{as.describe.button}</button>
                  )}
                  {focus.insightId && <button className={`chip replay-btn ${guide === 'show' && focus.insightId === flagship?.id ? 'guide-target' : ''}`} onClick={() => openReplay(focus.insightId!)}>{s.replay.button}</button>}
                  <button className="primary" onClick={() => {
                    setFocus(null)
                    setTab('live')
                    requestAnimationFrame(() => (document.getElementById(`card-${selected}`) ?? document.getElementById('tab-panel'))?.focus())
                  }}>{s.backToLive}</button>
                </span>
              </div>
            )}
            <div className="pitch-legend">
              <span className="legend-key"><i style={{ background: HOME_COLOR }} />{snap.home.name}</span>
              <span className="legend-key"><i className="swatch-away" style={{ background: AWAY_COLOR }} />{snap.away.name}</span>
              {focus && (() => {
                const split = splitEvidence(focus.events)
                const sup = split.supporting
                return <span>{s.showingEvents}: {evidenceLegend(split, s.supportingLegend, s.contextLegend)}
                  {sup.length > 0 && <> · <span className="mono">{sup[0].clock}–{sup[sup.length - 1].clock}</span></>}</span>
              })()}
            </div>
            {description && focus && <p className="pitch-description"><strong>{as.describe.title}:</strong> {description}</p>}
          </div>
          {snap.focusedPlayer && <PlayerPanel player={snap.focusedPlayer} lang={displayedLanguage}
            name={players.find(p => p.id === snap.focusedPlayer?.playerId)?.name ?? snap.focusedPlayer.playerId} />}
          <MetricsStrip snap={snap} s={s} />
        </div>
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
          <WhyPanel card={card} s={s} guideNext={guide === 'why' && card.id === flagship?.id} onClose={() => setWhy(null)} onShow={() => void showInsight(why)} onReplay={() => openReplay(why.id)}
            onAgents={() => openVerification(why.id)}
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
            lang={displayedLanguage} onClose={() => setReplayId(null)} />
        </Suspense>
      )}

      {guide && <JudgeGuide step={guide} s={s} lang={displayedLanguage} onGo={goGuide} onExit={() => setGuide(null)} onOpenA11y={() => setCenterOpen(true)} />}
      <SpeechBar lang={displayedLanguage} overReplay={!!replayId} onStatus={prefs.audioMatchMode ? speakStatus : undefined} />
      {centerOpen && <AccessibilityCenter lang={displayedLanguage} onClose={() => setCenterOpen(false)} onKeyboardHelp={() => { setCenterOpen(false); setKeysOpen(true) }} />}
      {keysOpen && <KeyboardHelp lang={displayedLanguage} onClose={() => setKeysOpen(false)} />}
    </div>
  )
}
