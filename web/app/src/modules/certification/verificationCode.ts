import type { RouteParams } from 'vue-router';

/**
 * The verification code a certificate's QR link carries, /verify/:code, as the page may send it to the host; none for
 * a malformed code, which is never sent. Stub for #54 (T3, red first).
 */
export function parseVerificationCode(params: RouteParams): string | undefined {
  void params;
  return '';
}
