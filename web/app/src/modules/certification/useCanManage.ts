import { computed, type ComputedRef } from 'vue';
import { ApiError } from '@/api/baseApi';
import { useGetCertificationPermissionsManage } from '@/api/generated';

/** What the module's permission probe answered: 200 grants Manage, 403 refuses it, and anything else is a failure to show. */
export type ManagePermission = 'checking' | 'granted' | 'refused' | 'failed';

/** A 403 is the probe's answer to a caller without the permission; anything else that went wrong is a failure. */
function settled(error: unknown): ManagePermission {
  return error instanceof ApiError && error.status === 403 ? 'refused' : 'failed';
}

/** Whether the caller may manage certification: the module's permission probe answers 200 only to a holder of certification.manage. */
export function useCanManage(): ComputedRef<ManagePermission> {
  const probe = useGetCertificationPermissionsManage();
  return computed(() => {
    if (probe.isSuccess.value) {
      return 'granted';
    }
    return probe.error.value === null ? 'checking' : settled(probe.error.value);
  });
}
