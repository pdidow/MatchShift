import type { InsightEvents, PitchEvent } from './api'

/**
 * What SHOW ME draws, split the same way as the data model: supporting events are the authoritative evidence (the card's
 * supporting-event count); context events are drawn grey for comparison and are never counted as evidence.
 */
export interface EvidenceSplit {
  supporting: PitchEvent[]
  context: PitchEvent[]
  /** Event IDs that appear more than once in the drawn set (should always be empty). */
  duplicates: string[]
}

export function splitEvidence(focus: InsightEvents): EvidenceSplit {
  const seen = new Set<string>()
  const duplicates: string[] = []
  for (const e of focus.events) {
    if (seen.has(e.id)) duplicates.push(e.id)
    seen.add(e.id)
  }
  // Older payloads without the split (e.g. a cached static export) treat everything drawn as supporting.
  const supportingIds = new Set(focus.supportingEventIds ?? focus.events.map(e => e.id))
  const contextIds = new Set(focus.contextEventIds ?? [])
  return {
    supporting: focus.events.filter(e => supportingIds.has(e.id)),
    context: focus.events.filter(e => contextIds.has(e.id) && !supportingIds.has(e.id)),
    duplicates,
  }
}

/** "30 supporting events + 1 context" in the viewer's language, from templates with {n} and {c}. */
export function evidenceLegend(split: EvidenceSplit, supportingTemplate: string, contextTemplate: string): string {
  const main = supportingTemplate.replace('{n}', String(split.supporting.length))
  return split.context.length > 0 ? `${main} ${contextTemplate.replace('{c}', String(split.context.length))}` : main
}
