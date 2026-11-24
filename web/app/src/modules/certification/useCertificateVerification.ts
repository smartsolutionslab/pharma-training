import { computed, type MaybeRefOrGetter, shallowRef, toValue, watch } from 'vue';
import { useGetCertificationVerifyCode } from '@/api/generated';
import { today } from '@/lib/germanDay';
import { type CertificateVerification, certificateVerificationSchema } from './certificateVerification';
import { type VerificationOutcome, verificationLoadFailed, verificationOutcome } from './verificationErrors';

/** What the verification page shows once an attempt has settled: the certificate, or why it is not shown. */
export type VerificationShown = { kind: 'verified'; certificate: CertificateVerification } | VerificationOutcome;

/** The German day of an instant the query cache recorded (milliseconds since the epoch). */
const answeredOn = (instant: number) => today(() => new Date(instant));

/**
 * The host's verification of a code (#54): none for a malformed code, which is never sent. Every request spends the
 * limit every verifier shares (ADR 0026): nothing is asked again by itself (no retry, no refetch on focus or on
 * reconnect, nor when the page is opened again after a refusal), and retry asks once, never while a request is on its way.
 * The one exception: whether a certificate is valid changes with the German day, so a verification answered on an
 * earlier day is asked again, once, when the window regains focus.
 *
 * What it shows is the outcome of the last attempt that settled, kept while the next one is on its way (TanStack clears
 * a query's error while it refetches one without data), with that attempt's number, so that the same refusal again is
 * shown anew. An answer that does not match the contract reads as a failure.
 */
export function useCertificateVerification(code: MaybeRefOrGetter<string | undefined>) {
  const { data, error, isFetching, refetch } = useGetCertificationVerifyCode(() => toValue(code) ?? '', {
    query: {
      // A refusal stays until Retry (refetch, which runs on a disabled query too): TanStack counts a query without data
      // as stale whatever its staleTime, and would otherwise ask again whenever an observer's options are set anew.
      enabled: (query) => toValue(code) !== undefined && query.state.status !== 'error',
      retry: false,
      retryOnMount: false,
      refetchOnWindowFocus: (query) =>
        query.state.status === 'success' && answeredOn(query.state.dataUpdatedAt) !== today(() => new Date())
          ? 'always'
          : false,
      refetchOnReconnect: false,
      staleTime: Infinity,
    },
  });

  // A malformed code is never sent: whatever the query says, nothing is on its way.
  const busy = computed(() => toValue(code) !== undefined && isFetching.value);
  const shown = shallowRef<VerificationShown>();
  const attempt = shallowRef(0);

  function settled(): VerificationShown | undefined {
    if (toValue(code) === undefined) {
      return { kind: 'notVerifiable' };
    }
    if (error.value !== null) {
      return verificationOutcome(error.value);
    }
    if (data.value === undefined) {
      return undefined;
    }
    const parsed = certificateVerificationSchema.safeParse(data.value);
    return parsed.success ? { kind: 'verified', certificate: parsed.data } : verificationLoadFailed;
  }

  watch(
    [() => toValue(code), busy, data, error],
    () => {
      if (busy.value) {
        return;
      }
      const outcome = settled();
      if (outcome !== undefined) {
        shown.value = outcome;
        attempt.value += 1;
      }
    },
    { immediate: true },
  );

  async function retry() {
    if (busy.value) {
      return;
    }
    await refetch();
  }

  return { shown, attempt, busy, retry };
}
