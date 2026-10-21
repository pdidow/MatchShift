import { describe, expect, it } from 'vitest'
import { renderToStaticMarkup } from 'react-dom/server'
import { t } from '../i18n'
import { LANGUAGE_CODES } from '../languages'
import { EmptyFeed, FeedIntro } from './Home'

const noop = () => undefined
const en = t('en')
const empty = (state: { playing?: boolean; finished?: boolean; started?: boolean } = {}) =>
  renderToStaticMarkup(<EmptyFeed s={en} onStart={noop} onTour={noop} playing={false} finished={false} started={false} {...state} />)

describe('homepage orientation and playback states', () => {
  it('uses the real localized action labels in evidence order, in all six languages', () => {
    for (const lang of LANGUAGE_CODES) {
      const s = t(lang)
      const html = renderToStaticMarkup(<FeedIntro s={s} onTrust={noop} />)
      const labels = [...html.matchAll(/<strong>(.*?)<\/strong>/g)].map(m => m[1])
      expect(labels).toEqual([s.why, s.showMe, s.replay.button])
      expect(s.home.loop).toHaveLength(3)
      expect(html).not.toMatch(/undefined|NaN|\uFFFD/)
      expect(html).toContain(s.home.trust)
      expect(html).toContain(s.home.trustLink)
    }
    expect(en.tagline).toBe('Football intelligence you can question, verify, and replay.')
  })

  it('offers a start and a tour before kick-off, without claiming to be watching yet', () => {
    expect(empty()).toContain(en.home.waiting)
    expect(empty()).toContain(en.home.start)
    expect(empty()).toContain(en.guide.entry)
    expect(empty()).not.toContain(en.home.watching)
  })

  it('shows a watching status while playing, without a redundant start button', () => {
    const html = empty({ playing: true, started: true })
    expect(html).toContain(en.home.watching)
    expect(html).toContain('role="status"')
    expect(html).not.toContain('feed-start')
  })

  it('resumes a paused match and does not offer to start a finished one', () => {
    expect(empty({ started: true })).toContain(en.play)
    expect(empty({ started: true })).not.toContain(en.home.start)
    const html = empty({ finished: true, started: true })
    expect(html).toContain(en.home.finished)
    expect(html).not.toContain('feed-start')
    expect(html).not.toContain(en.home.watching)
  })

  it('omits duplicate tour entry while a guide is already running', () => {
    const html = renderToStaticMarkup(<EmptyFeed s={en} onStart={noop} playing={false} finished={false} started={false} />)
    expect(html).not.toContain(en.guide.entry)
  })
})
