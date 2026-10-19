import { beforeAll, describe, expect, it, vi } from 'vitest'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import type { InsightCard, WorkflowExecution } from '../api'
import { t } from '../i18n'

// api.ts reads the browser location at import time; components are rendered to static markup (no network, no Azure).
vi.stubGlobal('location', { search: '' })

let JudgeGuide: typeof import('./JudgeGuide').JudgeGuide
let InsightCardView: typeof import('../components/InsightCard').InsightCardView
let AgentWorkflow: typeof import('../components/AgentWorkflow')

beforeAll(async () => {
  JudgeGuide = (await import('./JudgeGuide')).JudgeGuide
  InsightCardView = (await import('../components/InsightCard')).InsightCardView
  AgentWorkflow = await import('../components/AgentWorkflow')
})

const en = t('en')
const html = (el: ReturnType<typeof createElement>) => renderToStaticMarkup(el)

const flagshipCard = {
  id: 'MS-FLAGSHIP-001-I015', type: 'ATTACK_CHANNEL_SHIFT', minute: 63, second: 3720, teamId: 'WHA', severity: 'High', confidence: 'High', confidenceScore: 0.95,
  importance: 84, span: { fromSecond: 3000, toSecond: 3720 }, visuals: [], supportingEventCount: 30,
  narrative: {
    insightId: 'I015', language: 'en', mode: 'Analyst', typeLabel: 'Match shift', headline: 'Westhaven switch their attack down the right',
    fact: '90% of attacks went down the right', inference: 'Shifted right', ticker: '', studioLine: '', confidenceLabel: 'High', severityLabel: 'High',
    source: 'template', evidence: [],
  },
} as unknown as InsightCard

const noop = () => undefined

describe('guided demo panel', () => {
  it('is a labelled region with a polite live title, a step counter and an exit', () => {
    const out = html(createElement(JudgeGuide, { step: 'why', s: en, onGo: noop, onExit: noop }))
    expect(out).toContain('role="region"')
    expect(out).toContain('aria-label="Guided demo"')
    expect(out).toContain('aria-live="polite"')
    expect(out).toContain('Step 3 of 7')
    expect(out).toContain('aria-label="Exit demo"')
    expect(out).toContain('SHOW ME →')                                            // the next real action
    expect(out).toContain('>Back<')
  })

  it('starts without Back and ends with Finish', () => {
    const intro = html(createElement(JudgeGuide, { step: 'intro', s: en, onGo: noop, onExit: noop }))
    expect(intro).not.toContain('>Back<')
    expect(intro).toContain('Jump to the flagship moment')
    const insight = html(createElement(JudgeGuide, { step: 'insight', s: en, onGo: noop, onExit: noop }))
    expect(insight).not.toContain('>Back<')                                       // nothing to go back to before the jump
    const last = html(createElement(JudgeGuide, { step: 'trust', s: en, onGo: noop, onExit: noop }))
    expect(last).toContain('>Finish<')
    expect(last).toContain('Step 7 of 7')
  })

  it('separates deterministic facts, AI interpretation and verification once, on the insight step', () => {
    const out = html(createElement(JudgeGuide, { step: 'insight', s: en, onGo: noop, onExit: noop }))
    for (const k of ['Deterministic facts', 'AI interpretation', 'Verification']) expect(out).toContain(k)
    expect(html(createElement(JudgeGuide, { step: 'why', s: en, onGo: noop, onExit: noop }))).not.toContain('AI interpretation')
  })

  it('renders in French and Spanish from the localization tables', () => {
    expect(html(createElement(JudgeGuide, { step: 'why', s: t('fr'), onGo: noop, onExit: noop }))).toContain('Étape 3 sur 7')
    expect(html(createElement(JudgeGuide, { step: 'why', s: t('es'), onGo: noop, onExit: noop }))).toContain('Paso 3 de 7')
  })
})

describe('insight card evidence chain', () => {
  const render = (extra: object = {}) => html(createElement(InsightCardView, {
    card: flagshipCard, mode: 'Analyst', s: en, selected: false, teamName: () => 'Westhaven', onWhy: noop, onShow: noop, onReplay: noop, onSelect: noop, ...extra,
  }))

  it('keeps WHY? > SHOW ME > Evidence replay as one labelled group, in order', () => {
    const out = render()
    expect(out).toContain('role="group" aria-label="Challenge this insight"')
    const why = out.indexOf('>Why?<'), show = out.indexOf('>Show me<'), replay = out.indexOf('>Evidence replay<')
    expect(why).toBeGreaterThan(0)
    expect(show).toBeGreaterThan(why)
    expect(replay).toBeGreaterThan(show)
    expect(out).not.toContain('3D replay')
    expect(out).toContain('id="card-MS-FLAGSHIP-001-I015"')
    expect(out).not.toContain('guide-')                                            // normal use: no guide styling
  })

  it('marks the flagship card and the next action only during the guide', () => {
    const out = render({ guideTarget: true, guideNext: 'show' })
    expect(out).toContain('guide-card')
    expect(out).toMatch(/class="chip guide-target"[^>]*>Show me</)
  })
})

describe('verification and evaluation presentation', () => {
  const x = (mode: WorkflowExecution['executionMode'], outcome: WorkflowExecution['outcome'], verified: boolean) => ({
    workflowExecutionId: 'wf', insightId: 'I', matchId: 'M', executionMode: mode, provider: 'p', orchestrator: 'o', status: 'COMPLETED', startedAt: '',
    steps: [], handoffs: [], frameworkSupersteps: 0, frameworkExecutorInvocations: 0, modelCalls: 0, modelCallLimit: 8, inputTokens: 0, outputTokens: 0,
    outcome, verified, authoritativeFactsUnchanged: true, recoveryActions: [],
  }) as WorkflowExecution

  it('shows VERIFIED prominently only for a verified live result', () => {
    const live = html(createElement(AgentWorkflow.VerdictBanner, { x: x('LIVE_FOUNDRY', 'APPROVED', true), s: en }))
    expect(live).toContain('✓ Verified')
    expect(live).toContain('role="status"')
    const changes = html(createElement(AgentWorkflow.VerdictBanner, { x: x('LIVE_FOUNDRY', 'APPROVED_WITH_CHANGES', true), s: en }))
    expect(changes).toContain('Verified with changes')
    const rejected = html(createElement(AgentWorkflow.VerdictBanner, { x: x('LIVE_FOUNDRY', 'REJECTED', false), s: en }))
    expect(rejected).toContain('Rejected')
    expect(rejected).not.toContain('✓')
    const offline = html(createElement(AgentWorkflow.VerdictBanner, { x: x('OFFLINE_TEMPLATE', 'DETERMINISTIC_ONLY', false), s: en }))
    expect(offline).toContain('No AI output to verify')
    expect(offline).not.toMatch(/✓|>Verified/)
  })

  it('shows the Foundry Evaluations proof factually, including what generic scoring missed', () => {
    const out = html(createElement(AgentWorkflow.EvalProof, { s: en }))
    expect(out).toContain('Quality checked with Microsoft Foundry Evaluations')
    expect(out).toContain('28/28')
    expect(out).toContain('11/11')
    expect(out).toContain('5/11')
    expect(out).toContain('caught fewer defects')
    expect(out).not.toMatch(/PitchLens/i)
  })

  it('the Agents panel before any data shows no LIVE claim, but always the evaluation proof', () => {
    const out = html(createElement(AgentWorkflow.AgentWorkflowPanel, { insightId: null, profile: { mode: 'Analyst', language: 'en' }, defaultAccessible: false, s: en }))
    expect(out).not.toContain('LIVE ·')
    expect(out).toContain('Select an insight card')
    expect(out).toContain('id="eval-proof"')
  })
})
