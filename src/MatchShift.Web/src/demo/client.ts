import type { InsightEvents, Lang, Profile, RecapResponse, RecapStyle, Snapshot, WorkflowExecution } from '../api'
import { AgentRefusal } from '../agentRefusal'
import { asRecorded, recordedStatus, type AgentRecordings } from './recorded'
import type { ReplaySequence } from '../replay/types'
import { snapshotAt, type DemoCore, type DemoIndex, type DemoLanguage, type Playback } from './data'

export function createDemoClient(sessionId: string) {
  const params = new URLSearchParams(location.search)
  const requestedMinute = Number(params.get('minute') ?? 63)
  const state: Playback = { matchId: params.get('match') ?? '', second: Number.isFinite(requestedMinute) ? Math.max(0, Math.min(5400, requestedMinute * 60)) : 3780,
    playing: false, speed: 2, epoch: 0 }
  const storageKey = `matchshift.demo.${sessionId}`
  const cache = new Map<string, Promise<unknown>>()
  let wallTime = Date.now()
  let revision = 0
  let timer: number | undefined
  type Listener = { profile: Profile; snapshot: (s: Snapshot) => void; connection: (ok: boolean) => void; request: number }
  const listeners = new Set<Listener>()

  function read<T>(path: string): Promise<T> {
    let result = cache.get(path)
    if (!result) {
      result = fetch(`${import.meta.env.BASE_URL}demo/${path}`).then(async r => {
        if (!r.ok) throw new Error(`Demo data could not load (${r.status}). Please reload to try again.`)
        return r.json() as Promise<T>
      }).catch(error => { cache.delete(path); throw error })
      cache.set(path, result)
      // Keep recent match/language packs, without retaining every replay opened in a long session.
      if (cache.size > 24) cache.delete(cache.keys().next().value!)
    }
    return result as Promise<T>
  }
  const index = () => read<DemoIndex>('index.json')
  const agentRecordings = () => read<AgentRecordings>('agent-recordings.json').catch(() => null)
  async function ready() {
    const manifest = await index()
    if (manifest.schemaVersion !== 1) throw new Error('Please reload to update the demo.')
    if (!manifest.matches.some(m => m.id === state.matchId)) state.matchId = manifest.defaultMatchId
  }
  const coreFor = (matchId: string) => read<DemoCore>(`${encodeURIComponent(matchId)}/core.json`)
  const languageFor = (matchId: string, lang: Lang) => read<DemoLanguage>(`${encodeURIComponent(matchId)}/${lang}.json`)
  function advance() {
    const now = Date.now()
    if (state.playing) state.second = Math.min(5400, state.second + Math.max(0, now - wallTime) / 1000 * 30 * state.speed)
    if (state.second >= 5400) state.playing = false
    wallTime = now
  }
  function restore(value: string | null) {
    try {
      const saved = JSON.parse(value ?? 'null') as (Playback & { wallTime: number }) | null
      if (!saved || typeof saved.matchId !== 'string' || !Number.isFinite(saved.second) || ![1, 2, 5, 10].includes(saved.speed) ||
          typeof saved.playing !== 'boolean' || !Number.isFinite(saved.epoch) || !Number.isFinite(saved.wallTime)) return
      Object.assign(state, saved, { second: Math.max(0, Math.min(5400, saved.second)) })
      wallTime = saved.wallTime
      advance()
      revision++
    } catch { /* storage unavailable or old data */ }
  }
  // A broadcast tab shares only this session's playback; unrelated visitors stay independent.
  if (!params.has('minute') && !params.has('match')) {
    try { restore(localStorage.getItem(storageKey)) } catch { /* storage is optional */ }
  }
  function onStorage(event: StorageEvent) {
    if (event.key === storageKey) { restore(event.newValue); emit() }
  }
  function save() {
    try { localStorage.setItem(storageKey, JSON.stringify({ ...state, wallTime })) } catch { /* storage is optional */ }
  }
  async function snapshot(profile: Profile) {
    await ready()
    const matchId = state.matchId, version = revision
    const [core, words] = await Promise.all([coreFor(matchId), languageFor(matchId, profile.language)])
    if (version !== revision || matchId !== state.matchId) return null
    advance()
    return snapshotAt(core, words, state, profile, sessionId)
  }
  function emit() {
    for (const listener of listeners) {
      const request = ++listener.request
      void snapshot(listener.profile).then(snap => {
        if (snap && listeners.has(listener) && listener.request === request) { listener.snapshot(snap); listener.connection(true) }
      }).catch(() => { if (listeners.has(listener) && listener.request === request) listener.connection(false) })
    }
  }
  async function change(action: () => void) {
    await ready()
    advance()
    action()
    revision++
    save()
    emit()
  }
  async function active() { await ready(); return coreFor(state.matchId) }
  async function events(id: string, highlight: boolean): Promise<InsightEvents> {
    const core = await active()
    const ids = (highlight ? core.highlightEvents : core.insightEvents)[id]
    if (!ids) throw new Error('This moment is unavailable in the selected match.')
    const wanted = new Set(ids)
    const words = await languageFor(core.detail.summary.id, 'en')
    return { insightId: id, layers: highlight ? [] : words.cards.find(c => c.id === id)?.visuals ?? [],
      events: core.events.filter(e => wanted.has(e.id)),
      supportingEventIds: highlight ? ids : core.insightSupportingEvents?.[id],
      contextEventIds: highlight ? [] : core.insightContextEvents?.[id] }
  }
  return {
    meta: async () => (await index()).meta,
    matches: async () => (await index()).matches,
    match: async (id: string) => (await coreFor(id)).detail,
    play: () => change(() => { if (state.second >= 5400) { state.second = 0; state.epoch++ } state.playing = true }),
    pause: () => change(() => { state.playing = false }),
    reset: (id?: string) => change(() => { if (id) state.matchId = id; state.second = 0; state.playing = false; state.epoch++ }),
    speed: (speed: number) => change(() => { if ([1, 2, 5, 10].includes(speed)) state.speed = speed }),
    seek: (minute: number) => change(() => { if (Number.isFinite(minute)) { state.second = Math.max(0, Math.min(5400, Math.round(minute * 60))); state.epoch++ } }),
    insightEvents: (id: string) => events(id, false),
    highlightEvents: (id: string) => events(id, true),
    replay: async (id: string) => { await ready(); return read<ReplaySequence>(`${encodeURIComponent(state.matchId)}/replay/${encodeURIComponent(id)}.json`) },
    recap: async (period: 'halftime' | 'fulltime', style: RecapStyle, p: Profile): Promise<RecapResponse | null> => {
      await ready(); advance()
      if (state.second < (period === 'halftime' ? 2699 : 5399)) return null
      const recaps = await read<Record<string, RecapResponse>>(`${encodeURIComponent(state.matchId)}/recaps-${p.language}.json`)
      return recaps[`${period}|${style}|${p.focusTeamId ?? ''}`] ?? recaps[`${period}|${style}|`] ?? null
    },
    agentTrace: async (id: string, _p: Profile, _run = true): Promise<WorkflowExecution | null> => {
      const recordings = await agentRecordings()
      const x = recordings?.executions[id]
      if (!x) throw new AgentRefusal('No recorded workflow is available for this insight.')
      return asRecorded(x, recordings.recordedAt)
    },
    agentStatus: async () => recordedStatus(await agentRecordings()),
    streamUrl: (_p: Profile) => '',
    subscribe: (profile: Profile, onSnapshot: (s: Snapshot) => void, onConnection: (ok: boolean) => void) => {
      const listener: Listener = { profile, snapshot: onSnapshot, connection: onConnection, request: 0 }
      listeners.add(listener)
      if (timer === undefined) {
        window.addEventListener('storage', onStorage)
        timer = window.setInterval(emit, 400)
      }
      emit()
      return () => {
        listeners.delete(listener)
        if (!listeners.size) { clearInterval(timer); timer = undefined; window.removeEventListener('storage', onStorage) }
      }
    },
  }
}
