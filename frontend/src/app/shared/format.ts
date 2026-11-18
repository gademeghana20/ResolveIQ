// How dashboard numbers are written for people.

/** 45 -> "45 min", 200 -> "3 h 20 min", 3000 -> "2 d 2 h". */
export function formatDuration(totalMinutes: number): string {
  const minutes = Math.max(0, Math.round(totalMinutes));

  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    const rest = minutes % 60;
    return rest ? `${hours} h ${rest} min` : `${hours} h`;
  }

  const days = Math.floor(hours / 24);
  const restHours = hours % 24;
  return restHours ? `${days} d ${restHours} h` : `${days} d`;
}

/** Average hours -> a short readable time, e.g. 4.5 -> "4 h 30 min". */
export function formatHours(hours: number): string {
  return formatDuration(hours * 60);
}

/** 83.333 -> "83%" (rounded to a whole percent). */
export function formatPercent(value: number): string {
  return `${Math.round(value)}%`;
}

/** "2026-11-17" -> "Nov 17" (read as a plain date, so the time zone cannot shift it a day). */
export function formatDay(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

/** Rounds a maximum up to a tidy axis top, with tick marks at whole numbers: 7 -> {top: 8, step: 2}. */
export function niceAxis(maxValue: number, wantedTicks = 4): { top: number; step: number } {
  if (maxValue <= 0) {
    return { top: wantedTicks, step: 1 };
  }

  const rough = maxValue / wantedTicks;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const fraction = rough / magnitude;
  const niceFraction = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
  const step = Math.max(1, niceFraction * magnitude); // incident counts are whole numbers

  return { top: Math.ceil(maxValue / step) * step, step };
}

/** "2026-11-17T18:00:00Z" -> "just now", "5 min ago", "3 h ago", "2 d ago"; older than a week it is the date. */
export function timeAgo(isoDateTime: string, now: number = Date.now()): string {
  const minutes = Math.floor((now - new Date(isoDateTime).getTime()) / 60_000);

  if (minutes < 1) {
    return 'just now';
  }
  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours} h ago`;
  }

  const days = Math.floor(hours / 24);
  return days < 8 ? `${days} d ago` : new Date(isoDateTime).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}
