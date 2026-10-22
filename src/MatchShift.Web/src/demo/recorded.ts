import type { AgentRuntimeStatus, WorkflowExecution } from '../api'

/** File of agent workflow executions recorded from a real backend run, keyed by insight ID (see docs/AGENTS.md). */
export type AgentRecordings = { recordedAt: string; note: string; executions: Record<string, WorkflowExecution> }

/**
 * The recorded demo cannot call the backend, so it can only replay recordings. Whatever mode a recording was made in,
 * it is presented as RECORDED_DEMO and never as live; its original mode is kept in recordedFrom.
 */
export function asRecorded(x: WorkflowExecution, recordedAt: string): WorkflowExecution {
  // Each run keeps its own date (one file can hold runs recorded on different days); the file date is only a fallback.
  const when = x.completedAt ? `${x.completedAt.slice(0, 10)} ${x.completedAt.slice(11, 16)} UTC` : recordedAt
  const original = x.executionMode === 'RECORDED_DEMO' ? x.recordedFrom ?? 'unknown' : `${x.executionMode} run recorded ${when}`
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
      configurationNote: 'Recorded demo: saved real workflow results only. Nothing runs live in this mode.',
    },
    warmUp: 'not applicable (recorded demo)',
    agents: [],
  }
}
