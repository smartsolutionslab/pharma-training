import type { RouteParams } from 'vue-router';
import { z } from 'zod';

/** A code the host could have issued (VerificationCode): 24 to 64 characters of the Base64Url alphabet. */
export const verificationCodeSchema = z.string().regex(/^[A-Za-z0-9_-]{24,64}$/);

/**
 * The verification code a certificate's QR link carries, /verify/:code, as the page may send it to the host; none for
 * a malformed code, which is never sent: it spends none of the limit every verifier shares (ADR 0026).
 */
export function parseVerificationCode(params: RouteParams): string | undefined {
  const parsed = verificationCodeSchema.safeParse(params.code);
  return parsed.success ? parsed.data : undefined;
}
