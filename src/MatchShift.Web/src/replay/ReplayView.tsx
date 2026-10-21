import { useEffect, useMemo, useRef, useState } from 'react'
import { autoCrowdTier, loadCrowdChoice, readCrowdCapabilities, resolveCrowdTier, saveCrowdChoice } from './crowd'
import type { CrowdCapabilities, CrowdChoice, CrowdTier } from './crowd'
import { CrowdSoundPlayer, browserAudioContext, crowdSoundAllowed, crowdSoundStep, duckFactor, loadCrowdSound, saveCrowdSound } from './crowdAudio'
import { replayCuePriority, replayNarrationPolicy } from '../a11y/narrationPolicy'
import type { InsightCard, Lang, Team } from '../api'
import { api } from '../api'
import type { Strings } from '../i18n'
import { isTypingTarget, useA11y, useDialog } from '../a11y/A11yContext'
import { describeReplayEvent } from '../a11y/describe'
import { a11yStrings, fill, speechLang } from '../a11y/strings'
import { ReplayScene } from './scene'
import type { ReplayTick } from './scene'
import { currentEvent, formatTime, layout, locate, matchClock, stepEvent } from './timeline'
import type { CameraPreset, ReplayEvent, ReplaySequence } from './types'

interface Props {
  insightId: string
  card?: InsightCard
  home: Team
  away: Team
  s: Strings
  lang: Lang
  onClose: () => void
}

const SPEEDS = [0.5, 1, 1.5, 2]
const PRESETS: CameraPreset[] = ['tactical', 'sideline', 'behindGoal', 'player']
const INTRO_KEY = 'matchshift.replay.introSeen'

const cssVar = (name: string, fallback: string) =>
  getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback

/** Full-screen 3D tactical replay of the sequence behind one MatchShift insight. */
export default function ReplayView({ insightId, card, home, away, s, lang, onClose }: Props) {
  const r = s.replay
  const { prefs, update, reducedMotion, speech, say } = useA11y()
  const as = a11yStrings(lang)
  const audioDescription = prefs.replayAudioDescription
  const [showList, setShowList] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  useDialog(rootRef, onClose)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const hostRef = useRef<HTMLDivElement>(null)
  const sceneRef = useRef<ReplayScene | null>(null)
  const [seq, setSeq] = useState<ReplaySequence | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState<ReplayTick | null>(null)
  const [speed, setSpeed] = useState(1)
  const [titleClip, setTitleClip] = useState<number | null>(null)
  // Optional decorative crowd: default from device capability, an explicit choice is remembered, a slow device reduces it.
  const crowdChoice = useRef<CrowdChoice | null>(loadCrowdChoice())
  const crowdEnv = useRef<{ auto: CrowdTier; caps: CrowdCapabilities }>({ auto: 'off', caps: { webgl: false } })
  const [crowdTier, setCrowdTier] = useState<CrowdTier>('off')
  /** Set when the frame-time guard reduced the crowd: to LOW ("reduced") or OFF ("disabled"). */
  const [crowdReduced, setCrowdReduced] = useState<'low' | 'off' | null>(null)

  useEffect(() => {
    let cancelled = false
    setSeq(null)
    setError(null)
    api.replay(insightId).then(d => { if (!cancelled) setSeq(d) }).catch((e: Error) => { if (!cancelled) setError(e.message) })
    return () => { cancelled = true }
  }, [insightId])

  // Create the WebGL scene once data is here; dispose it completely on close.
  useEffect(() => {
    if (!seq || !canvasRef.current) return
    const scene = new ReplayScene(canvasRef.current, {
      home: cssVar('--series-home', '#4c7bf4'),
      away: cssVar('--series-away', '#e0623b'),
      homeKeeper: '#2fbf71',
      awayKeeper: '#e8c547',
      accent: cssVar('--accent', '#39e6b0'),
    }, { reducedMotion, steady: prefs.reduceFlashing, shapes: prefs.colorVision !== 'default' })
    sceneRef.current = scene
    const caps = readCrowdCapabilities(scene.capabilities())
    crowdEnv.current = { auto: autoCrowdTier(caps), caps }
    const initialCrowd = resolveCrowdTier(crowdEnv.current.auto, crowdChoice.current, caps, false)
    scene.setCrowd(initialCrowd, crowdChoice.current === null ? 'auto' : 'explicit')   // an explicit choice gets a lenient guard
    setCrowdTier(initialCrowd)
    setCrowdReduced(null)
    const offCrowd = scene.onCrowdAutoChange(tier => { setCrowdTier(tier); setCrowdReduced(tier === 'off' ? 'off' : 'low') })
    // Measurement aid for local checks only (?replayDebug=1): read-only crowd and draw-call figures.
    if (new URLSearchParams(window.location.search).get('replayDebug') === '1') (window as unknown as { __replayStats?: () => unknown }).__replayStats = () => scene.crowdStats()
    let introSeen = false
    try { introSeen = localStorage.getItem(INTRO_KEY) === '1'; localStorage.setItem(INTRO_KEY, '1') } catch { /* storage unavailable */ }
    // URL options for demos and screenshots: flyin=0|1, replayAt=<seconds>, replayCam=<preset>, replayPause=1.
    const q = new URLSearchParams(window.location.search)
    const flyIn = q.get('flyin') === '1' || (q.get('flyin') !== '0' && !introSeen && !q.has('replayAt'))
    const off = scene.onTick(setTick)
    scene.load(seq, flyIn)
    const cam = q.get('replayCam') as CameraPreset | null
    if (cam && PRESETS.includes(cam)) scene.setCamera(cam)
    const at = Number(q.get('replayAt'))
    if (q.has('replayAt') && Number.isFinite(at)) { scene.seek(at); scene.skipIntro() }
    if (q.get('replayPause') === '1') scene.pause()
    const observer = new ResizeObserver(() => scene.resize())
    if (hostRef.current) observer.observe(hostRef.current)
    return () => {
      off()
      offCrowd()
      observer.disconnect()
      scene.dispose()
      sceneRef.current = null
    }
  }, [seq]) // eslint-disable-line react-hooks/exhaustive-deps -- motion options are applied live below

  useEffect(() => { sceneRef.current?.setMotion({ reducedMotion, steady: prefs.reduceFlashing }) }, [reducedMotion, prefs.reduceFlashing, seq])

  const timeline = useMemo(() => (seq ? layout(seq) : null), [seq])

  // ---------------------------------------------------------------- crowd sound (optional atmosphere, never information)
  // Plays only while the crowd is shown and the viewer wants it; follows the replay clock; ducks under any narration.
  const [crowdSound, setCrowdSound] = useState(loadCrowdSound)
  const crowdPlayer = useRef<CrowdSoundPlayer | null>(null)
  crowdPlayer.current ??= new CrowdSoundPlayer({
    makeContext: browserAudioContext,
    load: file => fetch(`${import.meta.env.BASE_URL}audio/${file}`).then(res => { if (!res.ok) throw new Error(file); return res.arrayBuffer() }),
  })
  const soundAllowed = crowdSoundAllowed(crowdTier !== 'off', crowdSound)
  const crowdEvents = useMemo(() => timeline?.placed.flatMap(p => p.clip.events.map(e => ({ label: e.label, at: p.offset + e.time }))) ?? [], [timeline])
  useEffect(() => {
    if (soundAllowed && seq) void crowdPlayer.current?.prepare()
    else crowdPlayer.current?.stop()
  }, [soundAllowed, seq])
  const lastCrowdSound = useRef({ time: -1, clip: -1, jumps: 0 })
  useEffect(() => {
    if (!tick) return
    const prev = lastCrowdSound.current
    lastCrowdSound.current = { time: tick.time, clip: tick.clipIndex, jumps: tick.jumps }
    const step = crowdSoundStep(prev, { time: tick.time, clip: tick.clipIndex, playing: tick.playing, introActive: tick.introActive, jumps: tick.jumps }, crowdEvents)
    if (step.stop) crowdPlayer.current?.stop()
    if (step.play && soundAllowed) crowdPlayer.current?.play(step.play.clip, step.play.gain)
  }, [tick]) // eslint-disable-line react-hooks/exhaustive-deps
  // Narration is primary: while it speaks the crowd drops to a fifth of its level. Crowd audio never calls speech itself.
  useEffect(() => speech.subscribe(state => crowdPlayer.current?.setDuck(duckFactor(state.speaking, audioDescription))), [speech, audioDescription])
  useEffect(() => () => crowdPlayer.current?.stop(), [])
  const toggleCrowdSound = () => {
    const next = !crowdSound
    setCrowdSound(next)
    saveCrowdSound(next)
    if (!next) crowdPlayer.current?.stop()
  }

  const toggleCrowd = () => {
    const next: CrowdChoice = crowdTier === 'off' ? 'on' : 'off'
    crowdChoice.current = next
    saveCrowdChoice(next)
    const tier = resolveCrowdTier(crowdEnv.current.auto, next, crowdEnv.current.caps, false)
    sceneRef.current?.setCrowd(tier, 'explicit')
    setCrowdTier(tier)
    setCrowdReduced(null)
  }

  // Title card ("BEFORE SHIFT" / "AFTER SHIFT") whenever a clip starts.
  const clipIndex = tick?.clipIndex ?? -1
  useEffect(() => {
    if (clipIndex < 0 || !seq || seq.clips.length < 2) return
    setTitleClip(clipIndex)
    const id = window.setTimeout(() => setTitleClip(null), 1800)
    return () => window.clearTimeout(id)
  }, [clipIndex, seq])

  // Keyboard: Space play/pause, arrows step events, Home restart, C/1-4 cameras, D audio description,
  // R repeat description, L event list, Esc closes. Keys never fire while typing or with modifier keys.
  const keyState = useRef<{ timeline: typeof timeline; tick: ReplayTick | null }>({ timeline, tick })
  const actions = useRef({ cycleCamera: () => {}, toggleAd: () => {}, repeat: () => {} })
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const scene = sceneRef.current
      if (e.key === 'Escape') { onClose(); return }
      if (e.ctrlKey || e.metaKey || e.altKey || isTypingTarget(e.target)) return
      const { timeline: tl, tick: tk } = keyState.current
      if (!scene || !tl) return
      const target = e.target as HTMLElement | null
      const onButton = target?.tagName === 'BUTTON'
      const onSlider = target?.getAttribute?.('role') === 'slider'
      const key = e.key.length === 1 ? e.key.toLowerCase() : e.key
      if (key === ' ') { if (onButton) return; e.preventDefault(); scene.toggle() }
      else if (key === 'ArrowRight' && !onSlider) { e.preventDefault(); scene.seek(stepEvent(tl.placed, tk?.time ?? 0, 1)) }
      else if (key === 'ArrowLeft' && !onSlider) { e.preventDefault(); scene.seek(stepEvent(tl.placed, tk?.time ?? 0, -1)) }
      else if (key === 'Home') { e.preventDefault(); scene.restart() }
      else if (key === 'c') actions.current.cycleCamera()
      else if (key === 'd') actions.current.toggleAd()
      else if (key === 'r') actions.current.repeat()
      else if (key === 'l') setShowList(v => !v)
      else if (['1', '2', '3', '4'].includes(key)) scene.setCamera(PRESETS[Number(key) - 1])
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const located = timeline && tick ? locate(timeline.placed, tick.time) : null
  const clip = located?.placed.clip
  const local = located?.local ?? 0
  const event = clip ? currentEvent(clip, local) : null
  const player = (id?: string) => seq?.players.find(p => p.id === id)
  const score = clip ? liveScore(clip.scoreAtStart, clip.events, local, seq!.homeTeamId) : null
  const clipLabel = (key?: string) => (key === 'before' ? r.before : key === 'after' ? r.after : r.key)
  const overlayChips = clip ? clip.overlays.filter(o => o.kind === 'Channel').map(o => channelText(o.label, r)) : []

  const describe = (e: ReplayEvent) => {
    const actor = player(e.playerId)
    const target = player(e.relatedPlayerId)
    const label = r.events[e.label] ?? e.label.replace(/_/g, ' ').toLowerCase()
    if (e.type === 'Pass' && e.outcome === 'Complete' && actor && target) return `${label} · #${actor.shirtNumber} → #${target.shirtNumber} ${target.shortName}`
    return actor ? `${label} · #${actor.shirtNumber} ${actor.shortName}` : label
  }

  // ---------------------------------------------------------------- audio description (optional, off by default)
  const describeEvent = (e: ReplayEvent) => (seq ? describeReplayEvent(e, seq.players, seq.homeTeamId, as) : null)
  const adLang = speechLang(lang)
  const lastAd = useRef<{ time: number; clip: number }>({ time: -1, clip: -1 })
  useEffect(() => {
    if (!tick) return
    const prev = lastAd.current
    lastAd.current = { time: tick.time, clip: tick.clipIndex }
    // The replay clock is authoritative for replay narration: cues are judged against it, never against wall-clock time.
    // A paused replay starts no new cues (a sentence already speaking finishes, unless a seek or restart makes it stale).
    speech.setTimeline('replay', tick.time, tick.playing && !tick.introActive)
    if (!audioDescription || !seq || !timeline || tick.introActive) return
    // A seek or restart makes current and queued narration stale: drop it and describe from the new position only.
    const jumped = tick.time < prev.time - 1e-3 || tick.time - prev.time > 1.5
    if (jumped) speech.clearKind('replay')
    if (!tick.playing && !jumped) return
    const minPriority = replayNarrationPolicy(speed).minPriority
    const placed = timeline.placed[tick.clipIndex]
    if (placed && tick.clipIndex !== prev.clip && seq.clips.length > 1) {
      // BEFORE / AFTER transitions are critical: they frame the evidence.
      say({ text: fill(as.ad.clip, { label: clipLabel(placed.clip.key), clock: placed.clip.events[0]?.clock ?? '' }), kind: 'replay', source: 'replay',
        priority: 2, at: placed.offset, lang: adLang, label: r.title })
    }
    if (jumped) {
      // After a seek, describe only the event now on screen - never the skipped ones.
      if (event && placed) {
        const text = describeEvent(event)
        if (text) say({ text, kind: 'replay', source: 'replay', priority: 1, at: placed.offset + event.time, lang: adLang, clock: event.clock, label: r.title })
      }
      return
    }
    for (const p of timeline.placed) {
      for (const e of p.clip.events) {
        const at = p.offset + e.time
        if (at <= prev.time || at > tick.time) continue
        if (e.type === 'Carry' && !e.isEvidence && !e.isContext) continue
        const priority = replayCuePriority(e)
        if (priority < minPriority) continue   // faster replay: supporting evidence, goals and transitions only
        const text = describeEvent(e)
        // Bounded and timeline-tied: one cue may wait; a newer event replaces it; anything the replay has passed is dropped.
        if (text) say({ text, kind: 'replay', source: 'replay', priority, at, lang: adLang, clock: e.clock, label: r.title })
      }
    }
  }, [tick, audioDescription]) // eslint-disable-line react-hooks/exhaustive-deps

  // Stop narration when the replay closes.
  useEffect(() => () => speech.clearKind('replay'), [speech])

  const toggleAudioDescription = () => {
    const next = !audioDescription
    update({ replayAudioDescription: next })
    speech.clearKind('replay')
    speech.say({ text: `${as.replay.audioDescription}: ${next ? as.replay.on : as.replay.off}`, kind: 'status', lang: adLang, priority: 1 }, { allowRepeat: true })
  }
  const repeatDescription = () => {
    if (!event) return
    const text = describeEvent(event) ?? describe(event)
    speech.say({ text, kind: 'replay', lang: adLang, priority: 1, clock: event.clock, label: r.title }, { allowRepeat: true })
  }
  const cycleCamera = () => {
    const current = tick?.userCamera ? -1 : PRESETS.indexOf(tick?.preset ?? 'tactical')
    sceneRef.current?.setCamera(PRESETS[(current + 1) % PRESETS.length])
  }
  keyState.current = { timeline, tick }
  actions.current = { cycleCamera, toggleAd: toggleAudioDescription, repeat: repeatDescription }

  return (
    <div className="replay-root" role="dialog" aria-modal="true" aria-label={r.title} ref={rootRef}>
      <div className="replay-stage" ref={hostRef}>
        {seq && <canvas ref={canvasRef} className="replay-canvas" />}
        {!seq && !error && <div className="replay-status">{r.loading}</div>}
        {error && <div className="replay-status">{r.error}: {error}</div>}

        {seq && tick && clip && (
          <>
            <div className="replay-top">
              <div className="replay-score">
                <span className="swatch" style={{ background: 'var(--series-home)' }} />{home.shortName}
                <strong>{score?.home} – {score?.away}</strong>
                {away.shortName}<span className="swatch" style={{ background: 'var(--series-away)' }} />
                <span className="replay-clock">{matchClock(clip, local)}</span>
              </div>
              <div className="replay-insight">
                <span className="card-kicker">{r.title} · {card?.narrative.typeLabel ?? seq.insightType}</span>
                <strong>{card?.narrative.headline ?? seq.insightType}</strong>
                <div className="replay-chips">
                  <span className="replay-chip clip">{clipLabel(clip.key)}{seq.clips.length > 1 ? ` · ${r.clipOf} ${clipIndex + 1}/${seq.clips.length}` : ''}</span>
                  {overlayChips.map(c => <span key={c} className="replay-chip">{c}</span>)}
                </div>
              </div>
              <button className="ghost replay-close" onClick={onClose} aria-label={r.close}>✕</button>
            </div>

            {titleClip !== null && <div className="replay-title-card" key={titleClip}>{clipLabel(seq.clips[titleClip].key)}</div>}
            {tick.introActive && <button className="replay-skip" onClick={() => sceneRef.current?.skipIntro()}>{r.skipIntro}</button>}

            {/* Not a live region: it changes several times a second. Screen readers use the event list or audio description. */}
            <div className={`replay-event ${event?.isEvidence ? 'evidence' : ''}`}>
              <span className="replay-event-kicker">{r.currentEvent}</span>
              {event ? <><span className="replay-event-clock">{event.clock}</span> {describe(event)}</> : <span className="muted">—</span>}
            </div>

            {showList && timeline && (
              <nav className="replay-event-list" aria-label={as.replay.listTitle}>
                <h2 className="panel-title">{as.replay.listTitle}</h2>
                <ol>
                  {timeline.placed.map(p => (
                    <li key={p.index}>
                      {seq.clips.length > 1 && <span className="replay-list-clip">{clipLabel(p.clip.key)}</span>}
                      <ol>
                        {p.clip.events.filter(e => e.type !== 'Carry' || e.isEvidence || e.isContext).map(e => {
                          const isCurrent = event?.eventId === e.eventId && clip === p.clip
                          return (
                            <li key={e.eventId}>
                              <button className={`replay-list-item ${e.isEvidence ? 'evidence' : e.isContext ? 'context' : ''}`} aria-current={isCurrent ? 'step' : undefined}
                                onClick={() => sceneRef.current?.seek(p.offset + e.time)}>
                                <span className="replay-event-clock">{e.clock}</span>
                                <span>{describeEvent(e) ?? describe(e)}{e.isContext && <small className="context-tag"> · {s.contextTag}</small>}</span>
                              </button>
                            </li>
                          )
                        })}
                      </ol>
                    </li>
                  ))}
                </ol>
              </nav>
            )}

            <div className="replay-legend">
              <span>{r.legend}:</span>
              <span><i className="ring actual" />{r.actual}</span>
              <span><i className="ring interpolated" />{r.interpolated}</span>
              <span><i className="ring estimated" />{r.estimated}</span>
            </div>
          </>
        )}
      </div>

      {seq && timeline && tick && (
        <div className="replay-controls">
          <div className="replay-transport">
            <button onClick={() => sceneRef.current?.restart()} aria-label={r.restart} title={r.restart}>⟲</button>
            <button onClick={() => sceneRef.current?.seek(stepEvent(timeline.placed, tick.time, -1))} aria-label={r.prev} title={r.prev}>⏮</button>
            <button className="primary replay-play" onClick={() => sceneRef.current?.toggle()} aria-label={tick.playing ? r.pause : r.play}>
              {tick.playing ? '❚❚' : '▶'}
            </button>
            <button onClick={() => sceneRef.current?.seek(stepEvent(timeline.placed, tick.time, 1))} aria-label={r.next} title={r.next}>⏭</button>
            <span className="replay-time">{formatTime(tick.time)} / {formatTime(tick.duration)}</span>
          </div>

          <Scrubber label={r.timeline} seq={seq} total={timeline.total} time={tick.time} placed={timeline.placed}
            onSeek={(t) => sceneRef.current?.seek(t)} describe={describe} />

          <div className="replay-options">
            <div className="replay-group" role="group" aria-label={s.speed}>
              {SPEEDS.map(sp => (
                <button key={sp} className={`chip ${speed === sp ? 'active' : ''}`} aria-pressed={speed === sp}
                  onClick={() => { setSpeed(sp); sceneRef.current?.setSpeed(sp) }}>{sp}×</button>
              ))}
            </div>
            <div className="replay-group" role="group" aria-label={as.replay.audioDescription}>
              <button className={`chip ${audioDescription ? 'active' : ''}`} aria-pressed={audioDescription} onClick={toggleAudioDescription}
                title={`${as.replay.audioDescription} (D)`}>{as.replay.audioDescription}</button>
              {audioDescription && <button className="chip" onClick={repeatDescription} disabled={!event} title="R">{as.replay.repeat}</button>}
              <button className={`chip ${showList ? 'active' : ''}`} aria-pressed={showList} onClick={() => setShowList(v => !v)}
                title="L">{as.replay.eventList}</button>
            </div>
            <div className="replay-group" role="group" aria-label={r.crowd}>
              <button className={`chip ${crowdTier !== 'off' ? 'active' : ''}`} aria-pressed={crowdTier !== 'off'} onClick={toggleCrowd}
                disabled={!crowdEnv.current.caps.webgl}>{r.crowd}: {crowdTier !== 'off' ? r.crowdOn : r.crowdOff}</button>
              {/* Kept as a preference while the crowd is off (nothing plays then); restored when the crowd comes back. */}
              <button className={`chip ${soundAllowed ? 'active' : ''}`} aria-pressed={crowdSound} onClick={toggleCrowdSound}
                disabled={crowdTier === 'off'} title={crowdTier === 'off' ? r.crowdSoundNeedsCrowd : undefined}>
                {r.crowdSound}: {crowdSound ? r.crowdOn : r.crowdOff}</button>
            </div>
            <div className="replay-group" role="group" aria-label={r.camera}>
              {PRESETS.map(p => (
                <button key={p} className={`chip ${!tick.userCamera && tick.preset === p ? 'active' : ''}`} aria-pressed={!tick.userCamera && tick.preset === p}
                  onClick={() => sceneRef.current?.setCamera(p)}>{r[p]}</button>
              ))}
              <button className="chip" onClick={() => sceneRef.current?.resetCamera()} disabled={!tick.userCamera}
                title={r.drag}>{tick.userCamera ? r.resetCamera : r.freeCamera}</button>
            </div>
          </div>
          {crowdReduced && <p className="replay-crowd-note" role="status">{crowdReduced === 'off' ? r.crowdAutoOff : r.crowdAutoLow}</p>}
          <p className="replay-notice">{r.notice}</p>
        </div>
      )}
    </div>
  )
}

function Scrubber({ seq, total, time, placed, onSeek, describe, label }: {
  seq: ReplaySequence
  total: number
  time: number
  placed: ReturnType<typeof layout>['placed']
  onSeek: (t: number) => void
  describe: (e: ReplayEvent) => string
  label: string
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [dragging, setDragging] = useState(false)
  const seekAt = (clientX: number) => {
    const rect = ref.current!.getBoundingClientRect()
    onSeek(Math.min(1, Math.max(0, (clientX - rect.left) / rect.width)) * total)
  }
  const pct = (t: number) => `${(t / Math.max(total, 0.001)) * 100}%`
  return (
    <div className="replay-scrubber" ref={ref} role="slider" tabIndex={0} aria-label={label} aria-valuemin={0} aria-valuemax={Math.round(total)} aria-valuenow={Math.round(time)}
      aria-valuetext={`${formatTime(time)} / ${formatTime(total)}`}
      onKeyDown={(e) => {
        if (e.key === 'ArrowRight' || e.key === 'ArrowUp') { e.preventDefault(); onSeek(Math.min(total, time + 1)) }
        else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') { e.preventDefault(); onSeek(Math.max(0, time - 1)) }
        else if (e.key === 'End') { e.preventDefault(); onSeek(total) }
      }}
      onPointerDown={(e) => { setDragging(true); (e.target as HTMLElement).setPointerCapture?.(e.pointerId); seekAt(e.clientX) }}
      onPointerMove={(e) => { if (dragging) seekAt(e.clientX) }}
      onPointerUp={() => setDragging(false)}>
      <div className="track" />
      <div className="fill" style={{ width: pct(time) }} />
      {placed.slice(1).map(p => <div key={p.index} className="clip-boundary" style={{ left: pct(p.offset) }} />)}
      {placed.flatMap(p => p.clip.events.filter(e => e.type !== 'Carry').map(e => (
        <div key={`${p.index}:${e.eventId}`} title={`${e.clock} ${describe(e)}`}
          className={`tick ${e.isEvidence ? 'evidence' : e.isContext ? 'context' : ''} ${e.label === 'GOAL' ? 'goal' : ''} ${seq.relevantPlayerIds.includes(e.playerId ?? '') ? 'relevant' : ''}`}
          style={{ left: pct(p.offset + e.time) }} />
      )))}
      <div className="head" style={{ left: pct(time) }} />
    </div>
  )
}

function liveScore(start: { home: number; away: number }, events: ReplayEvent[], t: number, homeId: string) {
  let { home, away } = start
  for (const e of events) {
    if (e.label !== 'GOAL' || e.endTime > t) continue
    if (e.teamId === homeId) home++; else away++
  }
  return { home, away }
}

function channelText(label: string | undefined, r: Strings['replay']) {
  const [kind, channel] = (label ?? '').split(':')
  if (kind === 'INSIGHT_CHANNEL') return channel === 'Left' ? r.channelLeft : channel === 'Right' ? r.channelRight : r.channelCentre
  return channel === 'Left' ? r.routeLeft : channel === 'Right' ? r.routeRight : r.routeCentre
}
