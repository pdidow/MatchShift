import type { Strings } from '../i18n'
import type { Coverage, EventRole } from './provenance'
import { ROLE_MARK, hasRecordedPosition, percent } from './provenance'
import type { Provenance, ReplayEvent } from './types'

// The trust layer of Evidence Replay: what is evidence, what is context, what is visualization. Rendered from the
// replay sequence only (see provenance.ts), so camera, speed, crowd and animation can never change it.

/** Header: the insight's evidence counts (as on the card, WHY?, SHOW ME and Agents) and what these clips show of them. */
export function EvidenceSummary({ cover, clipIndex, s }: { cover: Coverage; clipIndex: number; s: Strings }) {
  const u = s.replayUi
  const clip = cover.clips[clipIndex]
  return (
    <>
      <p className="replay-about">{u.about}</p>
      <p className="replay-counts">
        <span><span aria-hidden>{ROLE_MARK.supporting} </span>{u.supporting.replace('{n}', String(cover.supporting))}</span>
        {cover.context > 0 && <span><span aria-hidden>{ROLE_MARK.context} </span>{u.context.replace('{c}', String(cover.context))}</span>}
        <span className="replay-window">{u.window.replace('{shown}', String(cover.supportingShown)).replace('{n}', String(cover.supporting))}
          {cover.context > 0 && cover.contextShown === 0 ? ` ${u.contextNotShown}` : ''}</span>
      </p>
      {clip && <span className={`replay-chip ${clip.supporting > 0 ? 'evidence' : 'comparison'}`}>
        {clip.supporting > 0 ? u.clipEvidence.replace('{n}', String(clip.supporting)) : u.clipComparison}</span>}
    </>
  )
}

/** The current event's role in words (and shape), that it is a recorded event, and that movement is visualized. */
export function EventStatus({ event, role, s }: { event: ReplayEvent; role: EventRole; s: Strings }) {
  const u = s.replayUi
  return (
    <>
      <span className={`replay-role role-${role}`}>{ROLE_MARK[role] && <span aria-hidden>{ROLE_MARK[role]} </span>}{u.status[role]}</span>
      <span className="replay-source">{u.recorded} · {hasRecordedPosition(event) ? u.positionAtEvent : u.positionNotRecorded}</span>
      <span className="replay-source">{u.between}</span>
    </>
  )
}

/** Role tag in the event list (supporting evidence and context only; other play is unmarked, as the list key says). */
export function RoleTag({ role, s }: { role: EventRole; s: Strings }) {
  if (role === 'other') return null
  return <small className={`replay-role role-${role}`}> <span aria-hidden>{ROLE_MARK[role]} </span>{s.replayUi.status[role]}</small>
}

/** The persistent line plus "About this replay": how positions, the ball, cameras and the crowd relate to the data. */
export function AboutReplay({ share, event, role, s }: { share: Record<Provenance, number> | null; event: ReplayEvent | null; role: EventRole | null; s: Strings }) {
  const u = s.replayUi
  return (
    <>
      <p className="replay-notice"><strong>{u.noNewEvidence}</strong> {u.aboutEvidence}</p>
      <details className="replay-about-more">
        <summary>{u.aboutTitle}</summary>
        <ul>
          {share && <li>{u.aboutShare.replace('{actual}', percent(share.ACTUAL_EVENT_POSITION)).replace('{interpolated}', percent(share.INTERPOLATED_POSITION)).replace('{estimated}', percent(share.ESTIMATED_POSITION))}</li>}
          <li>{u.aboutBall}</li>
          <li>{u.aboutCamera}</li>
          <li>{u.aboutCrowd}</li>
        </ul>
        <details className="replay-tech">
          <summary>{u.technical}</summary>
          <dl>
            {event && <><dt>{u.eventId}</dt><dd><code>{event.eventId}</code> · {event.clock} · {event.type} · {role}</dd></>}
            <dt>{u.provenanceValues}</dt><dd><code>ACTUAL_EVENT_POSITION</code> · <code>INTERPOLATED_POSITION</code> · <code>ESTIMATED_POSITION</code></dd>
          </dl>
        </details>
      </details>
    </>
  )
}
