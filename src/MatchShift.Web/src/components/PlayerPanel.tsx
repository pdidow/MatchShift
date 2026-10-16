import type { PlayerPerformance, Lang } from '../api'

export const performanceLabels = (lang: string) => lang === 'fr'
  ? { passes: 'Passes', completed: 'Réussies', progressive: 'Progressives', shots: 'Tirs', recoveries: 'Récupérations', turnovers: 'Pertes', long: 'Longs ballons', title: 'Depuis le coup d’envoi', events: 'Événements du joueur' }
  : lang === 'es'
    ? { passes: 'Pases', completed: 'Completados', progressive: 'Progresivos', shots: 'Tiros', recoveries: 'Recuperaciones', turnovers: 'Pérdidas', long: 'Balones largos', title: 'Desde el inicio', events: 'Eventos del jugador' }
    : { passes: 'Passes', completed: 'Completed', progressive: 'Progressive', shots: 'Shots', recoveries: 'Recoveries', turnovers: 'Turnovers', long: 'Long balls', title: 'Since kick-off', events: 'Player events' }

export function PlayerPanel({ player, name, lang }: { player: PlayerPerformance; name: string; lang: Lang }) {
  const s = performanceLabels(lang)
  const totals = [
    [s.passes, player.passes, player.passIds], [s.completed, player.completed, player.completedIds],
    [s.progressive, player.progressive, player.progressiveIds], [s.shots, player.shots, player.shotIds],
    [s.recoveries, player.recoveries, player.recoveryIds], [s.turnovers, player.turnovers, player.turnoverIds],
  ] as const
  return <section className="panel player-panel"><h2>{name} <small>{s.title}</small></h2>
    <div className="player-totals">{totals.map(([label, count, ids]) => <details key={label}>
      <summary>{label}: <strong>{count}</strong></summary><p>{ids.join(', ') || '—'}</p>
    </details>)}</div>
    <details><summary>{s.events} ({player.events.length})</summary>
      <ol className="player-events">{player.events.map(e => <li key={e.id}>
        {Math.floor(e.matchSecond / 60)}′{String(e.matchSecond % 60).padStart(2, '0')} · {e.type} · {e.outcome} <small>{e.id}</small>
      </li>)}</ol>
    </details>
  </section>
}
