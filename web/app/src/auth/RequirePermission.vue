<script setup lang="ts">
import { useHasRole } from './useHasRole';

interface RequirePermissionProps {
  /** The realm role the user must hold. */
  role: string;
}

/**
 * Renders its default slot only for a user whose ID token meets the requirement, else its fallback slot (nothing by
 * default). Hiding an element is a convenience, never protection: the backend policy decides.
 */
const { role } = defineProps<RequirePermissionProps>();
const allowed = useHasRole(() => role);
</script>

<template>
  <slot v-if="allowed" />
  <slot v-else name="fallback" />
</template>
