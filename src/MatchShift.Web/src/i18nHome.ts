import type { Lang } from './languages'

// First-run wording for the home screen (the insight feed), in all six languages (European Portuguese). Kept short:
// the product, not a landing page.

const en = {
  purpose: 'MatchShift spots meaningful changes in a football match and explains why they matter.',
  loopLabel: 'How it works',
  loop: ['The reasoning', 'Underlying events', 'Selected evidence in motion'],
  trust: 'AI does not decide what happened in the match. The match data does.',
  trustLink: 'How MatchShift verifies insights',
  waiting: 'Start play to follow changes in pressure, passing and match control. Insights appear when the events support them.',
  watching: 'Watching for changes in pressure, passing and match control. Insights appear when the events support them.',
  finished: 'Full time. No meaningful changes were flagged in this match.',
  start: 'Start the match',
  tour: 'Want a quick tour?',
  latest: 'Latest',
  agentsHint: 'How insights are interpreted and verified',
  navigation: 'Match views',
  skip: 'Skip to insights',
}

type Widen<T> = T extends string ? string : T extends readonly (infer U)[] ? readonly Widen<U>[] : T extends object ? { [K in keyof T]: Widen<T[K]> } : T
export type HomeStrings = Widen<typeof en>

const fr: HomeStrings = {
  purpose: 'MatchShift repère les changements significatifs d’un match de football et explique pourquoi ils comptent.',
  loopLabel: 'Comment ça marche',
  loop: ['Le raisonnement', 'Les événements sources', 'Les preuves sélectionnées en mouvement'],
  trust: 'L’IA ne décide pas de ce qui s’est passé pendant le match. Ce sont les données du match.',
  trustLink: 'Comment MatchShift vérifie ses analyses',
  waiting: 'Lancez le match pour suivre la pression, les passes et le contrôle du jeu. Les analyses apparaissent si les événements les justifient.',
  watching: 'Surveillance de la pression, des passes et du contrôle du jeu. Les analyses apparaissent si les événements les justifient.',
  finished: 'Match terminé. Aucun changement significatif n’a été signalé.',
  start: 'Lancer le match',
  tour: 'Une visite rapide ?',
  latest: 'Dernière',
  agentsHint: 'Comment les analyses sont interprétées et vérifiées',
  navigation: 'Vues du match',
  skip: 'Aller aux analyses',
}

const es: HomeStrings = {
  purpose: 'MatchShift detecta cambios significativos en un partido de fútbol y explica por qué importan.',
  loopLabel: 'Cómo funciona',
  loop: ['El razonamiento', 'Los eventos de origen', 'Evidencia seleccionada en movimiento'],
  trust: 'La IA no decide qué pasó en el partido. Lo deciden los datos del partido.',
  trustLink: 'Cómo verifica MatchShift sus análisis',
  waiting: 'Inicia el partido para seguir la presión, los pases y el control del juego. Los análisis aparecen cuando los eventos los respaldan.',
  watching: 'Observando la presión, los pases y el control del juego. Los análisis aparecen cuando los eventos los respaldan.',
  finished: 'Final del partido. No se señalaron cambios significativos.',
  start: 'Empezar el partido',
  tour: '¿Una visita rápida?',
  latest: 'Último',
  agentsHint: 'Cómo se interpretan y verifican los análisis',
  navigation: 'Vistas del partido',
  skip: 'Ir a los análisis',
}

const de: HomeStrings = {
  purpose: 'MatchShift erkennt wichtige Veränderungen im Fußballspiel und erklärt, warum sie zählen.',
  loopLabel: 'So funktioniert es',
  loop: ['Die Begründung', 'Die Quellereignisse', 'Ausgewählte Belege in Bewegung'],
  trust: 'Die KI entscheidet nicht, was im Spiel passiert ist. Das tun die Spieldaten.',
  trustLink: 'Wie MatchShift Analysen prüft',
  waiting: 'Starten Sie das Spiel und verfolgen Sie Druck, Passspiel und Spielkontrolle. Analysen erscheinen, wenn die Ereignisse sie belegen.',
  watching: 'Druck, Passspiel und Spielkontrolle werden beobachtet. Analysen erscheinen, wenn die Ereignisse sie belegen.',
  finished: 'Abpfiff. Es wurden keine bedeutenden Veränderungen erkannt.',
  start: 'Spiel starten',
  tour: 'Kurze Tour gefällig?',
  latest: 'Neueste',
  agentsHint: 'Wie Analysen gedeutet und geprüft werden',
  navigation: 'Spielansichten',
  skip: 'Zu den Analysen springen',
}

const it: HomeStrings = {
  purpose: 'MatchShift rileva i cambiamenti significativi in una partita di calcio e spiega perché contano.',
  loopLabel: 'Come funziona',
  loop: ['Il ragionamento', 'Gli eventi di origine', 'Prove selezionate in movimento'],
  trust: 'L’IA non decide cosa è successo in partita. Lo decidono i dati della partita.',
  trustLink: 'Come MatchShift verifica le analisi',
  waiting: 'Avvia il gioco per seguire pressione, passaggi e controllo della partita. Le analisi appaiono quando gli eventi le sostengono.',
  watching: 'Osservazione di pressione, passaggi e controllo della partita. Le analisi appaiono quando gli eventi le sostengono.',
  finished: 'Partita finita. Non sono stati segnalati cambiamenti significativi.',
  start: 'Avvia la partita',
  tour: 'Un giro veloce?',
  latest: 'Ultima',
  agentsHint: 'Come le analisi vengono interpretate e verificate',
  navigation: 'Viste della partita',
  skip: 'Vai alle analisi',
}

const pt: HomeStrings = {
  purpose: 'O MatchShift deteta mudanças significativas num jogo de futebol e explica por que são importantes.',
  loopLabel: 'Como funciona',
  loop: ['O raciocínio', 'Os eventos de origem', 'Provas selecionadas em movimento'],
  trust: 'A IA não decide o que aconteceu no jogo. Quem decide são os dados do jogo.',
  trustLink: 'Como o MatchShift verifica as análises',
  waiting: 'Comece o jogo para seguir a pressão, os passes e o controlo do jogo. As análises aparecem quando os eventos as sustentam.',
  watching: 'A observar a pressão, os passes e o controlo do jogo. As análises aparecem quando os eventos as sustentam.',
  finished: 'Fim do jogo. Não foram assinaladas mudanças significativas.',
  start: 'Começar o jogo',
  tour: 'Quer uma visita rápida?',
  latest: 'Mais recente',
  agentsHint: 'Como as análises são interpretadas e verificadas',
  navigation: 'Vistas do jogo',
  skip: 'Saltar para as análises',
}

export const HOME: Record<Lang, HomeStrings> = { en, fr, es, de, it, pt }
