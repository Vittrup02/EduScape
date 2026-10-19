import type { Plugin } from 'vite'

/** Extra origins the app may load images from, besides the image storage below. */
export const imageSources: readonly string[] = []

/**
 * The origin of the private R2 bucket (VITE_IMAGE_STORAGE_URL, e.g. https://<account>.eu.r2.cloudflarestorage.com).
 * Teachers upload to it with presigned PUT URLs (connect-src) and images are shown from presigned GET URLs (img-src).
 * Without it, uploads are blocked by the browser, which matches a backend without R2.
 */
export function imageStorageSources(imageStorageUrl: string | undefined): string[] {
  if (imageStorageUrl === undefined || imageStorageUrl === '') {
    return []
  }
  const url = new URL(imageStorageUrl)
  if (url.protocol !== 'https:') {
    throw new Error(`VITE_IMAGE_STORAGE_URL must be an https URL, got "${imageStorageUrl}"`)
  }
  return [url.origin]
}

/** The API origin plus the WebSocket origin SignalR connects to (https -> wss, http -> ws locally). */
export function apiConnectSources(apiUrl: string): string[] {
  const url = new URL(apiUrl)
  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    throw new Error(`VITE_API_URL must be an http(s) URL, got "${apiUrl}"`)
  }
  const socketProtocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
  return [url.origin, `${socketProtocol}//${url.host}`]
}

interface PolicyOptions {
  apiUrl: string
  imageSources?: readonly string[]
  imageStorageUrl?: string
}

/** Only our own files, no inline or external scripts, styles or fonts, and the page can't be framed. */
export function contentSecurityPolicy(options: PolicyOptions): string {
  const storage = imageStorageSources(options.imageStorageUrl)
  const directives: Record<string, readonly string[]> = {
    'default-src': ["'self'"],
    'script-src': ["'self'"],
    'style-src': ["'self'"],
    'font-src': ["'self'"],
    'img-src': ["'self'", ...(options.imageSources ?? imageSources), ...storage],
    'connect-src': ["'self'", ...apiConnectSources(options.apiUrl), ...storage],
    'object-src': ["'none'"],
    'base-uri': ["'self'"],
    'form-action': ["'self'"],
    'frame-ancestors': ["'none'"],
  }
  return Object.entries(directives)
    .map(([name, sources]) => `${name} ${sources.join(' ')}`)
    .join('; ')
}

/** Contents of the `_headers` file that Cloudflare Workers Static Assets applies to every response. */
export function headersFile(options: PolicyOptions): string {
  const headers: Record<string, string> = {
    'Content-Security-Policy': contentSecurityPolicy(options),
    'X-Content-Type-Options': 'nosniff',
    // Nothing needs the referrer, so don't leak paths (e.g. session URLs) to other sites
    'Referrer-Policy': 'no-referrer',
    // frame-ancestors for browsers without CSP level 2
    'X-Frame-Options': 'DENY',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
  }
  return [
    '# Generated at build time by security/securityHeaders.ts. Edit that file, not this one.',
    '/*',
    ...Object.entries(headers).map(([name, value]) => `  ${name}: ${value}`),
    '',
  ].join('\n')
}

/** Writes `_headers` into the build output. A static file can't do it: the URLs are only known at build time. */
export function securityHeaders(apiUrl: string, imageStorageUrl?: string): Plugin {
  return {
    name: 'eduscape-security-headers',
    apply: 'build',
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: '_headers', source: headersFile({ apiUrl, imageStorageUrl }) })
    },
  }
}
