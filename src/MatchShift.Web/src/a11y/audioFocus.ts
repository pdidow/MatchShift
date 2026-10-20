// Evidence Replay takes the audio and the clock while it is open. One small, deterministic helper so every way of
// opening it (card, WHY?, SHOW ME, highlights, the guided demo) behaves the same.

export interface MatchPlayback { matchId: string; epoch: number; isPlaying: boolean }

export interface ReplayFocusDeps {
  speech: { acquireReplayFocus(): void; releaseReplayFocus(): void }
  pause: () => void
  play: () => void
}

export class ReplayAudioFocus {
  private hold: { resume: boolean; key: string } | null = null

  get active() { return this.hold !== null }

  /**
   * Replay opens: remember whether the match was playing, pause it, stop and clear match narration and give the replay
   * exclusive focus. Opening again while already open changes nothing.
   */
  open(match: MatchPlayback | null, deps: ReplayFocusDeps) {
    if (this.hold) return
    this.hold = { resume: !!match?.isPlaying, key: keyOf(match) }
    deps.speech.acquireReplayFocus()
    if (match?.isPlaying) deps.pause()
  }

  /**
   * Replay closes: release focus (earlier match narration is not resumed) and restore play/pause. The match resumes only
   * if it was playing and is still the same match and epoch. The playback speed is never changed.
   */
  close(match: MatchPlayback | null, deps: ReplayFocusDeps) {
    if (!this.hold) return
    const { resume, key } = this.hold
    this.hold = null
    deps.speech.releaseReplayFocus()
    if (resume && key === keyOf(match)) deps.play()
  }
}

const keyOf = (m: MatchPlayback | null) => (m ? `${m.matchId}:${m.epoch}` : '')
