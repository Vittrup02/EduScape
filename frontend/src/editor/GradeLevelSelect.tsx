import { useTranslation } from 'react-i18next'
import { gradeLevels, isPrimarySchool, type GradeLevel } from '../api/teacher'

/** Grade levels in two groups, so a long list is easier to scan. */
export function GradeLevelSelect({
  id,
  value,
  onChange,
}: {
  id: string
  value: GradeLevel
  onChange: (gradeLevel: GradeLevel) => void
}) {
  const { t } = useTranslation()
  const groups = [
    { label: t('editor.gradeGroups.primary'), levels: gradeLevels.filter(isPrimarySchool) },
    { label: t('editor.gradeGroups.secondary'), levels: gradeLevels.filter((g) => !isPrimarySchool(g)) },
  ]
  return (
    <select
      id={id}
      value={value}
      onChange={(event) => {
        onChange(event.target.value as GradeLevel)
      }}
    >
      {groups.map((group) => (
        <optgroup key={group.label} label={group.label}>
          {group.levels.map((level) => (
            <option key={level} value={level}>
              {t(`gradeLevels.${level}`)}
            </option>
          ))}
        </optgroup>
      ))}
    </select>
  )
}
