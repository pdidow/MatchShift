# Accessibility

MatchShift follows one rule: **the full-featured default experience stays as it is, and accessibility adaptations are
optional and under the user's control.**

- **Default:** full animation, the intro splash, the 3D fly-in, the standard broadcast palette and text size, and no speech.
- **Built in for everyone:** structural accessibility. This means semantic labels, keyboard access, visible focus,
  dialog focus management and screen-reader live regions. None of it changes how the page looks or sounds unless
  you use it.
- **Optional:** every sensory or presentation change is off until you turn it on in the **Accessibility Center**
  (the *Accessibility* button in the top bar). Each option is independent and is saved in the browser
  (`localStorage`, key `matchshift.a11y`).

Visual adaptations are applied as `data-*` attributes on `<html>`, and only while an option is on:
`data-contrast`, `data-cvd`, `data-text`, `data-reduced-motion` and `data-reduce-flash`. With default settings
the page has none of them, so the default styling is untouched. This is covered by a unit test and a browser check.

The one input taken from the operating system is `prefers-reduced-motion`. The Reduced motion option defaults to
*Follow system setting*, and an explicit *On* or *Off* overrides it.

## Accessibility Center

| Group | Option | Default |
|---|---|---|
| Presets | Default experience · Low vision · Audio-first · Reduced motion · Custom (shown when your options match no preset) | Default |
| Audio & speech | Spoken insights | Off |
| | Speech detail: Major moments only / Important insights / Detailed commentary | Important |
| | Audio match mode | Off |
| | Spoken recaps: Off / Every 5 min / Every 10 min / Half-time only / Major tactical changes only | Off |
| | 3D replay audio description | Off |
| | Describe visuals (adds DESCRIBE THIS to SHOW ME and READ buttons to WHY?) | Off |
| | Speech speed (0.5–2×) and volume | 1×, 100% |
| | Sound cues | Off |
| | Test voice | – |
| Display | Captions & transcript | Off |
| | High contrast | Off |
| | Colour vision mode: Default / Deuteranopia / Protanopia / Tritanopia / Monochrome-pattern | Default |
| | Text size: Normal / Large / Extra large | Normal |
| Motion & effects | Reduced motion: Follow system / On / Off | Follow system |
| | Reduce flashing / intense effects | Off |
| Content | Simplified insights (with MORE DETAIL) | Off |
| Help | Keyboard shortcuts | – |

Presets always start from the defaults, so picking one never leaves unrelated options on. Speech speed and volume
are comfort settings and are kept.

- **Low vision:** large text and high contrast.
- **Audio-first:** spoken insights, audio match mode, replay audio description, describe visuals and transcript.
- **Reduced motion:** reduced motion on and reduce flashing on.

When any option is on, the Accessibility button shows a small count and lists the active options in its accessible
name and tooltip. Otherwise it looks like any other chip.

There is also an **Accessible audio** viewer persona in the *Viewer* menu. It applies the Audio-first options and uses
the concise *What Matters* wording. Leaving it restores your previous options. Accessibility also works in every other
persona, because the options are independent of the persona.

## Speech

Speech uses the browser's Web Speech API (`speechSynthesis`) through one queue (`src/a11y/speech.ts`):

- **One voice at a time**, highest priority first. Critical items are goals, dangerous turnovers, half-time and
  full-time. Normal-priority items are most insights, WHY? reading and status. Low-priority items are minor insights
  and periodic recaps.
- **No repeats.** Each insight has a key and is spoken at most once. A reset or seek clears the keys.
- **Critical items interrupt** lower-ranked speech. Interrupted speech is never resumed.
- **Semantic rank.** Ranks come from match event semantics only, never from AI:
  goal = match boundary (half-time, full-time) > major insight (high severity, accepted by the Verification Agent)
  > other critical > normal > low.
- **Goals and boundaries are never silently lost.**
  - A goal interrupts any speech below a goal, such as a key moment or a major insight.
  - A goal that arrives while another goal is speaking waits. If a further goal arrives, it is merged into the waiting
    goal and both are spoken in one cue, so there is still only one waiting goal.
  - Half-time and full-time interrupt only non-critical speech and never cut a goal. Behind a critical sentence they
    wait.
  - Goals and boundaries waiting behind a speaking sentence keep a bounded grace of 15 real seconds after their
    match-time window passes. After that they go to the transcript only.
  - A full-time announcement replaces a half-time one that is still waiting.
  - Ordinary cues get no grace and never wait behind a goal or boundary.

### Speech follows the timeline

The timeline is authoritative: the match never waits for speech, and speech never runs behind the match. MatchShift
**speaks less, not later**.

**Normal match.**

- Every match cue carries the match second it describes. A cue is spoken only while the match is still within its
  window: 45 match-seconds for low, 90 for normal and 180 for critical. Anything older goes to the transcript
  unspoken.
- The queue is bounded: at most one cue waits per source (match, replay, or the viewer's own requests). A newer cue
  replaces a waiting one unless the waiting one is more important.
- A sentence that has started is allowed to finish. A reset, seek or new match stops it.
- While the match is paused, its waiting cues do not start. The viewer's own requests (READ EXPLANATION, Repeat,
  Match status, Test voice) still speak. Full time is still announced although the match clock has stopped.
- **Speech is never sped up to match playback.** The speech rate is the viewer's own setting at every speed. Faster
  playback changes how much is said:

| Playback | What is spoken |
|---|---|
| 1× | The viewer's chosen detail level; every cue that passes it. |
| 2× (default) | Concise. Minute, type, team and headline (the *Major* level, about 4 s of speech instead of about 12 s with the fact sentence, measured in Chrome). Stale cues are dropped. |
| 5× | Key events and meaningful insights only (normal and critical priority), concise. |
| 10× | Critical moments only: goals, dangerous turnovers, half-time and full-time. |

  Bursts during fast playback are also trimmed to the three most important insights before they reach the queue.

**Evidence Replay.** Opening the replay (from a card, WHY?, SHOW ME, a highlight or the guided demo) does the
following:

- pauses the underlying match if it was playing;
- stops and clears any match narration;
- gives the replay exclusive audio focus. New match cues go to the transcript only while it is open.

Replay narration then follows the **replay clock**:

- each event is spoken only within a few replay-seconds of its time;
- pausing the replay starts no new narration;
- a seek or restart drops what is waiting and describes the event now on screen;
- faster replay speeds narrate only supporting evidence, goals and BEFORE/AFTER transitions.

Closing the replay:

- stops replay speech and releases focus;
- never resumes earlier match narration;
- resumes the match only if it was playing before (and is still the same match), at the same speed.

The crowd is visual only and never speaks or plays sound.

- **Controls:** Mute, Pause/Resume (only while speaking), Repeat last (only once something has been spoken),
  Match status (in Audio match mode) and Transcript.
- **Language-aware.** Speech is the viewer's browser Web Speech API (`speechSynthesis`); the server never makes audio,
  so voices come from the viewer's browser/OS and nothing is installed on Azure. Every utterance sets `lang` to en-GB,
  fr-FR, es-ES, de-DE, it-IT or pt-PT to match the UI language, and the voice is chosen as: exact locale, then any voice of the same language
  (platform default first, then a local voice), else no explicit voice so the browser picks one for that `lang`.
  A voice of another language is never chosen explicitly. Voices that load late are picked up via `voiceschanged`.
  If the device has no voice for the language, speech still runs with the right `lang` and a notice says so.
  Switching language drops queued narration in the old language and stops it if it is playing. Spoken text itself is
  localised (insight narratives, Accessibility Narrator output, WHY?, status and replay audio description). If the
  browser has no speech synthesis, everything is still shown as captions.
- **Transcript.** Every message, whether spoken or muted, is kept with its time, type and text, up to the last 200.
  Selecting an entry speaks it again.

What is said is generated **only from structured data**: the insight's own narrative (which comes from deterministic
evidence) and the numbers in the analytics. The speech layer computes no statistics.

| Feature | What is spoken |
|---|---|
| Spoken insights | `63'. Match shift, Westhaven.` then the headline. *Important* adds the fact. *Detailed* adds the inference and explanation. With Simplified on, it speaks the plain-language sentence. Only insights that arrive while you watch are spoken; the existing list is never read out as a backlog. |
| Audio match mode | Goals with the score, half-time and full-time summaries, and an immediate status when switched on. The status gives the time, the score, who has had control in the last ten minutes and which side they attack down. |
| Spoken recaps | The same status every 5 or 10 minutes, at half-time only, or after major tactical changes. Seeking backwards never triggers one. |
| WHY? → READ EXPLANATION | Headline, fact, inference, possible explanation, confidence and the top supporting evidence (before → now, with windows). |
| WHY? → READ WHAT CHANGED | "At minute 63, MatchShift detected a change. Before the change, Attacks down the right (share) was 31% between 0' and 50'. Afterwards it was 90% between 50' and 62'." |
| SHOW ME → DESCRIBE THIS | Counts from the plotted events (actions, passes and carries, final-third entries, shots, turnovers), the minute range and the dominant side. The text is also shown under the pitch. |

## 3D replay accessibility

- **Audio description** (off by default; toggle in the Accessibility Center, with the *Audio description* button, or
  with the **D** key).
  - Events are narrated when the replay clock crosses them, so narration stays in sync. Examples: "Castellane
    (Number 4) presses and intercepts high up the pitch, on the left, midfield."; "Haddadi (Number 11) cuts the ball
    back to Blackwood (Number 9)."; "Blackwood shoots from about 9 metres. Blackwood scores!"
  - Descriptions use the attacking team's frame: forward or back, left, centre or right, the zone (own third,
    midfield, attacking third, penalty area) and distance to goal, all taken from event coordinates.
  - A seek drops narration that has not been spoken yet and describes the event now on screen. A clip change
    announces BEFORE/AFTER and the clock. Closing the replay stops narration.
- **Event list** (*Event list* button or **L**): an ordered list of buttons, one per event, with its clock and spatial
  description. Selecting one seeks the replay to it. The current event is marked `aria-current="step"`. This is the
  screen-reader way through a replay.
- **Keys:** Space play/pause, ←/→ previous/next event, Home restart, C cycle camera, 1–4 choose a camera,
  D toggle audio description, R repeat the current event's description, L event list, Esc close.
  - Keys are ignored while typing in an input, select or textarea, and whenever Ctrl, Alt or Cmd is held, so browser
    shortcuts still work.
  - Space on a focused button activates that button instead.
- **Scrubber:** a labelled slider with a time value; arrow keys move one second and End jumps to the end.
- **Dialog:** focus moves into the replay, Tab stays inside it, and Esc closes it and returns focus to where it came from.
- The current-event caption is deliberately **not** a live region, because it changes several times a second.

## Reduced motion

When it is on (explicitly, or because the system asks for it under *Follow system*):

- No intro splash, no card entrance animation, no transitions, and no title-card movement.
- In 3D there is no fly-in, even on first open. Markers appear without a pop-in.
- The camera uses **direct cuts** instead of smooth pans. The shot holds still and cuts only on a camera, clip or seek
  change, or when the action would leave the frame.
- **The replay itself is kept:** players and ball still move, because the movement is the information.
- The optional Evidence Replay crowd stays static: its group reactions to goals, shots and turnovers are disabled, whatever
  the hardware level.
- The splash is decided once at page load, so changing the option later never re-plays it.

## Flashing reduction

With Reduce flashing on, highlights are steady instead of pulsing:

- In 3D, the actor and pressing-player glow rings and the channel and zone overlays hold a constant level instead
  of pulsing.
- The speaking indicator does not blink.
- BEFORE/AFTER title cards fade without movement.

No part of MatchShift flashes more than three times per second in either mode.

## Colour vision modes

Each mode changes the two team colours through CSS variables, so everything that uses them follows: pitch, charts,
metrics, legends, the scoreboard and the 3D replay. Each mode also adds non-colour coding, so team identity never
depends on colour alone.

| Mode | Home | Away | Validator (dark surface `#0E1420`) |
|---|---|---|---|
| Default | `#4c7bf4` | `#e0623b` | worst CVD ΔE 27.6, all checks pass |
| Deuteranopia | `#3b82f6` | `#c4820f` | worst CVD ΔE 30.3 (protan), all checks pass |
| Protanopia | `#4c7bf4` | `#b98a10` | worst CVD ΔE 30.9 (protan), all checks pass |
| Tritanopia | `#e0525a` | `#1e9fac` | worst CVD ΔE 12.9 (deutan) and 33.7 (tritan), all checks pass |
| Monochrome / pattern | `#e6ebf2` | `#7d8798` | Intentionally achromatic: lightness ΔE 31.8. Identity is carried by pattern. |

Patterns, shapes and labels, active in every non-default mode:

- **Pitch:** away events are squares (home are circles) and away routes are dashed. Away channel shading is stronger.
- **Match Control chart:** the away area is hatched. The legend swatch is hatched.
- **Pressure chart:** the away line is dashed, and so is its legend swatch.
- **Control bar, attack-side bars and pressure bars:** away segments are hatched.
- **Control vs Chaos meter:** textured, with the CONTROLLED / BALANCED / CHAOTIC labels underneath. In monochrome it
  becomes a light-to-dark ramp.
- **Team swatches** in the scoreboard: the away swatch is square-cornered and hatched.
- **3D replay:** away players get diamond rings and square number badges (home keep round ones).
- **Confidence:** always dots plus a text label, never colour alone.
- **Insight type:** text labels. In monochrome, shift cards also get a double left border.

## High contrast and larger text

- **High contrast:** a pure black surface and white ink, lighter borders and gridlines, thicker chart lines,
  2 px card borders and a 3 px yellow focus ring. Overlays in the 3D replay become solid.
- **Text size:** Large zooms the app chrome to 118% and Extra large to 140%: the top bar, main area, timeline,
  dialogs, speech bar and the replay controls and captions. Extra large stacks the layout into a single scrolling
  column. The 3D canvas is not zoomed.

## Screen readers and keyboard (always on)

- **Live regions:** one polite and one assertive region are shared by the whole app.
  - A new insight is announced once and concisely, assertively only for goals and dangerous turnovers.
  - Goals with the score and full time are announced.
  - The same text is not repeated within 5 s.
  - When Spoken insights is on, the live region does not also announce insights, so they are not heard twice.
- **Not live regions:** the scoreboard (the clock ticks every second) and the 3D current-event caption. The scoreboard
  has an accessible name with both scores and the clock instead.
- **Insight cards** are focusable. Enter or Space selects one, and they are labelled by their headline. Buttons inside
  keep their own keys.
- **Tabs** follow the WAI-ARIA pattern: ←/→/Home/End move between tabs with roving `tabindex`, and the panel is
  labelled by its tab.
- **Dialogs** (Accessibility Center, WHY?, Keyboard shortcuts, 3D replay) are modal. Focus moves in, Tab is trapped,
  Esc closes, and focus returns to where it was.
- **Other labels:** the evidence table has a caption and column headers; the charts, attack-side bars and Chaos meter
  have text equivalents; switches are `role="switch"` with descriptions.
- **?** opens keyboard help from anywhere except text fields.

## Sound cues

Sound cues are off by default. They are short synthesised tones (Web Audio, no audio files) for an insight, a tactical
shift, a turnover and a goal. A cue only ever accompanies information that is also shown and announced, and it follows
the speech volume.

## Testing

Automated:

```bash
cd src/MatchShift.Web
npm test          # vitest: 23 accessibility tests (+ 12 replay maths)
```

The accessibility unit tests are in `src/a11y/a11y.test.ts`. They cover:

- Defaults are off and produce no `<html>` attributes.
- System reduced motion, presets, option independence, persistence and garbage-tolerant loading.
- The speech queue: priority, dedupe, stale discard, interrupt, mute, repeat, a missing voice, clearing a category,
  rate and volume, and running without synthesis.
- Audio synchronisation and focus (`src/a11y/audioSync.test.ts`):
  - match-clock relevance, the bounded queue and preemption;
  - the speed policy at 1×, 2×, 5× and 10× (the speech rate is never scaled);
  - goal and boundary protection (`src/a11y/audioPriority.test.ts`): semantic rank, goal preemption and merging,
    half-time and full-time grace, bounded queue, and a four-goal match at 10×;
  - pause and full time;
  - Evidence Replay focus: pause, cancel, replay clock, seek, restart, speed, close and restore;
  - all six languages, and a check that the crowd is silent.
- Description helpers: detail levels, simplified text, WHY? and what-changed narration, match status, recap
  scheduling, team-frame spatial language and the SHOW ME summary.

Manual testing, starting with the default mode:

1. Start the app (`dotnet run --project src/MatchShift.Api`) and open it in a fresh browser profile. If you have used
   it before, run `localStorage.removeItem('matchshift.a11y')`.
2. Check the defaults:
   - The splash shows, insight cards animate in, the 3D replay flies in on first open, and the colours are blue/orange.
   - Nothing is spoken and there is no speech bar.
   - In DevTools, `<html>` has no `data-contrast`, `data-cvd`, `data-text`, `data-reduced-motion` or
     `data-reduce-flash` attribute.
3. Use the keyboard only:
   - Tab to the Accessibility button and press Enter. The dialog opens, focus moves into it and Tab cycles inside it.
     Esc closes it and focus returns to the button.
   - Tab to an insight card and press Enter to select it.
   - On the tab list, use ←/→.
   - Press **?** for help.

Then test the accessibility modes. Each option is independent, so turn one on, check it, turn it off and check that
the default is restored.

1. **Spoken insights** (set detail to *Detailed*) **plus Captions & transcript.**
   - Press Play at 10×. New insights are spoken one at a time and captioned.
   - Open Transcript, select an entry and check it is spoken again. Try Repeat last, Mute and Pause.
   - Seek: nothing from the past is read out.
2. **Audio match mode.** It speaks a status straight away. Goals are announced with the score; half-time and
   full-time are announced too.
3. **Spoken recaps:** try *Every 5 minutes* during playback.
4. **Describe visuals.**
   - Use WHY? on the 63' *Match shift* card, then READ EXPLANATION and READ WHAT CHANGED.
   - Use SHOW ME, then DESCRIBE THIS; the text also appears under the pitch.
5. **3D replay audio description.**
   - Open 3D REPLAY on the 35' *Key moment* card. Events are narrated as they happen.
   - Press **L** for the event list and select an event to seek.
   - Try **R**, **D**, **C**, **Home** and **Space**.
   - Press Space with a replay button focused: it activates that button and does not toggle playback.
6. **High contrast**, **Text size** Large and Extra large, and the **Colour vision** modes. Check the pitch shapes and
   dashes, the hatched bars and charts, and the 3D diamond rings and square badges.
7. **Reduced motion.**
   - Reload: there is no splash and cards do not animate in.
   - Open a replay: there is no fly-in and the camera cuts instead of panning, but the players still move.
   - Also test *Follow system* with the OS "reduce motion" setting.
8. **Reduce flashing:** in the replay, the glow rings and overlays are steady.
9. **Simplified insights:** plain sentences appear; MORE DETAIL expands one card.
10. **Presets:** Low vision, Audio-first, Reduced motion, Default. Changing any option shows *Custom*.
11. **Accessible audio persona** (Viewer menu). Switch back to *Tactical analyst* and check that your previous options
    return.
12. **Language:** switch to Français, Español, Deutsch, Italiano or Português (de-DE, it-IT, pt-PT); speech uses that language and voice, and narration queued in the
    previous language stops. Without a voice for that language on the device, a notice shows and the browser picks a
    voice for the requested `lang`. (Verified on the hosted app in Chrome on Windows: en-GB → Microsoft George,
    fr-FR → Google français, es-ES → Google español; French and Spanish replay audio description.)
    German, Italian and Portuguese were then checked in Chrome on Windows (local build): de-DE → Google Deutsch,
    it-IT → Google italiano; this device has no pt-PT voice, so pt-PT used Google português do Brasil (same language,
    utterance `lang` still pt-PT). With every Portuguese voice hidden, the utterance kept `lang` pt-PT with no explicit
    voice (never an English one) and the localized no-voice notice appeared.
13. **Combinations to spot-check:**
    - High contrast + Extra large + Monochrome.
    - Reduced motion + Audio description in the replay.
    - Spoken insights + Simplified + French.
    - Audio match mode + Spoken insights: each goal is spoken once, by Audio match mode.

## Limitations

- **Time Travel:** MatchShift has no separate Time Travel view. *READ WHAT CHANGED* applies to every insight with
  before/after evidence (the shift insights in WHY?), and the 3D replay's BEFORE/AFTER clips are announced and
  narrated. Insights without a baseline, such as a goal, do not offer it.
- **Voices:** speech quality and the available languages depend on the voices installed in the browser and OS. In
  headless or locked-down browsers there may be no voices; text is used instead.
- **Detail level:** Simplified sentences and audio descriptions are template-based and deliberately plain. The
  insight narrative remains the detailed source.
- **3D for screen readers:** the WebGL canvas itself is not accessible to screen readers. The event list, audio
  description and captions are the accessible equivalent.
- **Text size zoom** relies on CSS `zoom`, which is supported in current Chromium, Firefox (126+) and Safari.
- **Broadcast view:** the transparent OBS overlay (`/broadcast`) is a broadcast graphic and has no Accessibility
  Center.
- **Sound cues** need a user interaction before browsers allow audio. The first cue may be silent until you have
  clicked or pressed a key on the page.
