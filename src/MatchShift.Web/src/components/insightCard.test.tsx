/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import type { EvidenceLine, InsightCard, InsightEvents, PitchEvent } from '../api'
import { t } from '../i18n'
import { LANGUAGE_CODES } from '../languages'
import { evidenceLegend, splitEvidence } from '../evidence'
import { givenFacts } from './agentExplain'
import { basisLines, confidenceScore, evidenceCounts } from './insightBasis'

// The insight card and WHY? for the flagship insight, rendered to static markup from the real deterministic values
// of MS-FLAGSHIP-001-I015 (as exported by MatchShift.DemoExport). No network, no Azure, no Foundry.

vi.stubGlobal('location', { search: '' })
let InsightCardView: typeof import('./InsightCard').InsightCardView
let WhyPanel: typeof import('./WhyPanel').WhyPanel
beforeAll(async () => {
  InsightCardView = (await import('./InsightCard')).InsightCardView
  WhyPanel = (await import('./WhyPanel')).WhyPanel
})

// The stylesheet as text (vitest blanks CSS imports, even ?raw ones).
const css = readFileSync(new URL('../index.css', import.meta.url), 'utf8')

const en = t('en')
const noop = () => undefined

const evidence: EvidenceLine[] = [
  { metric: 'RightAttackShare', label: 'Attacks down the right (share)', baseline: '31%', recent: '90%', change: '+59 pts', threshold: '25 pts',
    baselineWindow: "0'-50'", recentWindow: "50'-62'", sampleSize: 10, minimumSample: 8, sampleRequirementMet: true, isPrimary: true, comparison: 'last 12 vs match so far' },
  { metric: 'RightAttacks', label: 'Attacks down the right', recent: '9', recentWindow: "50'-62'", sampleSize: 10, sampleRequirementMet: true, isPrimary: false },
  { metric: 'ShiftStrengthZ', label: 'Statistical strength (z-score)', recent: '3.3', threshold: '2.7', recentWindow: "50'-62'", sampleSize: 10, sampleRequirementMet: true, isPrimary: false },
  { metric: 'KeyPlayerInvolvement', label: 'Key player involvement', recent: '8', recentWindow: "50'-62'", sampleSize: 30, sampleRequirementMet: true, isPrimary: false },
]
const supportingIds = Array.from({ length: 30 }, (_, i) => `MS-FLAGSHIP-001-E${String(842 + i).padStart(5, '0')}`)
const CONTEXT = 'MS-FLAGSHIP-001-E00797'

const flagship = (patch: Partial<InsightCard> = {}, lang = en): InsightCard => ({
  id: 'MS-FLAGSHIP-001-I015', type: 'ATTACK_CHANNEL_SHIFT', minute: 63, second: 3780, teamId: 'WHA', severity: 'High', confidence: 'High', confidenceScore: 0.95,
  importance: 84, span: { fromSecond: 3000, toSecond: 3720 }, visuals: [{ key: 'shiftedAttacks', style: 'Routes', eventIds: supportingIds, emphasised: true }, { key: 'otherAttacks', style: 'Routes', eventIds: [CONTEXT], emphasised: false }],
  supportingEventCount: 30, contextEventIds: [CONTEXT],
  narrative: {
    insightId: 'MS-FLAGSHIP-001-I015', language: 'en', mode: 'Analyst', typeLabel: 'Match shift', headline: 'Westhaven switch their attack down the right',
    fact: "90% of Westhaven's attacks in the 50'-62' spell went down the right, up from 31% earlier in the match.",
    inference: 'Their attacking play has shifted decisively towards the right flank.',
    explanation: "The change follows Tavian Brooks's introduction, which may point to a deliberate adjustment - but the data cannot confirm intent.",
    ticker: '', studioLine: '', confidenceLabel: lang === en ? 'High' : 'H', severityLabel: 'Major', source: 'template', evidence,
  },
  ...patch,
})

const cardHtml = (card = flagship(), s = en, extra: object = {}) => renderToStaticMarkup(createElement(InsightCardView, {
  card, mode: 'Analyst', s, selected: false, teamName: () => 'Westhaven', onWhy: noop, onShow: noop, onReplay: noop, onSelect: noop, onAgents: noop, ...extra,
}))
const whyHtml = (card = flagship(), s = en) => renderToStaticMarkup(createElement(WhyPanel, { card, s, onClose: noop, onShow: noop, onReplay: noop, onAgents: noop }))
const text = (html: string) => html.replace(/<[^>]+>/g, ' ').replace(/&#x27;/g, "'").replace(/\s+/g, ' ')

function expectOrder(html: string, needles: string[]) {
  let at = -1
  for (const n of needles) {
    const i = html.indexOf(n, at + 1)
    expect(i, `"${n}" after position ${at}`).toBeGreaterThan(at)
    at = i
  }
}

describe('insight card: what changed, why it matters, how sure, what proves it, how to check', () => {
  it('reads in the order minute, headline, key evidence, why it matters, confidence, evidence count, actions', () => {
    const out = text(cardHtml())
    expectOrder(out, ["63'", 'Westhaven switch their attack down the right', 'Key evidence:', '31%', '90%', 'Attacks down the right (share)',
      'Why it matters', 'Their attacking play has shifted decisively towards the right flank.', 'Confidence: High', '30 supporting events',
      'Why?', 'Show me', 'Evidence replay', 'How was this verified?'])
  })

  it('stays compact: no speculation, no repeated fact sentence, no internal wording, each value once', () => {
    const out = text(cardHtml())
    expect(out).not.toContain('Tavian Brooks')                         // the possible explanation lives in WHY?
    expect(out).not.toContain("90% of Westhaven's attacks")            // the fact sentence would repeat the key number
    expect(out).not.toContain('Deterministic MatchShift wording')
    expect(out.match(/Confidence/g)).toHaveLength(1)
    expect(out.match(/30 supporting events/g)).toHaveLength(1)
    expect(out.match(/31%/g)).toHaveLength(1)
    expect(out).not.toMatch(/\b31 (supporting )?events\b/)
  })

  it('shows the fact sentence when there is no key number to show', () => {
    const noBaseline = flagship({ narrative: { ...flagship().narrative, evidence: evidence.map(e => ({ ...e, baseline: undefined })) } })
    expect(text(cardHtml(noBaseline))).toContain("90% of Westhaven's attacks")
  })

  it('keeps WHY? and SHOW ME primary; Evidence replay and the trust link are secondary', () => {
    const out = cardHtml()
    expect(out).toMatch(/<button class="chip chip-primary ">Why\?<\/button>/)
    expect(out).toMatch(/<button class="chip chip-primary ">Show me<\/button>/)
    expect(out).toMatch(/<button class="ghost replay-link ">Evidence replay<\/button>/)
    expect(out).toContain('<button class="ghost trust-link">How was this verified?</button>')
    expect(cardHtml(flagship(), en, { onAgents: undefined })).not.toContain('How was this verified?')
  })
})

describe('WHY?: why this was flagged, then what it may mean', () => {
  it('answers "why is MatchShift telling me this?" from the deterministic evidence', () => {
    const out = text(whyHtml())
    expectOrder(out, ['Why this was flagged', 'Deterministic facts', 'Attacks down the right (share)', '31%', '90%', '+59 pts',
      'The change (+59 pts) is bigger than the detection threshold (25 pts).',
      'Enough data: 10 in the recent sample (at least 8 needed).',
      'Unlikely to be chance: statistical strength 3.3 (the bar is 2.7).',
      'Confidence', 'High', '(score 0.95)', 'High means a score of 0.90 or more.',
      'Importance', '84 / 100', 'Evidence', '30 supporting events', '+ 1 context event: shown grey for comparison, not counted as evidence',
      'What this may mean', 'Fact', "90% of Westhaven's attacks", 'Inference', 'Their attacking play', 'Possible explanation', 'not confirmed by the data', 'Tavian Brooks',
      'All measurements', 'Show me', 'Evidence replay', 'How was this verified?', 'Close'])
  })

  it('never lets speculation look like fact: three labelled claims, separated by text, shape and border style', () => {
    const out = whyHtml()
    expectOrder(out, ['<div class="claim claim-fact"><dt><span aria-hidden="true">● </span>Fact',
      '<div class="claim claim-inference"><dt><span aria-hidden="true">◐ </span>Inference',
      '<div class="claim claim-explanation"><dt><span aria-hidden="true">○ </span>Possible explanation'])
    expect(css).toMatch(/\.claim-fact \{ border-style: solid;/)
    expect(css).toMatch(/\.claim-inference \{ border-style: dashed; \}/)
    expect(css).toMatch(/\.claim-explanation \{ border-style: dotted; \}/)
    const noExplanation = flagship({ narrative: { ...flagship().narrative, explanation: undefined } })
    expect(whyHtml(noExplanation)).not.toContain('Possible explanation')
  })

  it('every flagged line comes from the evidence: values change with it and missing values are not invented', () => {
    const p = { ...evidence[0], sampleSize: 6, sampleRequirementMet: false }
    const small = flagship({ narrative: { ...flagship().narrative, evidence: [p] } })
    expect(basisLines(small, en)).toEqual(['The change (+59 pts) is bigger than the detection threshold (25 pts).', 'Small sample: 6 (at least 8 needed).'])
    const bare = flagship({ narrative: { ...flagship().narrative, evidence: [{ ...evidence[0], threshold: undefined, minimumSample: undefined }] } })
    expect(basisLines(bare, en)).toEqual(['Sample size: 10.'])
  })

  it('explains confidence with the real level scale (Statistics.LevelOf)', () => {
    expect(confidenceScore(flagship(), en)).toBe('score 0.95')
    expect(confidenceScore(flagship(), t('fr'))).toBe('indice 0,95')
    const medium = text(whyHtml(flagship({ confidence: 'Medium', confidenceScore: 0.8 })))
    expect(medium).toContain('(score 0.80)')
    expect(medium).toContain('Medium means a score from 0.75 to 0.89.')
  })

  it('keeps every measurement, collapsed', () => {
    const out = whyHtml()
    expect(out).toMatch(/<details class="why-all"><summary>All measurements<\/summary><table class="evidence">/)
    for (const label of ['Statistical strength (z-score)', 'Key player involvement', 'last 12 vs match so far', 'Deterministic MatchShift wording']) expect(out).toContain(label)
  })
})

describe('evidence counts stay consistent: 30 supporting, 1 context (context is not evidence)', () => {
  const ev = (id: string): PitchEvent => ({ id, seq: 0, second: 3000, clock: '55:00', type: 'PASS', outcome: 'Complete', teamId: 'WHA' })
  const showMe: InsightEvents = { insightId: 'MS-FLAGSHIP-001-I015', layers: [], events: [ev(CONTEXT), ...supportingIds.map(ev)], supportingEventIds: supportingIds, contextEventIds: [CONTEXT] }

  it('card, WHY?, SHOW ME and the Agents page agree', () => {
    const card = flagship()
    const split = splitEvidence(showMe)
    expect(evidenceCounts(card)).toEqual({ supporting: 30, context: 1 })
    expect([split.supporting.length, split.context.length]).toEqual([30, 1])
    expect(split.supporting.map(e => e.id)).not.toContain(CONTEXT)
    expect(split.context.map(e => e.id)).toEqual([CONTEXT])
    const agents = givenFacts(card)
    expect([agents.supporting, agents.context]).toEqual([30, 1])
    expect(evidenceLegend(split, en.supportingLegend, en.contextLegend)).toBe('30 supporting events + 1 context (grey, not counted as evidence)')
    expect(text(cardHtml(card))).toContain('30 supporting events')
    expect(text(whyHtml(card))).toContain('30 supporting events + 1 context event: shown grey for comparison, not counted as evidence')
  })
})

describe('six languages', () => {
  it('renders the card and WHY? from localized strings in every language', () => {
    for (const lang of LANGUAGE_CODES) {
      const s = t(lang)
      const card = text(cardHtml(flagship({}, s), s))
      const why = text(whyHtml(flagship({}, s), s))
      for (const k of ['whyMatters', 'howVerified'] as const) expect(card, `${lang}.${k}`).toContain(s.insight[k])
      for (const k of ['flaggedTitle', 'meaningTitle', 'fact', 'inference', 'explanation', 'explanationHint', 'allMeasurements', 'confidenceBasis', 'importanceBasis'] as const)
        expect(why, `${lang}.${k}`).toContain(s.insight[k])
      if (lang !== 'en') {
        expect(why).not.toContain('Why this was flagged')
        expect(why).not.toContain('not confirmed by the data')
        expect(card).not.toContain('Why it matters')
      }
    }
  })
})

describe('accessibility semantics', () => {
  it('card: labelled article, heading, spoken key evidence, labelled action group, real buttons', () => {
    const out = cardHtml()
    expect(out).toContain('aria-labelledby="card-h-MS-FLAGSHIP-001-I015"')
    expect(out).toContain('<h3 class="card-headline" id="card-h-MS-FLAGSHIP-001-I015">')
    expect(out).toContain('<span class="sr-only">Key evidence: </span>')
    expect(out).toContain('<span class="arrow" aria-hidden="true">→</span><span class="sr-only"> → </span>')
    expect(out).toContain('role="group" aria-label="Challenge this insight"')
  })

  it('WHY?: a labelled modal dialog with h2 then two h3 sections, decorative glyphs hidden', () => {
    const out = whyHtml()
    expect(out).toContain('role="dialog" aria-modal="true" aria-labelledby="why-title"')
    expect(out.match(/<h2/g)).toHaveLength(1)
    expect(out.match(/<h3/g)).toHaveLength(2)
    expect(out).toContain('<section aria-labelledby="why-flagged">')
    expect(out).toContain('<section aria-labelledby="why-meaning">')
    expect(out).not.toMatch(/<dt>[●◐○]/)                               // the claim type is always a word, not just a shape
  })
})

describe('narrow layout', () => {
  it('wraps the actions and meta row, and gives phones one scrolling column with no fixed minimum width', () => {
    expect(css).toMatch(/\.card-actions \{ display: flex; flex-wrap: wrap;/)
    expect(css).toMatch(/\.card-meta \{ display: flex; flex-wrap: wrap;/)
    expect(css).toMatch(/@media \(max-width: 600px\) \{[^}]*\.app \{ height: auto;[^]*?\.main \{ grid-template-columns: minmax\(0, 1fr\);/)
    expect(css).toMatch(/@media \(max-width: 560px\) \{[^]*?\.why-facts \{ grid-template-columns: 1fr; \}/)
  })
})
