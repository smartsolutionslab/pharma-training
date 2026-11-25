import { z } from 'zod';

/** The longest reason the host takes, in characters: Unicode code points, an emoji one (ADR 0013). */
export const returnReasonMaxLength = 1000;

/**
 * What a reason may not hold: a control character but tab, line feed and carriage return, a format, separator,
 * private-use or unassigned character, or a lone surrogate.
 */
const notPlainText = /[\p{Cs}\p{Cf}\p{Zl}\p{Zp}\p{Co}\p{Cn}]|[^\P{Cc}\t\n\r]/u;

/** The return dialog's form (#26): the reason, trimmed, 1 to 1000 characters of plain text. Messages are keys. */
export const returnReasonSchema = z.object({
  reason: z
    .string()
    .trim()
    .min(1, 'editorial.workflow.returnForm.reasonRequired')
    // Counted in code points, as the host counts: an emoji outside the BMP is one, not two UTF-16 code units.
    .refine(
      (reason) => Array.from(reason).length <= returnReasonMaxLength,
      'editorial.workflow.returnForm.reasonTooLong',
    )
    .refine((reason) => !notPlainText.test(reason), 'editorial.workflow.returnForm.reasonInvalid'),
});

export type ReturnReason = z.infer<typeof returnReasonSchema>;
