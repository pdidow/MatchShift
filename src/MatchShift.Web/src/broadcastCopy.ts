import type { Lang } from './languages'

export const broadcastCopy: Record<Lang, { description: string; note: string; overlay: string }> = {
  en: {
    description: 'Preview of MatchShift broadcast-style graphics. No live video is transmitted.',
    note: 'Use the transparent overlay output as a browser-source style graphics layer.',
    overlay: 'Overlay output',
  },
  fr: {
    description: 'Aperçu des graphismes de diffusion MatchShift. Aucune vidéo en direct n’est transmise.',
    note: 'Utilisez la sortie transparente comme calque graphique de type source de navigateur.',
    overlay: 'Sortie transparente',
  },
  es: {
    description: 'Vista previa de gráficos de emisión de MatchShift. No se transmite vídeo en directo.',
    note: 'Use la salida transparente como capa gráfica de tipo fuente de navegador.',
    overlay: 'Salida transparente',
  },
  de: {
    description: 'Vorschau der MatchShift-Sendegrafiken. Es wird kein Live-Video übertragen.',
    note: 'Nutzen Sie die transparente Ausgabe als Grafikebene für eine Browserquelle.',
    overlay: 'Transparente Ausgabe',
  },
  it: {
    description: 'Anteprima della grafica televisiva di MatchShift. Non viene trasmesso video in diretta.',
    note: 'Usa l’output trasparente come livello grafico per una sorgente browser.',
    overlay: 'Output trasparente',
  },
  pt: {
    description: 'Pré-visualização dos gráficos de transmissão do MatchShift. Não é transmitido vídeo em direto.',
    note: 'Use a saída transparente como camada gráfica para uma fonte de navegador.',
    overlay: 'Saída transparente',
  },
}
