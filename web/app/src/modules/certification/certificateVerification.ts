import { z } from 'zod';
import { CertificateValidity } from '@/api/generated';
import { isCalendarDate } from '@/lib/germanDay';

const calendarDay = z.string().refine(isCalendarDate);

/**
 * The host's answer to a verification (CertificateVerificationDto), as the page may show it: a validity it knows, days
 * of the calendar (yyyy-mm-dd) and the holder's name and course title as text. The answer is untrusted input: anything
 * else in it is dropped, and an answer that does not match is not shown.
 */
export const certificateVerificationSchema = z.object({
  holderName: z.string(),
  courseTitle: z.string(),
  issuedOn: calendarDay,
  validUntil: calendarDay,
  validity: z.enum(CertificateValidity),
});

export type CertificateVerification = z.infer<typeof certificateVerificationSchema>;
