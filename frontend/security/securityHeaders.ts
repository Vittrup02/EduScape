import type { Plugin } from 'vite'

/**
 * Extra origins the app may load images from. The one place to change img-src, e.g. add the public
 * R2 bucket URL once image uploads exist. Uploads via presigned URLs also need the R2 S3 endpoint
 * in connect-src.
 */
export const imageSources: readonly string[] = []

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
}

/** Only our own files, no inline or external scripts, styles or fonts, and the page can't be framed. */
export function contentSecurityPolicy(options: PolicyOptions): string {
  const directives: Record<string, readonly string[]> = {
    'default-src': ["'self'"],
    'script-src': ["'self'"],
    'style-src': ["'self'"],
    'font-src': ["'self'"],
    'img-src': ["'self'", ...(options.imageSources ?? imageSources)],
    'connect-src': ["'self'", ...apiConnectSources(options.apiUrl)],
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

/** Writes `_headers` into the build output. A static file can't do it: the API URL is only known at build time. */
export function securityHeaders(apiUrl: string): Plugin {
  return {
    name: 'eduscape-security-headers',
    apply: 'build',
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: '_headers', source: headersFile({ apiUrl }) })
    },
  }
}
