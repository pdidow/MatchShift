import { Logo } from './Panels'

/** Only the initial load: language changes keep the existing match interface mounted. */
export function LoadingScreen({ tagline, badge, message }: { tagline: string; badge: string; message: string }) {
  return (
    <div className="startup-screen" role="status" aria-live="polite">
      <div className="startup-content">
        <div className="startup-brand">
          <Logo size={38} />
          <span className="startup-name">MatchShift</span>
          <span className="startup-badge">{badge}</span>
        </div>
        <p className="startup-tagline">{tagline}</p>
        <div className="startup-progress" aria-hidden="true" />
        <p className="startup-status">{message}</p>
      </div>
    </div>
  )
}
