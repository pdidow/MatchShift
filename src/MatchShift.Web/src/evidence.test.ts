import { describe, expect, it } from 'vitest'
import type { InsightEvents, PitchEvent } from './api'
import { evidenceLegend, splitEvidence } from './evidence'
import { FLAGSHIP, findFlagship } from './guide/guide'
import { t } from './i18n'

const ev = (n: number): PitchEvent => ({ id: `MS-FLAGSHIP-001-E${String(n).padStart(5, '0')}`, seq: n, second: 3000 + n, clock: '55:00', type: 'Pass', outcome: 'Complete', teamId: 'WHA' } as PitchEvent)

// Shape of the real flagship SHOW ME payload: 30 supporting events plus E00797, the one recent attack outside the right channel.
const supporting = Array.from({ length: 30 }, (_, i) => ev(842 + i))
const context = ev(797)
const flagshipFocus: InsightEvents = {
  insightId: 'MS-FLAGSHIP-001-I015',
  layers: [
    { key: 'channel', style: 'Channels', eventIds: supporting.map(e => e.id), emphasised: true },
    { key: 'otherAttacks', style: 'Routes', eventIds: [context.id], emphasised: false },
  ],
  events: [context, ...supporting],
  supportingEventIds: supporting.map(e => e.id),
  contextEventIds: [context.id],
}

describe('SHOW ME evidence split', () => {
  it('counts only authoritative supporting events, and the context event separately', () => {
    const split = splitEvidence(flagshipFocus)
    expect(split.supporting).toHaveLength(30)
    expect(split.context.map(e => e.id)).toEqual(['MS-FLAGSHIP-001-E00797'])
    expect(split.supporting.map(e => e.id)).not.toContain('MS-FLAGSHIP-001-E00797')
    expect(split.duplicates).toEqual([])
  })

  it('says so in the legend, in every language', () => {
    const split = splitEvidence(flagshipFocus)
    const en = t('en')
    expect(evidenceLegend(split, en.supportingLegend, en.contextLegend)).toBe('30 supporting events + 1 context (grey, not counted as evidence)')
    expect(evidenceLegend(split, t('fr').supportingLegend, t('fr').contextLegend)).toMatch(/^30 .* \+ 1 /)
    expect(evidenceLegend(split, t('es').supportingLegend, t('es').contextLegend)).toMatch(/^30 .* \+ 1 /)
    expect(evidenceLegend({ ...split, context: [] }, en.supportingLegend, en.contextLegend)).toBe('30 supporting events')
  })

  it('never counts an event as context when it is also supporting, and reports duplicates', () => {
    const split = splitEvidence({ ...flagshipFocus, events: [...flagshipFocus.events, supporting[0]], contextEventIds: [context.id, supporting[0].id] })
    expect(split.context.map(e => e.id)).toEqual([context.id])
    expect(split.duplicates).toEqual([supporting[0].id])
  })

  it('treats older payloads without the split as all supporting (never invents context)', () => {
    const legacy: InsightEvents = { insightId: 'x', layers: [], events: supporting }
    const split = splitEvidence(legacy)
    expect(split.supporting).toHaveLength(30)
    expect(split.context).toHaveLength(0)
  })

  it('the guided demo still lands on the same flagship insight', () => {
    expect(findFlagship(FLAGSHIP.matchId, [{ id: 'MS-FLAGSHIP-001-I015', type: 'ATTACK_CHANNEL_SHIFT', minute: 63 } as never])?.id).toBe('MS-FLAGSHIP-001-I015')
  })

  it('the guide no longer implies every drawn arrow is evidence', () => {
    for (const lang of ['en', 'fr', 'es'] as const) expect(t(lang).guide.steps.show.body).toMatch(/grey|grises/i)
  })
})
