// The single list of languages MatchShift supports. UI strings, accessibility strings, narration and speech all key off
// these codes; the server's LexiconRegistry has the same set (checked by tests).

export const LANGUAGES = [
  { code: 'en', name: 'English', locale: 'en-GB' },
  { code: 'fr', name: 'Français', locale: 'fr-FR' },
  { code: 'es', name: 'Español', locale: 'es-ES' },
  { code: 'de', name: 'Deutsch', locale: 'de-DE' },
  { code: 'it', name: 'Italiano', locale: 'it-IT' },
  { code: 'pt', name: 'Português', locale: 'pt-PT' },
] as const

export type Lang = (typeof LANGUAGES)[number]['code']

export const LANGUAGE_CODES: readonly Lang[] = LANGUAGES.map(l => l.code)

export const isLang = (value: string | null | undefined): value is Lang => !!value && (LANGUAGE_CODES as readonly string[]).includes(value)

/** Resolve UI codes and browser locale tags through the same supported-language registry. */
export const languageOf = (value: string): Lang | undefined => {
  const base = value.toLowerCase().replace(/_/g, '-').split('-')[0]
  return isLang(base) ? base : undefined
}

/** BCP-47 tag used for speech, document language and numeric formatting. */
export const localeOf = (lang: Lang | string): string => LANGUAGES.find(l => l.code === languageOf(lang))?.locale ?? 'en-GB'

const formatters = new Map<string, Intl.NumberFormat>()
export function formatNumber(value: number, lang: string, options: Intl.NumberFormatOptions = {}): string {
  const locale = localeOf(lang)
  const key = `${locale}:${JSON.stringify(options)}`
  let formatter = formatters.get(key)
  if (!formatter) {
    formatter = new Intl.NumberFormat(locale, options)
    formatters.set(key, formatter)
  }
  return formatter.format(value)
}

/** Localized units (including singular/plural); digits are pronounced by the selected language's voice. */
export const formatMinutes = (value: number, lang: string) =>
  formatNumber(value, lang, { style: 'unit', unit: 'minute', unitDisplay: 'long', maximumFractionDigits: 1 })
export const formatPercent = (value: number, lang: string) =>
  formatNumber(value, lang, { style: 'percent', maximumFractionDigits: 0 })

/** A match/replay duration, not a time of day such as "quarter past three". */
export function spokenClock(seconds: number, lang: string): string {
  const whole = Math.max(0, Math.floor(seconds))
  return `${formatMinutes(Math.floor(whole / 60), lang)}, ${formatNumber(whole % 60, lang, { style: 'unit', unit: 'second', unitDisplay: 'long' })}`
}
