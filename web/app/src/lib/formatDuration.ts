/**
 * A video chapter's duration in whole seconds as minutes:seconds, as the chapter form takes it: the minutes not capped
 * at 59, the seconds always two digits (725 reads 12:05). The generated client types it as number | string.
 */
export function formatDuration(seconds: number | string): string {
  const total = Math.max(0, Math.round(Number(seconds)));
  const minutes = Math.floor(total / 60);
  const rest = total % 60;
  return `${minutes}:${String(rest).padStart(2, '0')}`;
}
