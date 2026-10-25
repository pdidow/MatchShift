// Accessibility preferences. Everything is OFF by default: the full MatchShift experience is unchanged
// unless a user opts in. The only input taken from the system is prefers-reduced-motion, and only while
// the user has not made an explicit choice ("system").
import { LANGUAGES, type Lang } from '../languages'

export type SpeechDetail = 'major' | 'important' | 'detailed'
export type PeriodicRecap = 'off' | '5' | '10' | 'halftime' | 'shifts'
export type ColorVision = 'default' | 'deuteranopia' | 'protanopia' | 'tritanopia' | 'monochrome'
export type TextSize = 'normal' | 'large' | 'xl'
export type MotionChoice = 'system' | 'on' | 'off'
export type PresetId = 'default' | 'lowVision' | 'audioFirst' | 'reducedMotion' | 'custom'

export interface A11yPrefs {
  spokenInsights: boolean
  speechDetail: SpeechDetail
  audioMatchMode: boolean
  periodicRecap: PeriodicRecap
  replayAudioDescription: boolean
  describeVisuals: boolean
  speechRate: number
  speechVolume: number
  /** Optional browser-local voice choice for each MatchShift language. */
  voicePreferences: Partial<Record<Lang, string>>
  transcript: boolean
  soundCues: boolean
  highContrast: boolean
  colorVision: ColorVision
  textSize: TextSize
  reducedMotion: MotionChoice
  reduceFlashing: boolean
  simplified: boolean
  /** Set while the "Accessible audio" persona is selected; restores the previous prefs when it is left. */
  audioPersona: boolean
}

export const DEFAULT_PREFS: A11yPrefs = {
  spokenInsights: false,
  speechDetail: 'important',
  audioMatchMode: false,
  periodicRecap: 'off',
  replayAudioDescription: false,
  describeVisuals: false,
  speechRate: 1,
  speechVolume: 1,
  voicePreferences: {},
  transcript: false,
  soundCues: false,
  highContrast: false,
  colorVision: 'default',
  textSize: 'normal',
  reducedMotion: 'system',
  reduceFlashing: false,
  simplified: false,
  audioPersona: false,
}

/** Preset bundles. Each starts from the defaults, so choosing one never leaves unrelated options switched on. */
export const PRESETS: Record<Exclude<PresetId, 'custom'>, Partial<A11yPrefs>> = {
  default: {},
  lowVision: { textSize: 'large', highContrast: true },
  audioFirst: { spokenInsights: true, speechDetail: 'important', audioMatchMode: true, replayAudioDescription: true, describeVisuals: true, transcript: true },
  reducedMotion: { reducedMotion: 'on', reduceFlashing: true },
}

export function applyPreset(id: Exclude<PresetId, 'custom'>, current: A11yPrefs): A11yPrefs {
  // Keep comfort settings that are not part of any preset (speech rate and volume).
  return { ...DEFAULT_PREFS, speechRate: current.speechRate, speechVolume: current.speechVolume, voicePreferences: current.voicePreferences, ...PRESETS[id] }
}

/** Which preset the current prefs match exactly, or "custom". */
export function matchingPreset(p: A11yPrefs): PresetId {
  for (const id of Object.keys(PRESETS) as Exclude<PresetId, 'custom'>[]) {
    const expected = applyPreset(id, p)
    if ((Object.keys(DEFAULT_PREFS) as (keyof A11yPrefs)[]).every(k => k === 'audioPersona' || k === 'voicePreferences' || expected[k] === p[k])) return id
  }
  return 'custom'
}

export const STORAGE_KEY = 'matchshift.a11y'

export function loadPrefs(storage: Pick<Storage, 'getItem'> | undefined = safeStorage()): A11yPrefs {
  try {
    const raw = storage?.getItem(STORAGE_KEY)
    if (!raw) return { ...DEFAULT_PREFS }
    const parsed = JSON.parse(raw) as Partial<A11yPrefs>
    const merged = { ...DEFAULT_PREFS, ...parsed }
    // Ignore unknown/garbage values rather than breaking the UI.
    return sanitize(merged)
  } catch {
    return { ...DEFAULT_PREFS }
  }
}

export function savePrefs(p: A11yPrefs, storage: Pick<Storage, 'setItem'> | undefined = safeStorage()) {
  try { storage?.setItem(STORAGE_KEY, JSON.stringify(p)) } catch { /* storage unavailable */ }
}

function sanitize(p: A11yPrefs): A11yPrefs {
  const oneOf = <T,>(v: T, allowed: readonly T[], fallback: T) => (allowed.includes(v) ? v : fallback)
  return {
    ...p,
    speechDetail: oneOf(p.speechDetail, ['major', 'important', 'detailed'] as const, 'important'),
    periodicRecap: oneOf(p.periodicRecap, ['off', '5', '10', 'halftime', 'shifts'] as const, 'off'),
    colorVision: oneOf(p.colorVision, ['default', 'deuteranopia', 'protanopia', 'tritanopia', 'monochrome'] as const, 'default'),
    textSize: oneOf(p.textSize, ['normal', 'large', 'xl'] as const, 'normal'),
    reducedMotion: oneOf(p.reducedMotion, ['system', 'on', 'off'] as const, 'system'),
    speechRate: clamp(Number(p.speechRate) || 1, 0.5, 2),
    speechVolume: Number.isFinite(Number(p.speechVolume)) ? clamp(Number(p.speechVolume), 0, 1) : 1,
    voicePreferences: Object.fromEntries(Object.entries(p.voicePreferences ?? {}).filter(([code, name]) =>
      LANGUAGES.some(language => language.code === code) && typeof name === 'string' && name.length <= 128)) as Partial<Record<Lang, string>>,
  }
}

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v))

function safeStorage(): Storage | undefined {
  try { return typeof localStorage === 'undefined' ? undefined : localStorage } catch { return undefined }
}

/** Effective reduced-motion: the explicit choice wins; "system" follows prefers-reduced-motion. */
export const effectiveReducedMotion = (p: A11yPrefs, systemPrefersReduced: boolean) =>
  p.reducedMotion === 'on' || (p.reducedMotion === 'system' && systemPrefersReduced)

/** Any speech-producing feature enabled. */
export const speechEnabled = (p: A11yPrefs) =>
  p.spokenInsights || p.audioMatchMode || p.replayAudioDescription || p.describeVisuals || p.periodicRecap !== 'off'

/** Short labels for the status indicator - only for options that are actually on. */
export function activeFeatures(p: A11yPrefs, systemPrefersReduced: boolean): string[] {
  const on: string[] = []
  if (speechEnabled(p)) on.push('speech')
  if (p.transcript) on.push('transcript')
  if (p.highContrast) on.push('highContrast')
  if (p.colorVision !== 'default') on.push('colorVision')
  if (p.textSize !== 'normal') on.push('largeText')
  if (effectiveReducedMotion(p, systemPrefersReduced)) on.push('reducedMotion')
  if (p.reduceFlashing) on.push('reduceFlashing')
  if (p.simplified) on.push('simplified')
  if (p.soundCues) on.push('soundCues')
  return on
}

/** Data attributes applied to <html>. With default prefs this is empty, so default styling is untouched. */
export function documentAttributes(p: A11yPrefs, systemPrefersReduced: boolean): Record<string, string> {
  const attrs: Record<string, string> = {}
  if (p.highContrast) attrs['data-contrast'] = 'high'
  if (p.colorVision !== 'default') attrs['data-cvd'] = p.colorVision
  if (p.textSize !== 'normal') attrs['data-text'] = p.textSize
  if (effectiveReducedMotion(p, systemPrefersReduced)) attrs['data-reduced-motion'] = 'true'
  if (p.reduceFlashing) attrs['data-reduce-flash'] = 'true'
  return attrs
}
