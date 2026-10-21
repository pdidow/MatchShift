import { beforeAll, describe, expect, it, vi } from 'vitest'
import { createElement, isValidElement } from 'react'
import type { ReactElement, ReactNode } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import type { AgentRuntimeStatus, ExecutionMode, InsightCard, TraceStep, WorkflowExecution, WorkflowOutcome } from '../api'
import { t } from '../i18n'
import { LANGUAGE_CODES } from '../languages'
import { asRecorded } from '../demo/recorded'
import { AI_STAGES, MODE_BADGE, countLabel, deterministicChecks, givenFacts, verifierState } from './agentExplain'

// The Agents tab ("How MatchShift built this insight") rendered to static markup from fixed executions: no network, no
// Azure, no Foundry. Every state on the page must come from the execution passed in.

vi.stubGlobal('location', { search: '' })
let AW: typeof import('./AgentWorkflow')
beforeAll(async () => { AW = await import('./AgentWorkflow') })

const en = t('en')

/** The flagship insight: MS-FLAGSHIP-001-I015, 63' Westhaven switch their attack down the right (30 supporting + 1 context). */
const flagship = {
  id: 'MS-FLAGSHIP-001-I015', type: 'ATTACK_CHANNEL_SHIFT', minute: 63, second: 3780, teamId: 'WHA', severity: 'High', confidence: 'High', confidenceScore: 0.95,
  importance: 84, span: { fromSecond: 3000, toSecond: 3720 }, visuals: [], supportingEventCount: 30, contextEventIds: ['MS-FLAGSHIP-001-E00797'],
  narrative: {
    insightId: 'MS-FLAGSHIP-001-I015', language: 'en', mode: 'Analyst', typeLabel: 'Match shift', headline: 'Westhaven switch their attack down the right',
    fact: 'Attacks down the right rose from 31% to 90%', inference: 'Shifted right', ticker: '', studioLine: '', confidenceLabel: 'High', severityLabel: 'High',
    source: 'template', evidence: [{ metric: 'rightShare', label: 'Right-channel attacks', baseline: '31%', recent: '90%', sampleSize: 30, sampleRequirementMet: true, isPrimary: true }],
  },
} as unknown as InsightCard

const ORDER = ['deterministic-grounding', 'tactical-analyst', 'audience-narrative', 'accessibility-narrator', 'fan-in', 'deterministic-validation', 'verification-agent', 'final-validation']

function step(sequence: number, stage: string, mode: ExecutionMode, extra: Partial<TraceStep> = {}): TraceStep {
  const ai = AI_STAGES.has(stage) && mode !== 'OFFLINE_TEMPLATE'
  return {
    sequence, stage, agentName: `MatchShift ${stage}`, responsibility: 'r', executionType: ai ? 'MICROSOFT_AGENT_FRAMEWORK' : 'DETERMINISTIC', executionMode: mode,
    provider: ai ? 'microsoft-foundry' : 'deterministic', modelDeployment: ai ? 'gpt-4.1-mini' : undefined, startedAt: '2026-10-19T12:00:00Z',
    completedAt: '2026-10-19T12:00:01Z', durationMs: 1000, superstep: sequence, status: 'COMPLETED', handoffFrom: [], handoffTo: [],
    evidenceIds: ['MS-FLAGSHIP-001-E00842'], evidenceCount: 30, inputSummary: 'in', outputSummary: 'out', modelCalls: ai ? 1 : 0, retryCount: 0,
    attemptFailures: [], inputTokens: 0, outputTokens: 0, findings: [], ...extra,
  }
}

function execution(mode: ExecutionMode, outcome: WorkflowOutcome, verified: boolean, stepExtra: Record<string, Partial<TraceStep>> = {}): WorkflowExecution {
  const offline = mode === 'OFFLINE_TEMPLATE'
  const steps = ORDER.map((stage, i) => step(i + 1, stage, mode, {
    ...(offline && AI_STAGES.has(stage) ? { status: stage === 'verification-agent' ? 'SKIPPED' : 'TEMPLATE' } : {}),
    ...(stage === 'verification-agent' && !offline ? { verificationDecision: outcome === 'REJECTED' ? 'REJECTED' : outcome } : {}),
    ...stepExtra[stage],
  }))
  return {
    workflowExecutionId: 'wf-test-0001', insightId: flagship.id, matchId: 'MS-FLAGSHIP-001', executionMode: mode, provider: offline ? 'offline-template' : 'microsoft-foundry',
    modelDeployment: offline ? undefined : 'gpt-4.1-mini', orchestrator: 'Microsoft Agent Framework Workflows', status: 'COMPLETED',
    startedAt: '2026-10-19T12:00:00Z', completedAt: '2026-10-19T12:00:09Z', durationMs: 9000, steps,
    handoffs: [
      { from: 'deterministic-grounding', to: ['tactical-analyst'], message: 'GroundedInsight', at: '' },
      { from: 'tactical-analyst', to: ['audience-narrative', 'accessibility-narrator'], message: 'TacticalHandoff (fan-out)', at: '' },
      { from: 'fan-in', to: ['deterministic-validation'], message: 'NarrativeBundle', at: '' },
    ],
    concurrency: offline ? undefined : { branchA: 'audience-narrative', branchB: 'accessibility-narrator', sameSuperstep: true, overlapped: true, overlapMs: 4584 },
    frameworkSupersteps: 7, frameworkExecutorInvocations: 9, modelCalls: offline ? 0 : 4, modelCallLimit: 8, inputTokens: 1, outputTokens: 1,
    outcome, verificationDecision: offline ? undefined : outcome, verified, authoritativeFingerprintBefore: '6E18', authoritativeFingerprintAfter: '6E18',
    authoritativeFactsUnchanged: true, recoveryActions: [],
  }
}

const runtime = (mode: ExecutionMode): AgentRuntimeStatus => ({
  runtime: { executionMode: mode, provider: 'p', modelDeployment: mode === 'LIVE_FOUNDRY' ? 'gpt-4.1-mini' : undefined, credential: 'ManagedIdentity', orchestrator: 'o', packages: {} },
  warmUp: 'done', agents: [],
})

const page = (x: WorkflowExecution | null, extra: Partial<import('./AgentWorkflow').ExplainerProps> = {}) =>
  renderToStaticMarkup(createElement(AW.AgentsExplainer, { insightId: flagship.id, card: flagship, x, status: runtime(x?.executionMode ?? 'LIVE_FOUNDRY'), s: en, ...extra }))

const approved = () => execution('LIVE_FOUNDRY', 'APPROVED', true)

/** Text order on the page: every needle must appear, in this order. */
function expectOrder(html: string, needles: string[]) {
  let at = -1
  for (const n of needles) {
    const i = html.indexOf(n, at + 1)
    expect(i, `"${n}" after position ${at}`).toBeGreaterThan(at)
    at = i
  }
}

describe('Agents tab: understand first', () => {
  it('leads with a user-centred heading and the insight being explained, before any technical detail', () => {
    const out = page(approved())
    expect(out).toContain('<h2 class="ex-title">How MatchShift built this insight</h2>')
    expect(out).toContain(en.explain.subheading)
    expectOrder(out, ['How MatchShift built this insight', 'AI does not decide what happened in the match. The match data does.',
      'Current insight', '63&#x27;', 'Westhaven switch their attack down the right', 'Status: ', 'How this insight was created',
      'Step by step', 'Match data', 'What the agents were allowed to use', 'Tactical Analyst', 'Verification', 'Final status',
      'What if an agent gets it wrong?', 'Technical details'])
    // Each fact appears once in the primary story, not repeated in separate panels.
    expect(out.match(/What the agents were allowed to use/g)).toHaveLength(1)
    expect(out.match(/31% → 90%/g)).toHaveLength(1)
  })

  it('shows the evidence counts of the current insight (30 supporting, 1 context)', () => {
    const g = givenFacts(flagship)
    expect([g.supporting, g.context, g.importance, g.confidence]).toEqual([30, 1, 84, 'High'])
    expect(g.metric).toEqual({ label: 'Right-channel attacks', baseline: '31%', recent: '90%' })
    expect(countLabel(en.explain.contextCount, 1)).toBe('1 context event')
    expect(countLabel(en.explain.contextCount, 2)).toBe('2 context events')
    const out = page(approved())
    expect(out).toContain('30 supporting events · 1 context event')
    expect(out).toContain('31% → 90%')
    expect(out).toContain('>84<')
  })

  it('summarises the five steps with each step\'s real state', () => {
    const out = page(approved())
    for (const line of en.explain.how) expect(out).toContain(line)
    const failed = page(execution('LIVE_FOUNDRY', 'FALLBACK_UNVERIFIED', false, { 'tactical-analyst': { status: 'FALLBACK' } }))
    expect(failed).toContain('Fallback used')
    expect(failed).not.toContain('✓ Verified')
  })
})

describe('deterministic facts versus AI interpretation', () => {
  it('labels every stage as Deterministic or AI agent, in text and shape, not colour alone', () => {
    const out = page(approved())
    expect(out).toContain('Deterministic facts')
    expect(out).toContain('AI interpretation')
    const aiTags = out.match(/<span class="st-tag"><span aria-hidden="true">◆ <\/span>AI agent<\/span>/g) ?? []
    const detTags = out.match(/<span class="st-tag"><span aria-hidden="true">■ <\/span>Deterministic<\/span>/g) ?? []
    expect(aiTags).toHaveLength(4)    // Tactical Analyst, Audience Narrative, Accessibility Narrator, Verification Agent
    expect(detTags).toHaveLength(3)   // match data (deterministic analytics), grounding check, final check
    expect(out.match(/class="st-card st-ai /g)).toHaveLength(4)
  })

  it('explains all four agents in plain English, technical names only underneath', () => {
    const out = page(approved())
    for (const [stage, sentence] of [
      ['tactical-analyst', 'Explains why the detected change matters tactically.'],
      ['audience-narrative', 'Adapts the same grounded insight for the selected viewer.'],
      ['accessibility-narrator', 'Creates accessible spoken wording from the same evidence.'],
      ['verification-agent', 'Checks that the generated explanation stays supported by the match facts.'],
    ]) {
      expect(out).toContain(sentence)
      expectOrder(out, [en.explain.stages[stage][0], sentence, '<p class="st-tech">Microsoft Agent Framework · gpt-4.1-mini'])
    }
  })
})

describe('handoffs: fan-out to two parallel agents, fan-in before verification', () => {
  it('shows the parallel pair between the Tactical Analyst and the checks, with labelled handoffs', () => {
    const out = page(approved())
    expectOrder(out, ['Tactical Analyst', 'Grounded context', 'Parallel handoff · same evidence', 'role="group" aria-label="Works in parallel"',
      '<ul class="st-branches">', 'Audience Narrative', 'Accessibility Narrator', '</ul>', 'Ran at the same time (4.6 s overlap)',
      'Both results joined', 'Grounding check', 'Validated result', 'Verification Agent', 'Verification handoff', 'Final check', 'Final decision'])
    expect(out.match(/<li class="st-branch">/g)).toHaveLength(2)
  })

  it('shows the repair loop only when the workflow actually repaired', () => {
    expect(page(approved())).not.toContain(en.explain.repairLoop)
    const x = approved()
    x.steps.push(step(9, 'constrained-repair', 'LIVE_FOUNDRY'))
    expect(page(x)).toContain(en.explain.repairLoop)
  })
})

const card = (x: WorkflowExecution) => renderToStaticMarkup(createElement(AW.VerificationCard, { x, s: en }))
const states = (x: WorkflowExecution) => deterministicChecks(x).map(c => c.state)

describe('verification result comes from the real workflow state', () => {
  it('APPROVED: code checks passed, the Verification Agent approved, final status Verified', () => {
    const x = approved()
    expect(states(x)).toEqual(['pass', 'pass', 'pass'])
    expect(verifierState(x)).toBe('approved')
    const out = card(x)
    for (const k of Object.values(en.explain.checks)) expect(out).toContain(k)
    expectOrder(out, ['Deterministic numeric check', 'Passed', 'Verification Agent (AI)', '>Approved<', 'Final status', 'Verified', 'Result of this live Microsoft Foundry run.'])
  })

  it('APPROVED_WITH_CHANGES: agent row says so, with the safe high-level note', () => {
    const x = execution('LIVE_FOUNDRY', 'APPROVED_WITH_CHANGES', true)
    expect(verifierState(x)).toBe('approvedWithChanges')
    const out = card(x)
    expect(out).toContain('Approved with changes')
    expect(out).toContain('Unsupported wording was removed before publication.')
  })

  it('REJECTED: not published, and the findings that caused it are shown as problems', () => {
    const x = execution('LIVE_FOUNDRY', 'REJECTED', false, {
      'deterministic-validation': { findings: ['UNSUPPORTED_NUMBER Audience Narrative.summary: 95% is not an authoritative value.'] },
    })
    expect(Object.fromEntries(deterministicChecks(x).map(c => [c.key, c.state]))).toEqual({ numbers: 'fail', evidence: 'pass', facts: 'pass' })
    expect(verifierState(x)).toBe('rejected')
    const out = page(x)
    expect(out).toContain('This generated explanation was not published.')
    expect(out).toContain('Status: </span>✗ Rejected')
    expect(out).not.toContain('✓ Verified')
  })

  it('an approval the code then overrode is never shown as approved', () => {
    const x = execution('LIVE_FOUNDRY', 'REJECTED', false, { 'verification-agent': { verificationDecision: 'APPROVED' } })
    expect(verifierState(x)).toBe('rejected')
  })

  it('a verifier that failed or never ran is never Approved or Passed', () => {
    expect(verifierState(execution('LIVE_FOUNDRY', 'FALLBACK_UNVERIFIED', false, { 'verification-agent': { status: 'FAILED', verificationDecision: undefined } }))).toBe('failed')
    expect(verifierState(execution('LIVE_FOUNDRY', 'FALLBACK_UNVERIFIED', false, { 'verification-agent': { status: 'SKIPPED', verificationDecision: undefined } }))).toBe('skipped')
  })

  it('a problem removed by code before an approved result is shown as corrected, never hidden', () => {
    const x = execution('LIVE_FOUNDRY', 'APPROVED', true, { 'deterministic-validation': { findings: ["UNKNOWN_EVIDENCE_ID Tactical Analyst.evidenceIds: Evidence 'X' does not exist."] } })
    expect(deterministicChecks(x).find(c => c.key === 'evidence')?.state).toBe('fixed')
    expect(card(x)).toContain('Unsupported wording was removed before publication.')
  })
})

describe('OFFLINE TEMPLATE vs RECORDED DEMO vs LIVE MICROSOFT FOUNDRY in the verification result', () => {
  it('OFFLINE_TEMPLATE: code checks may pass, but the Verification Agent is NOT RUN and nothing is AI-verified', () => {
    const x = execution('OFFLINE_TEMPLATE', 'DETERMINISTIC_ONLY', false)
    expect(states(x)).toEqual(['pass', 'pass', 'pass'])         // deterministic checks really ran on the template wording
    expect(verifierState(x)).toBe('offline')
    const out = card(x)
    expectOrder(out, ['Deterministic numeric check', 'Passed', 'Verification Agent (AI)', 'Not run — OFFLINE TEMPLATE',
      'Final status', 'OFFLINE TEMPLATE · Not AI-verified', 'No model was called, so nothing is AI-verified.'])
    // The AI row is never "Passed" or "Approved" offline: the only "Passed" texts are the three code checks.
    expect(out.match(/>Passed</g)).toHaveLength(3)
    expect(out).not.toMatch(/>Approved|Verified</)
    const whole = page(x)
    expect(whole).toContain('Status: </span>OFFLINE TEMPLATE · Not AI-verified')
    expect(whole).toContain('Not run — OFFLINE TEMPLATE')             // the summary and story say so too
    expect(whole).not.toMatch(/✓ Verified|>Verified</)
    expect(whole).not.toContain('Result of this live')
  })

  it('RECORDED_DEMO: says the result comes from a previously recorded real run, never live', () => {
    const out = card(asRecorded(approved(), '2026-10-19'))
    expect(out).toContain('Result of a previously recorded real workflow run. Nothing ran live now.')
    expect(out).not.toContain('Result of this live')
  })

  it('LIVE_FOUNDRY: says the result comes from this live run', () => {
    expect(card(approved())).toContain('Result of this live Microsoft Foundry run.')
  })
})

describe('the recorded flagship run (MS-FLAGSHIP-001-I015)', () => {
  it('replays in the static demo as RECORDED_DEMO with the real outcome and no live call', () => {
    const files = import.meta.glob<{ recordedAt: string; executions: Record<string, WorkflowExecution> }>('../../public/demo/agent-recordings.json', { eager: true, import: 'default' })
    const file = Object.values(files)[0]
    expect(file, 'public/demo/agent-recordings.json').toBeDefined()
    const raw = file.executions['MS-FLAGSHIP-001-I015']
    expect(raw, 'flagship recording').toBeDefined()
    expect(raw.executionMode).toBe('LIVE_FOUNDRY')                  // it came from a real live run...
    expect(raw.modelCalls).toBeGreaterThan(0)
    const x = asRecorded(raw, file.recordedAt)                       // ...and is only ever shown as a recording
    expect(x.executionMode).toBe('RECORDED_DEMO')
    expect(x.recordedFrom).toMatch(/^LIVE_FOUNDRY run recorded 2026-\d\d-\d\d \d\d:\d\d UTC$/)
    expect(JSON.stringify(raw)).not.toMatch(/services\.ai\.azure\.com|instructions|"prompt|apiKey|tenant|subscription/i)
    const out = page(x, { status: null })
    expect(out).toContain('RECORDED DEMO')
    expect(out).not.toContain('LIVE MICROSOFT FOUNDRY')
    expect(out).toContain(`Status: </span>✓ ${en.explain.statusWord[x.outcome === 'APPROVED' ? 'verified' : 'verifiedWithChanges']}`)
    expect(out).toContain('30 supporting events · 1 context event')
  })
})

describe('execution mode', () => {
  it('LIVE_FOUNDRY shows the live badge with the framework, model and managed identity', () => {
    const out = page(approved())
    expect(out).toContain(`<span class="ex-mode-badge">${MODE_BADGE.LIVE_FOUNDRY}</span>`)
    expect(out).toContain('Microsoft Agent Framework · gpt-4.1-mini · Managed identity')
  })

  it('offline or recorded runs can never appear as LIVE, even when the runtime itself is live', () => {
    const offline = page(execution('OFFLINE_TEMPLATE', 'DETERMINISTIC_ONLY', false), { status: runtime('LIVE_FOUNDRY') })
    expect(offline).toContain('OFFLINE TEMPLATE')
    expect(offline).not.toContain('LIVE MICROSOFT FOUNDRY')
    expect(offline).not.toContain('Managed identity')
    const recorded = page(asRecorded(approved(), '2026-10-19'), { status: runtime('LIVE_FOUNDRY') })
    expect(recorded).toContain('RECORDED DEMO')
    expect(recorded).toContain('Nothing is executing live')
    expect(recorded).not.toContain('LIVE MICROSOFT FOUNDRY')
    expect(recorded).not.toContain('ex-mode-tech')
  })

  it('before any data there is no LIVE claim and no workflow story', () => {
    const out = renderToStaticMarkup(createElement(AW.AgentsExplainer, { insightId: null, x: null, status: null, s: en }))
    expect(out).not.toContain('LIVE')
    expect(out).not.toContain('Step by step')
    expect(out).toContain('Select an insight card')
    expect(out).toContain('What if an agent gets it wrong?')
  })
})

describe('trust and evidence navigation', () => {
  it('explains failure recovery in plain language', () => {
    const out = page(approved())
    for (const item of en.explain.failItems) expect(out).toContain(item.replace(/'/g, '&#x27;'))
  })

  it('offers WHY? > SHOW ME > Evidence replay for the current insight, wired to the real actions', () => {
    const calls: string[] = []
    const actions = { onWhy: () => calls.push('why'), onShow: () => calls.push('show'), onReplay: () => calls.push('replay') }
    const out = page(approved(), { actions })
    expectOrder(out, ['role="group" aria-label="Inspect the evidence"', '>Why?<', '>Show me<', '>Evidence replay<'])
    // Walk the rendered element tree and press each button.
    const buttons: ReactElement<{ onClick: () => void }>[] = []
    const walk = (n: ReactNode) => {
      if (Array.isArray(n)) n.forEach(walk)
      else if (isValidElement<{ children?: ReactNode; onClick?: () => void }>(n)) {
        if (n.type === 'button') buttons.push(n as ReactElement<{ onClick: () => void }>)
        walk(n.props.children)
      }
    }
    walk(AW.CurrentInsight({ card: flagship, s: en, actions }))
    buttons.forEach(b => b.props.onClick())
    expect(calls).toEqual(['why', 'show', 'replay'])
    expect(page(approved())).not.toContain('Inspect the evidence')
  })

  it('keeps the full technical trace, collapsed by default and after the explanation', () => {
    const out = page({ ...approved(), published: { workflowExecutionId: 'wf', insightId: flagship.id, executionMode: 'LIVE_FOUNDRY', outcome: 'APPROVED', verified: true,
      source: 'microsoft-foundry', headline: 'AI headline', summary: 'AI summary', limitations: [], evidenceIds: [], notice: 'Verified.' } })
    expect(out).toMatch(/<details class="card wf-published "><summary class="card-kicker">What viewers see · /)
    expect(out).toMatch(/<details class="wf-trace"><summary>Technical details · execution trace<\/summary>/)
    for (const s of ['wf-test-0001', 'MS-FLAGSHIP-001-I015', 'TacticalHandoff (fan-out)', 'superstep', 'Execution timeline', 'Stage details', '6E18'])
      expect(out).toContain(s)
    expect(out.indexOf('Technical details')).toBeGreaterThan(out.indexOf('id="ex-verify-h"'))
  })

  it('shows an unavailable trace honestly, keeping the deterministic facts', () => {
    const out = page(null, { error: 'This static demo has no recorded agent workflow for this insight.' })
    expect(out).toContain('no recorded agent workflow')
    expect(out).toContain('30 supporting events · 1 context event')
    expect(out).not.toContain('Status: ')
    expect(out).not.toContain('Step by step')
  })
})

describe('accessibility semantics', () => {
  it('uses one h2, h3 sections, h4 stage titles, one status region and text for every state', () => {
    const out = page(approved())
    expect(out.match(/<h2/g)).toHaveLength(1)
    expect((out.match(/<h3/g) ?? []).length).toBeGreaterThanOrEqual(6)
    expect(out.match(/<h4 class="st-title">/g)).toHaveLength(7)
    expect(out.match(/role="status"/g)).toHaveLength(1)
    expect(out).toContain('<ol class="st-flow">')
    // Icons are decoration; the state word is always present as text.
    expect(out).not.toMatch(/<span class="ex-state[^"]*">[✓○–!✗…·]<\/span>/)
    expect(out).toMatch(/<span aria-hidden="true">✓ <\/span>Done/)
  })
})

describe('every language', () => {
  it('renders the page in all six languages from localized strings', () => {
    for (const lang of LANGUAGE_CODES) {
      const s = t(lang)
      const out = renderToStaticMarkup(createElement(AW.AgentsExplainer, { insightId: flagship.id, card: flagship, x: approved(), status: runtime('LIVE_FOUNDRY'), s }))
      for (const text of [s.explain.heading, s.explain.principle, s.explain.givenTitle, s.explain.failTitle, s.explain.links.parallel, s.explain.statusWord.verified])
        expect(out, `${lang}: ${text}`).toContain(text.replace(/'/g, '&#x27;'))
      if (lang !== 'en') {
        expect(s.explain.heading).not.toBe(en.explain.heading)
        expect(out).not.toContain(en.explain.principle)
        expect(out).not.toContain(en.explain.stages['tactical-analyst'][2])
      }
      expect(out).toContain('LIVE MICROSOFT FOUNDRY')   // identifiers stay exact
    }
  })
})
