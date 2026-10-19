import type { TFunction } from 'i18next'
import type { TeamColor } from '../api/sessions'

/** "Hold Rød" / "Team Red": team names are their colour, stored as a key and translated here. */
export function teamName(t: TFunction, color: TeamColor): string {
  return t('teams.name', { color: t(`teamColors.${color}`) })
}

/** A clock time such as "14.30" (da) or "2:30 PM" (en), formatted for the chosen language. */
export function formatTime(iso: string, language: string): string {
  return new Intl.DateTimeFormat(language, { hour: '2-digit', minute: '2-digit' }).format(new Date(iso))
}
