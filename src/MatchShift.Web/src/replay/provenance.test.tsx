import { describe, expect, it } from 'vitest'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { t } from '../i18n'
import { LANGUAGE_CODES } from '../languages'
import { coverage, eventRole, percent, positionShare } from './provenance'
import { AboutReplay, EventStatus, EvidenceSummary, RoleTag } from './ReplayTrust'
import { ballAt, cameraPose, currentEvent, layout, locate, samplePlayer, stepEvent } from './timeline'
import type { CameraPreset, Provenance, ReplayClip, ReplayEvent, ReplaySequence } from './types'

// Evidence Replay trust layer, from a sequence shaped like the flagship's (MS-FLAGSHIP-001-I015): 30 supporting events,
// 1 context event; a BEFORE clip of earlier play with no evidence and an AFTER clip showing 5 supporting events. One
// context event is placed in the AFTER clip here so its badge can be tested. No network, no Azure, no Foundry.

const en = t('en')
const id = (n: number) => `MS-FLAGSHIP-001-E${String(n).padStart(5, '0')}`
const SUPPORTING = Array.from({ length: 30 }, (_, i) => id(842 + i))
const CONTEXT = id(797)

const ev = (eventId: string, time: number, extra: Partial<ReplayEvent> = {}): ReplayEvent => ({
  eventId, sequence: time, time, endTime: time + 1, matchSecond: 3400 + time, clock: `56:${String(40 + time).padStart(2, '0')}`, type: 'Pass', outcome: 'Complete',
  label: 'PASS', teamId: 'WHA', x: 50, y: 50, isEvidence: SUPPORTING.includes(eventId), isContext: eventId === CONTEXT, ...extra,
})
const samples = (provenance: Provenance[]) => provenance.map((p, i) => ({ t: i * 0.5, x: 40 + i, y: 50, provenance: p }))
const clip = (key: ReplayClip['key'], events: ReplayEvent[], provenance: Provenance[]): ReplayClip => ({
  key, label: key, startSecond: 3400, endSecond: 3420, duration: 20, scoreAtStart: { home: 1, away: 1 }, events,
  tracks: [{ playerId: 'WHA-07', samples: samples(provenance) }],
  ball: [{ t0: 0, t1: 20, motion: 'Pass', x0: 40, y0: 50, x1: 60, y1: 50, z0: 0, z1: 0, arcHeight: 2, provenance: 'INTERPOLATED_POSITION' }],
  overlays: [], camera: { attackingTeamId: 'WHA', attackDirection: 1, centroidX: 50, centroidY: 50 },
})

const sequence = (): ReplaySequence => ({
  matchId: 'MS-FLAGSHIP-001', insightId: 'MS-FLAGSHIP-001-I015', insightType: 'ATTACK_CHANNEL_SHIFT', teamId: 'WHA', homeTeamId: 'NBR', awayTeamId: 'WHA',
  insightWindow: { fromSecond: 3000, toSecond: 3720 }, evidenceEventIds: [...SUPPORTING], contextEventIds: [CONTEXT], relevantPlayerIds: [], players: [],
  notice: '',
  clips: [
    clip('before', [ev(id(684), 1), ev(id(685), 4), ev(id(686), 8, { x: undefined, y: undefined })], ['ESTIMATED_POSITION', 'ESTIMATED_POSITION', 'ACTUAL_EVENT_POSITION', 'INTERPOLATED_POSITION']),
    clip('after', [ev(id(830), 1), ...SUPPORTING.slice(0, 5).map((x, i) => ev(x, 3 + i * 2)), ev(CONTEXT, 14), ev(id(880), 17)],
      ['ESTIMATED_POSITION', 'ESTIMATED_POSITION', 'ESTIMATED_POSITION', 'ACTUAL_EVENT_POSITION']),
  ],
})

/** Deep-freeze, so any mutation of the evidence during playback maths throws. */
function freeze<T>(o: T): T {
  if (o && typeof o === 'object') { Object.values(o).forEach(freeze); Object.freeze(o) }
  return o
}

describe('evidence and context counts', () => {
  it('keeps 30 supporting and 1 context, and reports what the clips show as a viewing window', () => {
    const c = coverage(sequence())
    expect([c.supporting, c.context]).toEqual([30, 1])
    expect([c.supportingShown, c.contextShown]).toEqual([5, 1])
    expect(c.clips.map(x => [x.key, x.supporting, x.context, x.other])).toEqual([['before', 0, 0, 3], ['after', 5, 1, 2]])
  })

  it('never classifies a context event as supporting, even if a flag says otherwise', () => {
    const seq = sequence()
    expect(eventRole(seq, ev(CONTEXT, 0))).toBe('context')
    expect(eventRole(seq, ev(CONTEXT, 0, { isEvidence: true }))).toBe('context')
    expect(eventRole(seq, ev(SUPPORTING[0], 0))).toBe('supporting')
    expect(eventRole(seq, ev(id(684), 0))).toBe('other')
    for (const c of seq.clips) for (const e of c.events) expect(eventRole(seq, e) === 'supporting').toBe(seq.evidenceEventIds.includes(e.eventId))
  })
})

describe('the role follows the event through stepping, seeking, restarting and camera changes', () => {
  const seq = sequence()
  const { placed, total } = layout(seq)
  const roleAt = (time: number) => {
    const { placed: p, local } = locate(placed, time)
    const e = currentEvent(p.clip, local)
    return e ? { id: e.eventId, role: eventRole(seq, e) } : null
  }
  const expected = (eventId: string) => (SUPPORTING.includes(eventId) ? 'supporting' : eventId === CONTEXT ? 'context' : 'other')

  it('stepping forward visits every event with the right role', () => {
    let time = 0
    const seen: string[] = []
    for (let i = 0; i < 20; i++) {
      const next = stepEvent(placed, time, 1)
      if (next === time) break
      time = next
      const r = roleAt(time)!
      expect(r.role, r.id).toBe(expected(r.id))
      seen.push(r.id)
    }
    expect(seen).toContain(CONTEXT)
    expect(seen.filter(x => SUPPORTING.includes(x))).toHaveLength(5)
  })

  it('seeking anywhere (forwards, backwards, onto the context event) never carries a badge over', () => {
    for (const time of [total - 1, 0.5, 20 + 14.2, 20 + 3.1, 5, 20 + 14.5, 20 + 1.2]) {
      const r = roleAt(time)
      if (r) expect(r.role, `${time}: ${r.id}`).toBe(expected(r.id))
    }
    expect(roleAt(20 + 14.2)).toEqual({ id: CONTEXT, role: 'context' })
    expect(roleAt(20 + 3.1)).toEqual({ id: SUPPORTING[0], role: 'supporting' })
  })

  it('restart goes back to the first event with its own role', () => {
    expect(roleAt(stepEvent(placed, 0, 1))).toEqual({ id: id(684), role: 'other' })
  })

  it('camera, playback maths and speed cannot change evidence or provenance', () => {
    const frozen = freeze(sequence())
    const before = JSON.stringify({ ids: frozen.evidenceEventIds, ctx: frozen.contextEventIds, cover: coverage(frozen), share: positionShare(frozen) })
    for (const c of frozen.clips) {
      for (let time = 0; time <= c.duration; time += 0.25) {
        const p = samplePlayer(c.tracks[0], time)
        ballAt(c, time, () => p)
        for (const preset of ['tactical', 'sideline', 'behindGoal', 'player'] as CameraPreset[]) cameraPose(preset, { x: 0, z: 0 }, 1, { x: p.x, z: p.y })
      }
    }
    expect(JSON.stringify({ ids: frozen.evidenceEventIds, ctx: frozen.contextEventIds, cover: coverage(frozen), share: positionShare(frozen) })).toBe(before)
  })
})

describe('position provenance comes from the replay data', () => {
  it('counts the server-labelled samples, and never hides a rare category', () => {
    const share = positionShare(sequence())
    expect(share).toEqual({ ACTUAL_EVENT_POSITION: 2 / 8, INTERPOLATED_POSITION: 1 / 8, ESTIMATED_POSITION: 5 / 8 })
    expect([percent(0.004), percent(0), percent(0.914)]).toEqual(['<1%', '0%', '91%'])
  })

  it('says whether the event itself carries a recorded position', () => {
    const at = (e: ReplayEvent) => renderToStaticMarkup(createElement(EventStatus, { event: e, role: 'other', s: en }))
    expect(at(ev(id(684), 1))).toContain('Position at this event: from event data')
    expect(at(ev(id(686), 8, { x: undefined, y: undefined }))).toContain('Position at this event: not recorded')
  })
})

describe('what the viewer reads', () => {
  const seq = sequence()
  const cover = coverage(seq)

  it('header: the same counts as card / WHY? / SHOW ME, the viewing window, and a comparison clip named as such', () => {
    const before = renderToStaticMarkup(createElement(EvidenceSummary, { cover, clipIndex: 0, s: en }))
    expect(before).toContain('Supporting evidence: 30 events')
    expect(before).toContain('Context: 1, not counted as evidence')
    expect(before).toContain('This replay shows 5 of the 30 supporting events.')
    expect(before).toContain('Comparison: earlier play, not evidence')
    const after = renderToStaticMarkup(createElement(EvidenceSummary, { cover, clipIndex: 1, s: en }))
    expect(after).toContain('5 supporting in this clip')
    const noContextShown = renderToStaticMarkup(createElement(EvidenceSummary, { cover: { ...cover, contextShown: 0 }, clipIndex: 1, s: en }))
    expect(noContextShown).toContain('Context is not in these clips (see SHOW ME).')
  })

  it('current event and list: the role in words, shape decoration hidden from screen readers', () => {
    const supporting = renderToStaticMarkup(createElement(EventStatus, { event: ev(SUPPORTING[0], 3), role: 'supporting', s: en }))
    expect(supporting).toContain('<span class="replay-role role-supporting"><span aria-hidden="true">■ </span>Supporting evidence</span>')
    expect(supporting).toContain('Recorded match event')
    expect(supporting).toContain('Movement between events is visualized (interpolated or estimated).')
    const context = renderToStaticMarkup(createElement(EventStatus, { event: ev(CONTEXT, 14), role: 'context', s: en }))
    expect(context).toContain('Context, not counted as evidence')
    expect(context).not.toContain('Supporting evidence')
    expect(renderToStaticMarkup(createElement(RoleTag, { role: 'other', s: en }))).toBe('')
    expect(renderToStaticMarkup(createElement(RoleTag, { role: 'context', s: en }))).toContain('Context, not counted as evidence')
  })

  it('persistent line and About: no new evidence, illustrative positioning, ball height, camera and crowd', () => {
    const out = renderToStaticMarkup(createElement(AboutReplay, { share: positionShare(seq), event: ev(CONTEXT, 14), role: 'context', s: en }))
    expect(out).toContain('<strong>Replay animation does not create new evidence.</strong>')
    expect(out).toContain('25% from event data, 13% interpolated, 63% estimated from formation. Illustrative positioning, not tracking data.')
    for (const k of ['aboutBall', 'aboutCamera', 'aboutCrowd'] as const) expect(out).toContain(en.replayUi[k].replace(/'/g, '&#x27;'))
    expect(out).toMatch(/<details class="replay-about-more"><summary>About this replay<\/summary>/)
    expect(out).toContain(`<code>${CONTEXT}</code>`)                      // technical detail is behind the disclosure
  })

  it('no replay wording claims exact tracking, observed movement or video', () => {
    for (const lang of LANGUAGE_CODES) {
      const s = t(lang)
      const all = JSON.stringify({ ...s.replayUi, notice: s.replay.notice, actual: s.replay.actual, interpolated: s.replay.interpolated, estimated: s.replay.estimated })
      expect(all, lang).not.toMatch(/\bexact|\bobserved (movement|trajector)|video footage/i)
    }
    // "tracking data" appears only as a denial.
    expect(JSON.stringify(en.replayUi).match(/tracking data/g)).toHaveLength(1)
    expect(en.replayUi.aboutShare).toContain('not tracking data')
    expect(en.replayUi.aboutCamera).toContain('not video')
  })
})

describe('six languages', () => {
  it('every language has the replay trust wording, and none falls back to English', () => {
    const keys = Object.keys(en.replayUi) as (keyof typeof en.replayUi)[]
    for (const lang of LANGUAGE_CODES) {
      const u = t(lang).replayUi
      expect(Object.keys(u).sort(), lang).toEqual([...keys].sort())
      expect(Object.keys(u.status).sort()).toEqual(['context', 'other', 'supporting'])
      if (lang !== 'en') for (const k of ['about', 'noNewEvidence', 'window', 'clipComparison', 'aboutShare'] as const) expect(u[k], `${lang}.${k}`).not.toBe(en.replayUi[k])
    }
    expect(t('pt').replayUi.recorded).toBe('Evento do jogo registado')    // European Portuguese
    expect(t('pt').replayUi.aboutCamera).toContain('câmara')
  })
})
