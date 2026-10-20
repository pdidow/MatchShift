import { describe, expect, it } from 'vitest'
import type { AgentRuntimeStatus, ExecutionMode, InsightCard, WorkflowExecution, WorkflowOutcome } from '../api'
import { t } from '../i18n'
import type { GuideStep } from './guide'
import { EVALUATION, FLAGSHIP, GUIDE_STEPS, findFlagship, nextStep, prevStep, runtimeChips, verdict, wantsGuideFromUrl } from './guide'

const card = (id: string, type: string, minute: number) => ({ id, type, minute } as InsightCard)

function status(mode: ExecutionMode, credential?: string): AgentRuntimeStatus {
  return {
    runtime: { executionMode: mode, provider: 'p', modelDeployment: mode === 'LIVE_FOUNDRY' ? 'gpt-4.1-mini' : undefined, credential, orchestrator: 'o', packages: {} },
    warmUp: 'done', agents: [],
  }
}

function exec(mode: ExecutionMode, outcome: WorkflowOutcome | undefined, verified: boolean, run: WorkflowExecution['status'] = 'COMPLETED'): WorkflowExecution {
  return {
    workflowExecutionId: 'wf', insightId: 'I', matchId: 'M', executionMode: mode, provider: 'p', modelDeployment: mode === 'LIVE_FOUNDRY' ? 'gpt-4.1-mini' : undefined,
    orchestrator: 'o', status: run, startedAt: '2026-10-19T12:00:00Z', steps: [], handoffs: [], frameworkSupersteps: 7, frameworkExecutorInvocations: 9,
    modelCalls: 0, modelCallLimit: 8, inputTokens: 0, outputTokens: 0, outcome, verified, authoritativeFactsUnchanged: true, recoveryActions: [],
  }
}

describe('guided demo steps', () => {
  it('runs intro -> every viewer -> insight -> WHY? -> SHOW ME -> Evidence Replay -> Agents -> trust', () => {
    expect(GUIDE_STEPS).toEqual(['intro', 'viewers', 'insight', 'why', 'show', 'replay', 'agents', 'trust'])
    let s: GuideStep = GUIDE_STEPS[0]
    const seen: GuideStep[] = [s]
    for (let n = nextStep(s); n; n = nextStep(n)) seen.push(n)
    expect(seen).toEqual([...GUIDE_STEPS])
    expect(nextStep('trust')).toBeNull()
    expect(prevStep('intro')).toBeNull()
    expect(prevStep('replay')).toBe('show')
    s = 'agents'
    expect(nextStep(s)).toBe('trust')
  })

  it('starts from ?demo=judge only', () => {
    expect(wantsGuideFromUrl('?demo=judge')).toBe(true)
    expect(wantsGuideFromUrl('?lang=fr&demo=1')).toBe(true)
    expect(wantsGuideFromUrl('?lang=fr')).toBe(false)
    expect(wantsGuideFromUrl('?demo=no')).toBe(false)
  })

  it('finds the real flagship insight only in the flagship match', () => {
    const insights = [card('I014', 'PRESSURE_INCREASE', 58), card('I015', 'ATTACK_CHANNEL_SHIFT', 63), card('I017', 'POST_SUBSTITUTION_CHANGE', 68)]
    expect(findFlagship(FLAGSHIP.matchId, insights)?.id).toBe('I015')
    expect(findFlagship('MS-ACS-6035', insights)).toBeUndefined()
    expect(findFlagship(FLAGSHIP.matchId, [card('I099', 'ATTACK_CHANNEL_SHIFT', 80)])).toBeUndefined()
    expect(FLAGSHIP.seekMinute).toBeGreaterThan(FLAGSHIP.minute)                    // the insight exists when the guide lands
  })
})

describe('live status is truthful', () => {
  it('shows LIVE, the model and managed identity only for a live execution', () => {
    const labels = runtimeChips(status('LIVE_FOUNDRY', 'ManagedIdentity'), exec('LIVE_FOUNDRY', 'APPROVED', true)).map(c => c.label)
    expect(labels).toEqual(['LIVE · Microsoft Foundry', 'Microsoft Agent Framework', 'gpt-4.1-mini', 'Managed identity', 'Verification: APPROVED'])
  })

  it('never shows LIVE, a model or a credential when offline or recorded', () => {
    for (const mode of ['OFFLINE_TEMPLATE', 'RECORDED_DEMO'] as ExecutionMode[]) {
      const labels = runtimeChips(status(mode, 'ManagedIdentity'), exec(mode, 'DETERMINISTIC_ONLY', false)).map(c => c.label).join(' | ')
      expect(labels).not.toMatch(/LIVE ·|gpt-4\.1-mini|Managed identity/)
    }
    expect(runtimeChips(status('RECORDED_DEMO'), null)[0].label).toContain('not live')
  })

  it('the execution mode wins over the runtime mode (a quota fallback on a live server is not live)', () => {
    const labels = runtimeChips(status('LIVE_FOUNDRY', 'ManagedIdentity'), exec('OFFLINE_TEMPLATE', 'DETERMINISTIC_ONLY', false)).map(c => c.label)
    expect(labels[0]).toBe('OFFLINE TEMPLATE · no model calls')
    expect(labels).not.toContain('gpt-4.1-mini')
  })

  it('shows nothing before any status is known', () => {
    expect(runtimeChips(null, null)).toEqual([])
  })
})

describe('verification outcome', () => {
  it('maps outcomes to verified, verified with changes and rejected', () => {
    expect(verdict(exec('LIVE_FOUNDRY', 'APPROVED', true))?.kind).toBe('verified')
    expect(verdict(exec('LIVE_FOUNDRY', 'APPROVED_WITH_CHANGES', true))?.kind).toBe('verifiedWithChanges')
    expect(verdict(exec('LIVE_FOUNDRY', 'REJECTED', false))?.kind).toBe('rejected')
    expect(verdict(exec('LIVE_FOUNDRY', 'FALLBACK_UNVERIFIED', false))?.kind).toBe('unverified')
    expect(verdict(exec('LIVE_FOUNDRY', undefined, false, 'RUNNING'))?.kind).toBe('running')
    expect(verdict(null)).toBeNull()
  })

  it('never calls template output verified', () => {
    expect(verdict(exec('OFFLINE_TEMPLATE', 'DETERMINISTIC_ONLY', false))?.kind).toBe('deterministic')
    expect(verdict(exec('OFFLINE_TEMPLATE', 'APPROVED', true))?.kind).toBe('deterministic')   // even if a flag were wrong
    expect(verdict(exec('LIVE_FOUNDRY', 'APPROVED', false))?.kind).toBe('unverified')         // approval without code re-validation
  })
})

describe('localization', () => {
  it('every language has every guide step, layer, verdict and the evaluation proof', () => {
    for (const lang of ['en', 'fr', 'es'] as const) {
      const s = t(lang)
      for (const step of GUIDE_STEPS) {
        const g = s.guide.steps[step]
        expect(g.title && g.body && g.action, `${lang}.${step}`).toBeTruthy()
      }
      for (const k of ['verified', 'verifiedWithChanges', 'rejected', 'unverified', 'deterministic', 'running'] as const)
        expect(s.verdict[k]).toHaveLength(2)
      expect(s.guide.stepOf).toContain('{n}')
      expect(s.evalProof.title).toContain('Microsoft Foundry Evaluations')
      expect(s.chain.length).toBeGreaterThan(0)
    }
    expect(t('fr').guide.steps.why.title).not.toBe(t('en').guide.steps.why.title)
    expect(t('es').guide.entry).not.toBe(t('en').guide.entry)
  })

  it('names the replay as evidence, not as a 3D gimmick', () => {
    expect(t('en').replay.button).toBe('Evidence replay')
    expect(t('en').replay.title).toBe('Evidence Replay · 3D reconstruction')
    for (const lang of ['en', 'fr', 'es'] as const) expect(t(lang).replay.button).not.toMatch(/3D/)
  })

  it('evaluation figures match the latest real run report', () => {
    expect(EVALUATION.runId).toBe('evalrun_72ca538217034d0bb108a4bc5d2958a9')
    expect([EVALUATION.publishedPassing, EVALUATION.publishedTotal, EVALUATION.deterministicCaught, EVALUATION.verifierCaught, EVALUATION.foundryGroundednessCaught])
      .toEqual([28, 28, 11, 11, 5])
  })
})
