/** SignalR'ın negotiate sırasında fırlattığı HttpError'un 401 (oturum düşmüş) olup olmadığı. */
export function isAuthError(err: unknown): boolean {
  return typeof err === 'object' && err !== null && (err as { statusCode?: unknown }).statusCode === 401;
}
