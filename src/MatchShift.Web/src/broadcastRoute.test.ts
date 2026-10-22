import { describe, expect, it } from 'vitest'
import { broadcastHref, broadcastRoute } from './broadcastRoute'
import { broadcastCopy } from './broadcastCopy'
import { LANGUAGE_CODES } from './languages'
import { t } from './i18n'
import { usesRecordedDemo } from './demo/mode'

describe('broadcast output routing', () => {
  it('routes hosted preview and transparent overlay without changing normal hosted mode', () => {
    expect(broadcastRoute('/broadcast', '/')).toBe('broadcast')
    expect(broadcastRoute('/overlay', '/')).toBe('overlay')
    expect(broadcastRoute('/', '/')).toBeNull()
    const href = broadcastHref('broadcast', '/', '', 'session-1', 'en')
    expect(href).toBe('/broadcast?session=session-1&lang=en')
    expect(usesRecordedDemo(new URL(href, 'http://local').search, false)).toBe(false)
  })

  it('routes both outputs beneath a configured static base', () => {
    expect(broadcastRoute('/MatchShift/broadcast', '/MatchShift/')).toBe('broadcast')
    expect(broadcastRoute('/MatchShift/overlay', '/MatchShift/')).toBe('overlay')
    expect(broadcastRoute('/MatchShift/broadcast/', '/MatchShift/')).toBe('broadcast')
    expect(broadcastRoute('/broadcast', '/MatchShift/')).toBeNull()
  })

  it('keeps judge mode, session and language in preview and overlay links', () => {
    for (const route of ['broadcast', 'overlay'] as const) {
      const href = broadcastHref(route, '/MatchShift/', '?demo=judge&lang=fr&session=old&minute=63', 'judge-session', 'de')
      const url = new URL(href, 'http://local')
      expect(url.pathname).toBe(`/MatchShift/${route}`)
      expect(url.searchParams.get('demo')).toBe('judge')
      expect(url.searchParams.get('session')).toBe('judge-session')
      expect(url.searchParams.get('lang')).toBe('de')
      expect(url.searchParams.get('minute')).toBe('63')
      expect(usesRecordedDemo(url.search, false)).toBe(true)
    }
  })

  it('has a distinct localized preview label and explanation in all six languages', () => {
    expect(t('en').broadcast).toBe('Broadcast Preview')
    expect(new Set(LANGUAGE_CODES.map(lang => t(lang).broadcast)).size).toBe(6)
    for (const lang of LANGUAGE_CODES) {
      expect(t(lang).broadcast.length).toBeGreaterThan(0)
      expect(broadcastCopy[lang].description.length).toBeGreaterThan(20)
      expect(broadcastCopy[lang].note.length).toBeGreaterThan(20)
      expect(broadcastCopy[lang].overlay.length).toBeGreaterThan(0)
    }
  })
})
