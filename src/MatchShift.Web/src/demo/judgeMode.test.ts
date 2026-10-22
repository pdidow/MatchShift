import { afterEach, describe, expect, it, vi } from 'vitest'
import type { WorkflowExecution } from '../api'
import { usesRecordedDemo } from './mode'

const files = import.meta.glob<{ recordedAt: string; executions: Record<string, WorkflowExecution> }>(
  '../../public/demo/agent-recordings.json', { eager: true, import: 'default' },
)
const recordings = Object.values(files)[0]
const profile = { mode: 'Analyst' as const, language: 'en' as const }

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  vi.resetModules()
})

describe('hosted judge mode routing', () => {
  it('selects the recorded client only for ?demo=judge or a static build', () => {
    expect(usesRecordedDemo('?demo=judge', false)).toBe(true)
    expect(usesRecordedDemo('?demo=1', false)).toBe(false)
    expect(usesRecordedDemo('', false)).toBe(false)
    expect(usesRecordedDemo('?demo=judge', true)).toBe(true)
    expect(usesRecordedDemo('', true)).toBe(true)
  })

  it('replays the real I015 result with no workflow-start or other API call', async () => {
    expect(recordings).toBeDefined()
    vi.stubGlobal('location', { search: '?demo=judge&session=test-judge' })
    const fetcher = vi.fn(async (url: string) => {
      if (url.endsWith('/demo/agent-recordings.json')) return Response.json(recordings)
      throw new Error(`Unexpected network request: ${url}`)
    })
    vi.stubGlobal('fetch', fetcher)
    const { api, subscribeDemo } = await import('../api')
    expect(subscribeDemo).toBeTypeOf('function')

    const status = await api.agentStatus()
    const result = await api.agentTrace('MS-FLAGSHIP-001-I015', profile)
    expect(status.runtime.executionMode).toBe('RECORDED_DEMO')
    expect(result?.executionMode).toBe('RECORDED_DEMO')
    expect(result?.outcome).toBe('APPROVED_WITH_CHANGES')
    expect(result?.verificationDecision).toBe('APPROVED_WITH_CHANGES')
    expect(result?.steps.map(s => s.stage)).toEqual(expect.arrayContaining([
      'deterministic-grounding', 'tactical-analyst', 'audience-narrative', 'accessibility-narrator',
      'verification-agent', 'final-validation',
    ]))
    expect(result?.concurrency?.overlapped).toBe(true)
    expect(result?.modelCalls).toBeGreaterThan(0) // historical count, not a call made by this test
    expect(fetcher.mock.calls.every(([url]) => String(url).includes('/demo/agent-recordings.json'))).toBe(true)
  })

  it('shows a missing-recording refusal and never falls through to a live endpoint', async () => {
    vi.stubGlobal('location', { search: '?demo=judge&session=test-missing' })
    const fetcher = vi.fn(async (url: string) => {
      if (url.endsWith('/demo/agent-recordings.json')) return Response.json(recordings)
      throw new Error(`Unexpected network request: ${url}`)
    })
    vi.stubGlobal('fetch', fetcher)
    const { api } = await import('../api')
    await expect(api.agentTrace('MS-FLAGSHIP-001-I009', profile)).rejects.toThrow(
      'No recorded workflow is available for this insight.',
    )
    expect(fetcher.mock.calls.every(([url]) => String(url).includes('/demo/agent-recordings.json'))).toBe(true)
  })

  it('keeps normal hosted workflow routing live-capable', async () => {
    vi.stubGlobal('location', { search: '?session=test-normal' })
    const live = recordings.executions['MS-FLAGSHIP-001-I015']
    const fetcher = vi.fn(async (_url: string) => Response.json(live))
    vi.stubGlobal('fetch', fetcher)
    const { api, subscribeDemo } = await import('../api')
    expect(subscribeDemo).toBeUndefined()
    const result = await api.agentTrace('MS-FLAGSHIP-001-I015', profile)
    expect(result?.executionMode).toBe('LIVE_FOUNDRY')
    expect(fetcher).toHaveBeenCalledOnce()
    expect(String(fetcher.mock.calls[0][0])).toContain('/api/demo/insights/MS-FLAGSHIP-001-I015/agents?')
    expect(String(fetcher.mock.calls[0][0])).not.toContain('run=false')
  })
})
