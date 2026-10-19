import 'i18next'
import type { Translation } from './languages'

// Type-checks every t() key against da.json, so a typo or missing key fails the build.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation'
    resources: { translation: Translation }
  }
}
