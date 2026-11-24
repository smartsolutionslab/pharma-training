<script setup lang="ts">
import { useForm } from 'vee-validate';
import { computed, ref, useId, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { z } from 'zod';
import {
  type ChangeOrganisationMembershipRequest,
  OrganisationJobFunction,
  OrganisationMembershipRole,
  type PharmacyMemberDto,
} from '@/api/generated';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { toTypedSchema } from '@/forms/toTypedSchema';
import type { MessageKey } from '@/i18n';
import type { ErrorMessage } from '@/i18n/errorText';
import { jobFunctions, membershipRoles } from './inviteSchema';
import MembershipSelect from './MembershipSelect.vue';
import { teamFailureText } from './teamErrors';

interface ChangeMembershipDialogProps {
  open: boolean;
  /** The member whose job function and role change; the last one asked about while the dialog closes. */
  member?: PharmacyMemberDto;
  /** The current pharmacy's name, which the description names. */
  pharmacy: string;
  /** Sends the change; resolves with the failure the dialog shows, or nothing once it was made. */
  change: (request: ChangeOrganisationMembershipRequest) => Promise<ErrorMessage | undefined>;
}

// Changes a membership's job function and role, preset from its row; both are sent as they stand. A refusal (the last
// manager made an employee, say) keeps the dialog open with the choice and says why above its buttons.
const { open, member, pharmacy, change } = defineProps<ChangeMembershipDialogProps>();
const emit = defineEmits<{ cancel: []; closed: [event: Event] }>();
const { t } = useI18n();
const id = useId();

const changeSchema = z.object({
  jobFunction: z.enum(OrganisationJobFunction, {
    error: 'organisation.team.invite.jobFunctionRequired' satisfies MessageKey,
  }),
  role: z.enum(OrganisationMembershipRole, { error: 'organisation.team.invite.roleRequired' satisfies MessageKey }),
});

const presetOf = (shown: PharmacyMemberDto | undefined) => ({
  jobFunction: shown?.jobFunction ?? '',
  role: shown?.role ?? '',
});

const { defineField, errors, handleSubmit, isSubmitting, resetForm } = useForm({
  validationSchema: toTypedSchema(changeSchema),
  initialValues: presetOf(member),
});
const [jobFunction, jobFunctionAttributes] = defineField('jobFunction');
const [role, roleAttributes] = defineField('role');

/** Why the last change was refused, until the form is sent again or opened anew. */
const failure = ref<ErrorMessage>();

watch(
  () => open,
  (shown) => {
    if (shown) {
      resetForm({ values: presetOf(member) });
      failure.value = undefined;
    }
  },
);

const jobFunctionOptions = computed(() =>
  jobFunctions.map((value) => ({ value, label: t(`glossary.jobFunction.${value}`) })),
);
const roleOptions = computed(() =>
  membershipRoles.map((value) => ({ value, label: t(`glossary.membershipRole.${value}`) })),
);

const sendOnce = handleSubmit((values) => {
  failure.value = undefined;
  return change(values);
});

// A submit while one is on its way is ignored, or a double click would send the change twice. A refusal shows once
// the submit has settled, with the button enabled again.
async function submit(event: Event) {
  if (isSubmitting.value) {
    event.preventDefault();
    return;
  }
  failure.value = await sendOnce(event);
}
</script>

<template>
  <Dialog :open="open" @update:open="(shown) => !shown && !isSubmitting && emit('cancel')">
    <DialogContent :close-disabled="isSubmitting" @close-auto-focus="(event: Event) => emit('closed', event)">
      <DialogHeader>
        <DialogTitle>{{ t('organisation.team.change.title') }}</DialogTitle>
        <DialogDescription>
          {{ t('organisation.team.change.description', { name: member?.name ?? '', pharmacy }) }}
        </DialogDescription>
      </DialogHeader>
      <form novalidate class="flex flex-col gap-4" @submit="submit">
        <MembershipSelect
          :id="`${id}-job-function`"
          v-model="jobFunction"
          v-bind="jobFunctionAttributes"
          :label="t('organisation.team.invite.jobFunction')"
          :options="jobFunctionOptions"
          :error="errors.jobFunction"
        />
        <MembershipSelect
          :id="`${id}-role`"
          v-model="role"
          v-bind="roleAttributes"
          :label="t('organisation.team.invite.role')"
          :options="roleOptions"
          :error="errors.role"
        />
        <p v-if="failure !== undefined" role="alert" class="text-body text-destructive">
          {{ teamFailureText(failure) }}
        </p>
        <DialogFooter>
          <DialogClose as-child>
            <Button type="button" variant="outline" :disabled="isSubmitting">{{
              t('organisation.team.cancel')
            }}</Button>
          </DialogClose>
          <Button type="submit" :disabled="isSubmitting">{{ t('organisation.team.change.submit') }}</Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
