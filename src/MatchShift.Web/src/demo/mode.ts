/** Judge URLs use the recorded in-browser demo even when the hosted backend is live. */
export function usesRecordedDemo(search: string, staticBuild: boolean): boolean {
  return staticBuild || new URLSearchParams(search).get('demo') === 'judge'
}
