export type BallHighlight = 'off' | 'halo' | 'ring' | 'glow'
export type BallColor = 'auto' | 'yellow' | 'cyan' | 'white' | 'red'
export type BallTrailLength = 'off' | 'short' | 'medium' | 'long'
export type BallSize = 'normal' | 'enhanced' | 'high'
export type BallHighlightIntensity = 'subtle' | 'normal' | 'strong'
export type BallPreset = 'broadcast' | 'analysis' | 'accessibility' | 'clean' | 'custom'

export interface BallVisualizationSettings {
  preset: BallPreset
  highlight: BallHighlight
  intensity: BallHighlightIntensity
  color: BallColor
  trail: BallTrailLength
  size: BallSize
  pulse: boolean
}

export const BALL_PRESETS: Record<Exclude<BallPreset, 'custom'>, BallVisualizationSettings> = {
  broadcast: { preset: 'broadcast', highlight: 'halo', intensity: 'subtle', color: 'auto', trail: 'off', size: 'normal', pulse: true },
  analysis: { preset: 'analysis', highlight: 'halo', intensity: 'normal', color: 'auto', trail: 'short', size: 'enhanced', pulse: true },
  accessibility: { preset: 'accessibility', highlight: 'ring', intensity: 'strong', color: 'yellow', trail: 'medium', size: 'high', pulse: true },
  clean: { preset: 'clean', highlight: 'off', intensity: 'subtle', color: 'auto', trail: 'off', size: 'normal', pulse: false },
}

export const DEFAULT_BALL_SETTINGS = BALL_PRESETS.analysis
export const BALL_TRAIL_SECONDS: Record<BallTrailLength, number> = { off: 0, short: 0.5, medium: 1.25, long: 2.5 }
export const BALL_SIZE_SCALE: Record<BallSize, number> = { normal: 1, enhanced: 1.23, high: 1.45 }
export const BALL_COLORS: Record<BallColor, string> = {
  auto: '#f8de63', yellow: '#f8de63', cyan: '#7ce3ed', white: '#f6f7ee', red: '#ff7774',
}

const STORAGE_KEY = 'matchshift.replay.ballVisualization'
const isOneOf = <T extends string>(value: unknown, options: readonly T[]): value is T => options.includes(value as T)

export function loadBallSettings(): BallVisualizationSettings {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Partial<BallVisualizationSettings>
    return {
      preset: isOneOf(saved.preset, ['broadcast', 'analysis', 'accessibility', 'clean', 'custom']) ? saved.preset : 'analysis',
      highlight: isOneOf(saved.highlight, ['off', 'halo', 'ring', 'glow']) ? saved.highlight : DEFAULT_BALL_SETTINGS.highlight,
      intensity: isOneOf(saved.intensity, ['subtle', 'normal', 'strong']) ? saved.intensity : DEFAULT_BALL_SETTINGS.intensity,
      color: isOneOf(saved.color, ['auto', 'yellow', 'cyan', 'white', 'red']) ? saved.color : DEFAULT_BALL_SETTINGS.color,
      trail: isOneOf(saved.trail, ['off', 'short', 'medium', 'long']) ? saved.trail : DEFAULT_BALL_SETTINGS.trail,
      size: isOneOf(saved.size, ['normal', 'enhanced', 'high']) ? saved.size : DEFAULT_BALL_SETTINGS.size,
      pulse: typeof saved.pulse === 'boolean' ? saved.pulse : DEFAULT_BALL_SETTINGS.pulse,
    }
  } catch { return { ...DEFAULT_BALL_SETTINGS } }
}

export function saveBallSettings(settings: BallVisualizationSettings) {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(settings)) } catch { /* storage unavailable */ }
}
