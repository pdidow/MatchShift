import { afterEach, describe, expect, it, vi } from 'vitest'
import { chooseVoice, SpeechQueue, type SynthLike, type UtteranceLike, type VoiceLike } from './speech'
import type { CloudSpeech } from './cloudSpeech'

afterEach(() => vi.useRealTimers())
function setup(voices: VoiceLike[] = [], autoStart = true) {
  let current: UtteranceLike | undefined
  let changed = () => {}
  const synth: SynthLike = {
    getVoices: () => voices, speak: vi.fn(u => { current = u; if (autoStart) u.onstart?.() }),
    cancel: vi.fn(), pause: vi.fn(), resume: vi.fn(), addEventListener: (_, callback) => { changed = callback },
  }
  const cloud: CloudSpeech = { play: vi.fn(async (_t, _l, _r, _v, _s, start) => start()), pause: vi.fn(), resume: vi.fn() }
  const queue = new SpeechQueue({ synth, cloud, makeUtterance: text => ({ text, lang: '', rate: 1, volume: 1, voice: null, onend: null, onerror: null }) })
  return { queue, synth, cloud, current: () => current!, load: (next: VoiceLike[]) => { voices = next; changed() } }
}
const french = { text: '15 minutes', lang: 'fr-FR', kind: 'test', source: 'ui' as const }

describe('local speech to server audio fallback', () => {
  it('prefers Canadian French, then France, never English', () => {
    const voices = [{ name: 'English', lang: 'en-US', default: true }, { name: 'France', lang: 'fr-FR' }, { name: 'Canada', lang: 'fr-CA' }]
    expect(chooseVoice(voices, 'fr-FR')?.name).toBe('Canada')
    expect(chooseVoice(voices.slice(0, 2), 'fr-FR')?.name).toBe('France')
    expect(chooseVoice(voices.slice(0, 1), 'fr-FR')).toBeNull()
  })
  it.each(['fr-FR', 'en-GB', 'es-ES', 'de-DE', 'it-IT', 'pt-PT'])('keeps working local %s speech free', locale => {
    const { queue, cloud, synth } = setup([{ name: locale, lang: locale }])
    queue.say({ ...french, lang: locale })
    expect(synth.speak).toHaveBeenCalledOnce()
    expect(cloud.play).not.toHaveBeenCalled()
    queue.dispose()
  })
  it('waits for asynchronous Firefox voices before making a request', async () => {
    vi.useFakeTimers()
    const { queue, cloud, load, synth } = setup()
    queue.say(french)
    await vi.advanceTimersByTimeAsync(300)
    load([{ name: 'French', lang: 'fr-FR' }])
    await vi.advanceTimersByTimeAsync(400)
    expect(synth.speak).toHaveBeenCalledOnce()
    expect(cloud.play).not.toHaveBeenCalled()
    queue.dispose()
  })
  it('sends French locale to Azure when only English is installed, without local English speech', async () => {
    vi.useFakeTimers()
    const { queue, cloud, synth } = setup([{ name: 'English', lang: 'en-GB' }])
    queue.say(french)
    await vi.advanceTimersByTimeAsync(601)
    expect(synth.speak).not.toHaveBeenCalled()
    expect(cloud.play).toHaveBeenCalledWith('15 minutes', 'fr-FR', 1, 1, expect.any(AbortSignal), expect.any(Function), expect.any(Function))
    expect(queue.getState().missingVoice).toBeNull()
    queue.dispose()
  })
  it.each(['voice-unavailable', 'language-unavailable', 'synthesis-unavailable', 'synthesis-failed'])('falls back after %s', async error => {
    const { queue, cloud, current } = setup([{ name: 'French', lang: 'fr-FR' }], false)
    queue.say(french); current().onerror?.({ error })
    await Promise.resolve()
    expect(cloud.play).toHaveBeenCalledOnce()
    queue.dispose()
  })
  it('falls back if the local engine never starts', async () => {
    vi.useFakeTimers()
    const { queue, cloud } = setup([{ name: 'French', lang: 'fr-FR' }], false)
    queue.say(french)
    await vi.advanceTimersByTimeAsync(2501)
    expect(cloud.play).toHaveBeenCalledOnce()
    queue.dispose()
  })
  it('reports cloud errors without crashing', async () => {
    vi.useFakeTimers()
    const { queue, cloud } = setup()
    vi.mocked(cloud.play).mockRejectedValue(new Error('speech-service-503'))
    queue.say(french)
    await vi.advanceTimersByTimeAsync(601)
    expect(queue.getState().error).toBe('synthesis-failed')
    expect(queue.getState().speaking).toBe(false)
    queue.dispose()
  })
  it('aborts pending cloud speech on language switch and ignores its late callbacks', async () => {
    vi.useFakeTimers()
    const { queue, cloud, load, current } = setup()
    vi.mocked(cloud.play).mockImplementation(() => new Promise(() => {}))
    queue.setLanguage('fr'); queue.say(french)
    await vi.advanceTimersByTimeAsync(601)
    const call = vi.mocked(cloud.play).mock.calls[0]
    queue.setLanguage('en')
    expect(call[4].aborted).toBe(true)
    call[5]()
    expect(queue.getState().transcript[0].spoken).toBe(false)
    load([{ name: 'English', lang: 'en-GB' }])
    queue.say({ ...french, lang: 'en', text: '15 minutes' })
    expect(current().lang).toBe('en-GB')
    queue.setLanguage('fr')
    expect(queue.getState().speaking).toBe(false)
    queue.dispose()
  })
})
