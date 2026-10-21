import type { EvidenceLine, InsightCard } from '../api'
import type { Strings } from '../i18n'

/**
 * "Why this was flagged", in plain language, built only from the card's deterministic evidence (computed by the C#
 * detectors before any wording exists). A line is omitted when the evidence does not carry that value.
 */

const fill = (template: string, values: Record<string, string | number>) =>
  Object.entries(values).reduce((t, [k, v]) => t.replace(`{${k}}`, String(v)), template)

export const primaryEvidence = (card: InsightCard): EvidenceLine | undefined =>
  card.narrative.evidence.find(e => e.isPrimary) ?? card.narrative.evidence[0]

/** Statistical-strength evidence: the detectors' z statistics use metric keys ending in "Z" (e.g. ShiftStrengthZ). */
export const strengthEvidence = (card: InsightCard): EvidenceLine | undefined =>
  card.narrative.evidence.find(e => /Z$/.test(e.metric) && !!e.threshold)

export function basisLines(card: InsightCard, s: Strings): string[] {
  const i = s.insight
  const p = primaryEvidence(card)
  const lines: string[] = []
  if (p?.threshold) lines.push(p.change ? fill(i.thresholdLine, { change: p.change, threshold: p.threshold }) : fill(i.thresholdOnly, { threshold: p.threshold }))
  if (p && p.sampleSize > 0) {
    lines.push(p.minimumSample
      ? fill(p.sampleRequirementMet ? i.sampleLine : i.sampleNotMet, { n: p.sampleSize, min: p.minimumSample })
      : fill(i.sampleOnly, { n: p.sampleSize }))
  }
  const z = strengthEvidence(card)
  if (z) lines.push(fill(i.strengthLine, { z: z.recent, threshold: z.threshold! }))
  return lines
}

/** Confidence score as shown (0.95 / 0,95), with the real level scale (Statistics.LevelOf). */
export const confidenceScore = (card: InsightCard, s: Strings) =>
  fill(s.insight.score, { score: card.confidenceScore.toFixed(2).replace('.', s.insight.decimal) })

export interface EvidenceCounts { supporting: number; context: number }

/** Supporting events are the evidence; context events are drawn for comparison only and never counted as evidence. */
export const evidenceCounts = (card: InsightCard): EvidenceCounts => ({
  supporting: card.supportingEventCount,
  context: (card.contextEventIds ?? []).length,
})
