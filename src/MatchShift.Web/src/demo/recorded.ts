import type { AgentRuntimeStatus, WorkflowExecution } from '../api'

/** File of agent workflow executions recorded from a real backend run, keyed by insight ID (see docs/AGENTS.md). */
export type AgentRecordings = { recordedAt: string; note: string; executions: Record<string, WorkflowExecution> }

/**
 * The static demo cannot call the backend, so it can only replay recordings. Whatever mode a recording was made in,
 * it is presented as RECORDED_DEMO and never as live; its original mode is kept in recordedFrom.
 */
export function asRecorded(x: WorkflowExecution, recordedAt: string): WorkflowExecution {
  const original = x.executionMode === 'RECORDED_DEMO' ? x.recordedFrom ?? 'unknown' : `${x.executionMode} run recorded ${recordedAt}`
  return {
    ...x,
    executionMode: 'RECORDED_DEMO',
    recordedFrom: original,
    steps: x.steps.map(s => ({ ...s, executionMode: 'RECORDED_DEMO' })),
    published: x.published ? { ...x.published, executionMode: 'RECORDED_DEMO' } : undefined,
  }
}

export function recordedStatus(recordings: AgentRecordings | null): AgentRuntimeStatus {
  const sample = recordings ? Object.values(recordings.executions)[0] : undefined
  return {
    runtime: {
      executionMode: 'RECORDED_DEMO',
      provider: 'recorded',
      modelDeployment: sample?.modelDeployment,
      orchestrator: sample?.orchestrator ?? 'Microsoft Agent Framework Workflows (recorded)',
      packages: {},
      configurationNote: 'Static demo: no backend and no credentials, so nothing runs live here. Traces are replays of earlier backend runs.',
    },
    warmUp: 'not applicable (static demo)',
    agents: [],
  }
}
