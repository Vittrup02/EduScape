import type { TFunction } from 'i18next'
import type { TeamColor } from '../api/sessions'

/** "Hold Rød" / "Team Red": team names are their colour, stored as a key and translated here. */
export function teamName(t: TFunction, color: TeamColor): string {
  return t('teams.name', { color: t(`teamColors.${color}`) })
}

/** Whole minutes, e.g. "7 minutter" (da) or "7 minutes" (en), for the chosen language. */
export function formatMinutes(minutes: number, language: string): string {
  return new Intl.NumberFormat(language, { style: 'unit', unit: 'minute', unitDisplay: 'long' }).format(minutes)
}

/** Date and time, e.g. "20. okt. 2026 04.57" (da) or "Oct 20, 2026, 4:57 AM" (en), for the chosen language. */
export function formatDateTime(iso: string, language: string): string {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))
}
