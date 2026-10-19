import { describe, expect, it } from 'vitest'
import type { InsightCard, PublishedInsight, WorkflowExecution } from './api'
import { insightSpeech, whyNarration } from './a11y/describe'
import { a11yStrings } from './a11y/strings'
import { asRecorded, recordedStatus } from './demo/recorded'

const en = a11yStrings('en')
const teamName = (id?: string) => (id === 'WHA' ? 'Westhaven' : '')
const opponentOf = () => 'Northbridge'

function published(verified: boolean): PublishedInsight {
  return {
    workflowExecutionId: 'wf-1', insightId: 'I1', executionMode: 'LIVE_FOUNDRY', outcome: verified ? 'APPROVED' : 'REJECTED', verified,
    source: 'microsoft-foundry', headline: verified ? 'Agent headline' : undefined, summary: verified ? 'Agent summary' : undefined,
    spokenSummary: verified ? 'Westhaven now attack mostly down the right.' : undefined,
    simplifiedExplanation: verified ? 'Westhaven keep going down one side.' : undefined,
    visualDescription: verified ? 'Most supporting events were in the right channel.' : undefined,
    limitations: [], evidenceIds: ['MS-FLAGSHIP-001-E00010'], notice: verified ? 'Verified.' : 'Rejected.',
  }
}

function card(agent?: PublishedInsight): InsightCard {
  return {
    id: 'I1', type: 'ATTACK_CHANNEL_SHIFT', minute: 63, second: 3780, teamId: 'WHA', severity: 'High', confidence: 'High', confidenceScore: 0.9,
    importance: 80, span: { fromSecond: 3000, toSecond: 3720 }, visuals: [], supportingEventCount: 12, agent,
    narrative: {
      insightId: 'I1', language: 'en', mode: 'Analyst', typeLabel: 'Match shift', headline: 'Westhaven switch to the right',
      fact: 'Attacks down the right rose from 31% to 90%', inference: 'They are targeting the right flank', ticker: '', studioLine: '',
      confidenceLabel: 'High', severityLabel: 'High', source: 'template', evidence: [],
    },
  }
}

function execution(): WorkflowExecution {
  return {
    workflowExecutionId: 'wf-1', insightId: 'I1', matchId: 'MS-FLAGSHIP-001', executionMode: 'LIVE_FOUNDRY', provider: 'microsoft-foundry',
    modelDeployment: 'gpt-4.1-mini', orchestrator: 'Microsoft Agent Framework Workflows', status: 'COMPLETED', startedAt: '2026-10-19T12:00:00Z',
    steps: [{
      sequence: 1, stage: 'tactical-analyst', agentName: 'MatchShift Tactical Analyst', responsibility: 'r', executionType: 'MICROSOFT_AGENT_FRAMEWORK',
      executionMode: 'LIVE_FOUNDRY', provider: 'microsoft-foundry', startedAt: '2026-10-19T12:00:00Z', status: 'COMPLETED', handoffFrom: [], handoffTo: [],
      evidenceIds: [], evidenceCount: 0, inputSummary: '', outputSummary: '', modelCalls: 1, retryCount: 0, attemptFailures: [], inputTokens: 1, outputTokens: 1, findings: [],
    }],
    handoffs: [], frameworkSupersteps: 7, frameworkExecutorInvocations: 9, modelCalls: 4, modelCallLimit: 8, inputTokens: 1, outputTokens: 1,
    outcome: 'APPROVED', verified: true, authoritativeFactsUnchanged: true, recoveryActions: [], published: published(true),
  }
}

describe('agent workflow output in accessible narration', () => {
  it('reads the Accessibility Narrator text only once the workflow verified it', () => {
    expect(insightSpeech(card(published(true)), 'detailed', false, en, teamName, opponentOf)).toContain('Westhaven now attack mostly down the right.')
    expect(insightSpeech(card(published(true)), 'detailed', true, en, teamName, opponentOf)).toContain('Westhaven keep going down one side.')
    expect(whyNarration(card(published(true)), en)).toContain('Most supporting events were in the right channel.')
  })

  it('never reads unverified agent output and falls back to the deterministic wording', () => {
    const rejected = card({ ...published(true), verified: false, outcome: 'REJECTED' })
    const speech = insightSpeech(rejected, 'detailed', false, en, teamName, opponentOf)
    expect(speech).not.toContain('Westhaven now attack mostly down the right.')
    expect(speech).toContain('Attacks down the right rose from 31% to 90%')
    expect(whyNarration(rejected, en)).not.toContain('Most supporting events')
    expect(insightSpeech(card(), 'detailed', false, en, teamName, opponentOf)).toContain('They are targeting the right flank')
  })
})

describe('RECORDED_DEMO', () => {
  it('relabels a recorded live run so it can never be shown as live', () => {
    const recorded = asRecorded(execution(), '2026-10-19')
    expect(recorded.executionMode).toBe('RECORDED_DEMO')
    expect(recorded.steps.every(s => s.executionMode === 'RECORDED_DEMO')).toBe(true)
    expect(recorded.published?.executionMode).toBe('RECORDED_DEMO')
    expect(recorded.recordedFrom).toContain('LIVE_FOUNDRY')
    expect(JSON.stringify(recorded)).not.toContain('"executionMode":"LIVE_FOUNDRY"')
  })

  it('reports the static runtime as recorded, never live', () => {
    const status = recordedStatus(null)
    expect(status.runtime.executionMode).toBe('RECORDED_DEMO')
    expect(status.runtime.configurationNote).toMatch(/nothing runs live/i)
  })
})
