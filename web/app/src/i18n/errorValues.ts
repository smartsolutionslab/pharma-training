import { maximumLearners, maximumYearsAhead } from '@/modules/compliance/assignSchema';
import { chapterTitleMaxLength, maxDurationSeconds, maxTextLength } from '@/modules/editorial/chapterSchema';
import { courseTitleMaxLength } from '@/modules/editorial/courseTitle';
import { questionBounds } from '@/modules/editorial/questionSchema';
import { returnReasonMaxLength } from '@/modules/editorial/returnReasonSchema';
import { blueprintBounds } from '@/modules/editorial/testBlueprintSchema';

// The limits an error text names. Where a form holds a value to a bound of its own, the bound comes from that form's
// module (src/i18n depends on src/modules here, for the values alone); below, the limits the host holds a value to that
// the frontend has no bound of its own for yet, each as the host has it.

/** The longest pharmacy name (PharmacyName). */
export const pharmacyNameMaxLength = 120;
/** The longest reason for a change of an assignment. */
export const changeReasonMaxLength = 500;
/** How often a recurring assignment may recur, in months. */
export const recurrenceMonths = { min: 1, max: 60 } as const;
/** How many years a pharmacy may keep a certificate in its view (RetentionPeriod). */
export const retentionYears = { min: 2, max: 30 } as const;
/** The longest name of a module's generated sample. */
export const sampleNameMaxLength = 100;

const sampleName = { max: sampleNameMaxLength };

/**
 * The values an errors.<code> text fills in, by code: the limits it names, which the problem body never carries. Only
 * src/i18n/errorText.ts passes them; a caller's own values go to its fallback text.
 */
export const errorValues: Partial<Record<string, Readonly<Record<string, number>>>> = {
  'editorial.invalid-answer-options': { max: questionBounds.options.max },
  'editorial.invalid-answer-text': { max: questionBounds.option.max },
  'editorial.invalid-chapter-text': { max: maxTextLength },
  'editorial.invalid-chapter-title': { max: chapterTitleMaxLength },
  'editorial.invalid-course-title': { max: courseTitleMaxLength },
  'editorial.invalid-passing-score': blueprintBounds.passingScorePercent,
  'editorial.invalid-question-count': blueprintBounds.questions,
  'editorial.invalid-question-text': { max: questionBounds.text.max },
  // The course version page says this refusal with its own returnForm.reasonRefused, which names the longest reason.
  'editorial.invalid-return-reason': { max: returnReasonMaxLength },
  'editorial.invalid-validity': blueprintBounds.validityMonths,
  'editorial.invalid-video-duration': { hours: maxDurationSeconds / 3600 },
  'editorial.too-few-options': { min: questionBounds.options.min },
  'editorial.too-many-questions': { max: blueprintBounds.questions.max },
  'organisation.invalid-pharmacy-name': { max: pharmacyNameMaxLength },
  'compliance.due-date-too-far': { years: maximumYearsAhead },
  'compliance.invalid-change-reason': { max: changeReasonMaxLength },
  'compliance.invalid-recurrence': recurrenceMonths,
  'compliance.too-many-learners': { max: maximumLearners },
  'assessment.invalid-sample-name': sampleName,
  'catalog.invalid-sample-name': sampleName,
  'certification.invalid-retention-period': retentionYears,
  'certification.invalid-sample-name': sampleName,
  'compliance.invalid-sample-name': sampleName,
  'editorial.invalid-sample-name': sampleName,
  'learning.invalid-sample-name': sampleName,
  'organisation.invalid-sample-name': sampleName,
};
