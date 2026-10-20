import { useTranslation } from 'react-i18next'

const studentKeys = [
  'privacy.students.account',
  'privacy.students.nickname',
  'privacy.students.token',
  'privacy.students.answers',
  'privacy.students.noRanking',
] as const

const deletionKeys = [
  'privacy.deletion.afterClose',
  'privacy.deletion.sleeping',
  'privacy.deletion.results',
  'privacy.deletion.device',
] as const

const teacherKeys = [
  'privacy.teachers.account',
  'privacy.teachers.content',
  'privacy.teachers.images',
  'privacy.teachers.kept',
] as const

const whereKeys = ['privacy.where.eu', 'privacy.where.noTracking', 'privacy.where.browser'] as const

type PrivacyKey =
  | (typeof studentKeys)[number]
  | (typeof deletionKeys)[number]
  | (typeof teacherKeys)[number]
  | (typeof whereKeys)[number]

function Section({ id, title, items }: { id: string; title: string; items: readonly PrivacyKey[] }) {
  const { t } = useTranslation()
  return (
    <section aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      <ul className="privacy-list">
        {items.map((key) => (
          <li key={key}>{t(key)}</li>
        ))}
      </ul>
    </section>
  )
}

/** "About data": in plain words, what is stored about students and teachers, and when it is deleted. */
export function PrivacyPage() {
  const { t } = useTranslation()
  return (
    <main className="privacy">
      <h1>{t('privacy.title')}</h1>
      <p>{t('privacy.intro')}</p>
      <Section id="privacy-students" title={t('privacy.studentsTitle')} items={studentKeys} />
      <Section id="privacy-deletion" title={t('privacy.deletionTitle')} items={deletionKeys} />
      <Section id="privacy-teachers" title={t('privacy.teachersTitle')} items={teacherKeys} />
      <Section id="privacy-where" title={t('privacy.whereTitle')} items={whereKeys} />
    </main>
  )
}
