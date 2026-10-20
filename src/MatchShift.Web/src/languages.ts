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

/** BCP-47 tag used for speech and document language. Unknown codes fall back to English. */
export const localeOf = (lang: Lang | string): string => LANGUAGES.find(l => l.code === lang)?.locale ?? 'en-GB'
