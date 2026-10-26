/** Audio only travels through our backend. No Azure credentials or SDK in the browser. */
export interface CloudSpeech {
  play(text: string, locale: string, rate: number, volume: number, signal: AbortSignal, start: () => void, current: () => boolean): Promise<void>
  pause(): void
  resume(): void
}

export class BrowserCloudSpeech implements CloudSpeech {
  private context: AudioContext | null = null
  private cache = new Map<string, AudioBuffer>()
  private getContext() { return this.context ??= new AudioContext() }

  /** Called in the existing interaction gesture, before the asynchronous synthesis request. */
  unlock = () => { void this.getContext().resume().catch(() => {}) }
  pause() { if (this.context) void this.context.suspend() }
  resume() { if (this.context) void this.context.resume() }

  async play(text: string, locale: string, rate: number, volume: number, signal: AbortSignal, start: () => void, current: () => boolean) {
    const context = this.getContext()
    if (context.state !== 'running') throw new Error('audio-interaction-required')
    const key = `${locale}:${text}`
    let buffer = this.cache.get(key)
    if (!buffer) {
      const response = await fetch('/api/speech', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, locale }), signal,
      })
      if (!response.ok) throw new Error(`speech-service-${response.status}`)
      buffer = await context.decodeAudioData(await response.arrayBuffer())
      if (signal.aborted) return
      if (this.cache.size >= 16) this.cache.delete(this.cache.keys().next().value!)
      this.cache.set(key, buffer)
    }
    // Network latency must not turn stale replay events into late narration.
    if (signal.aborted || !current()) return
    await new Promise<void>((resolve, reject) => {
      const source = context.createBufferSource()
      const gain = context.createGain()
      source.buffer = buffer
      source.playbackRate.value = Math.max(0.5, Math.min(2, rate))
      gain.gain.value = Math.max(0, Math.min(1, volume))
      source.connect(gain).connect(context.destination)
      const abort = () => { source.stop(); finish() }
      const cleanup = () => {
        signal.removeEventListener('abort', abort)
        source.disconnect(); gain.disconnect()
      }
      const finish = () => { cleanup(); resolve() }
      source.onended = finish
      signal.addEventListener('abort', abort, { once: true })
      try { source.start(); start() } catch (error) { cleanup(); reject(error) }
    })
  }
}

export function browserCloudSpeech(): BrowserCloudSpeech {
  const transport = new BrowserCloudSpeech()
  // The shared queue lives for the page lifetime. No audio is synthesized by this gesture handler.
  window.addEventListener('pointerdown', transport.unlock, { passive: true })
  window.addEventListener('keydown', transport.unlock, { passive: true })
  return transport
}
