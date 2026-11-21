import { computed, type ComputedRef, type MaybeRefOrGetter, toValue } from 'vue';
import { useAuth } from './useAuth';

/**
 * Whether the signed-in user holds the realm role, as the ID token says (`realm_access.roles`). Hiding an element is a
 * convenience, never protection: the backend policy decides.
 */
export function useHasRole(role: MaybeRefOrGetter<string>): ComputedRef<boolean> {
  const { user } = useAuth();
  return computed(() => {
    const realmAccess = user.value?.profile.realm_access;
    if (typeof realmAccess !== 'object' || realmAccess === null || !('roles' in realmAccess)) {
      return false;
    }

    const { roles } = realmAccess;
    return Array.isArray(roles) && roles.includes(toValue(role));
  });
}
