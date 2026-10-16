// Mirrors MatchShift.Analytics.Replay (server). Coordinates are ABSOLUTE 0-100: the home team attacks towards X = 100.

export type Provenance = 'ACTUAL_EVENT_POSITION' | 'INTERPOLATED_POSITION' | 'ESTIMATED_POSITION'
export type BallMotion = 'Held' | 'Pass' | 'Carry' | 'Shot' | 'Loose'

export interface ReplayPlayer {
  id: string
  teamId: string
  isHome: boolean
  shirtNumber: number
  name: string
  shortName: string
  position: string
  isGoalkeeper: boolean
}

export interface ReplayEvent {
  eventId: string
  sequence: number
  time: number
  endTime: number
  matchSecond: number
  clock: string
  type: string
  outcome: string
  label: string
  teamId: string
  playerId?: string
  relatedPlayerId?: string
  x?: number
  y?: number
  endX?: number
  endY?: number
  qualifier?: string
  isEvidence: boolean
}

export interface TrackSample { t: number; x: number; y: number; provenance: Provenance }
export interface PlayerTrack { playerId: string; samples: TrackSample[] }

export interface BallSegment {
  t0: number; t1: number; motion: BallMotion
  x0: number; y0: number; x1: number; y1: number; z0: number; z1: number
  arcHeight: number; holderId?: string; eventId?: string; provenance: Provenance
}

export interface ReplayOverlay {
  kind: 'Channel' | 'Zone' | 'Marker' | 'PressingPlayers' | 'PassPath'
  teamId?: string
  label?: string
  x0?: number; y0?: number; x1?: number; y1?: number
  playerIds: string[]
  eventIds: string[]
  fromTime: number
  toTime: number
}

export interface ReplayCamera { attackingTeamId: string; attackDirection: 1 | -1; focusPlayerId?: string; centroidX: number; centroidY: number }

export interface ReplayClip {
  key: 'before' | 'after' | 'key'
  label: string
  startSecond: number
  endSecond: number
  duration: number
  scoreAtStart: { home: number; away: number }
  events: ReplayEvent[]
  tracks: PlayerTrack[]
  ball: BallSegment[]
  overlays: ReplayOverlay[]
  camera: ReplayCamera
}

export interface ReplaySequence {
  matchId: string
  insightId: string
  insightType: string
  teamId?: string
  homeTeamId: string
  awayTeamId: string
  insightWindow: { fromSecond: number; toSecond: number }
  evidenceEventIds: string[]
  relevantPlayerIds: string[]
  players: ReplayPlayer[]
  clips: ReplayClip[]
  notice: string
}

export type CameraPreset = 'tactical' | 'sideline' | 'behindGoal' | 'player'
