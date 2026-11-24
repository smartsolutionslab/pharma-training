<script setup lang="ts">
import { useForm } from 'vee-validate';
import { computed, ref, useId, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { InviteOrganisationMemberRequest } from '@/api/generated';
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
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import { inviteInitialValues, inviteSchema, jobFunctions, membershipRoles } from './inviteSchema';
import MembershipSelect from './MembershipSelect.vue';
import { type TeamFailure, teamFailureText } from './teamErrors';

interface InviteMemberDialogProps {
  open: boolean;
  /** Sends the invitation; resolves with the failure the dialog shows, or nothing once it was sent. */
  invite: (request: InviteOrganisationMemberRequest) => Promise<TeamFailure | undefined>;
}

// Asks for the address, the job function and the role of a new member, checked as the host checks them. A refusal
// keeps the dialog open with what was entered and says why above its buttons; an address the host refuses is also
// the address field's message. Each time it opens, the form is empty again.
const { open, invite } = defineProps<InviteMemberDialogProps>();
const emit = defineEmits<{ cancel: []; closed: [event: Event] }>();
const { t } = useI18n();
const id = useId();
const emailField = `${id}-email`;

const { defineField, errors, handleSubmit, resetForm, setFieldError } = useForm({
  validationSchema: toTypedSchema(inviteSchema),
  initialValues: inviteInitialValues,
});
const [email, emailAttributes] = defineField('email');
const [jobFunction, jobFunctionAttributes] = defineField('jobFunction');
const [role, roleAttributes] = defineField('role');

/** Counts the dialog's openings: an answer to what an earlier opening sent shows nothing in this one. */
const opening = ref(0);
/** The opening whose submit is on its way: only that opening is busy, so a dialog opened again can send. */
const sendingIn = ref<number>();
const sending = computed(() => sendingIn.value === opening.value);

/** Why the last invitation was refused, until the form is sent again or opened anew. */
const failure = ref<TeamFailure>();

watch(
  () => open,
  (shown) => {
    if (shown) {
      opening.value += 1;
      resetForm({ values: inviteInitialValues });
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
  return invite(values);
});

// A submit while one of this opening is on its way is ignored, or a double click would send two invitations. A
// refusal shows once the submit has settled, with the button enabled again, unless the dialog was opened anew since.
async function submit(event: Event) {
  const asked = opening.value;
  if (sendingIn.value === asked) {
    event.preventDefault();
    return;
  }
  sendingIn.value = asked;
  try {
    const refused = await sendOnce(event);
    if (asked !== opening.value) {
      return;
    }
    failure.value = refused;
    if (refused !== undefined && 'key' in refused && refused.key === 'organisation.team.errors.invalidEmail') {
      setFieldError('email', refused.key);
    }
  } finally {
    if (sendingIn.value === asked) {
      sendingIn.value = undefined;
    }
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="(shown) => !shown && !sending && emit('cancel')">
    <DialogContent :close-disabled="sending" @close-auto-focus="(event: Event) => emit('closed', event)">
      <DialogHeader>
        <DialogTitle>{{ t('organisation.team.invite.title') }}</DialogTitle>
        <DialogDescription>{{ t('organisation.team.invite.description') }}</DialogDescription>
      </DialogHeader>
      <form novalidate class="flex flex-col gap-4" @submit="submit">
        <div class="flex flex-col gap-2">
          <label :for="emailField" class="text-body font-medium">{{ t('organisation.team.invite.email') }}</label>
          <Input
            :id="emailField"
            v-model="email"
            v-bind="emailAttributes"
            type="email"
            autocomplete="off"
            aria-required="true"
            :aria-invalid="errors.email ? true : undefined"
            :aria-describedby="errors.email ? `${emailField}-error` : undefined"
          />
          <p v-if="errors.email" :id="`${emailField}-error`" role="alert" class="text-caption text-destructive">
            {{ t(errors.email) }}
          </p>
        </div>
        <MembershipSelect
          :id="`${id}-job-function`"
          v-model="jobFunction"
          v-bind="jobFunctionAttributes"
          :label="t('organisation.team.invite.jobFunction')"
          :placeholder="t('organisation.team.invite.choose')"
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
            <Button type="button" variant="outline" :disabled="sending">{{ t('organisation.team.cancel') }}</Button>
          </DialogClose>
          <Button type="submit" :disabled="sending">{{ t('organisation.team.invite.submit') }}</Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
