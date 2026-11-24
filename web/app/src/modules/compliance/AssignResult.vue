<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';
import type { ComplianceRefusedLearnerDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { codeMessage, messageText } from '@/i18n/errorText';

// The wizard's result: how many were assigned, and each person refused by name, in the UI's own words for the refusals
// it knows (the host's name a person by a raw id), the catalog's for another code (#147), the host's only for a code the
// catalog does not know, and "Nicht zugewiesen." when it has none.
defineProps<{ assigned: number; refused: ComplianceRefusedLearnerDto[]; nameOf: (personId: string) => string }>();
const emit = defineEmits<{ again: [] }>();

const { t } = useI18n();

/** A refused learner's reason by its code; a text already shown follows a switch of the language. */
const refusalText = (refusal: ComplianceRefusedLearnerDto) =>
  messageText(codeMessage(refusal.code, 'compliance.assign.result.refusedUnknown', { serverMessage: refusal.message }));
</script>

<template>
  <div class="flex flex-col gap-4">
    <p class="text-body tabular-nums">
      {{
        assigned > 0
          ? t('compliance.assign.result.assigned', { count: assigned }, assigned)
          : t('compliance.assign.result.noneAssigned')
      }}
    </p>
    <div v-if="refused.length > 0" class="flex flex-col gap-2">
      <h3 id="assign-refused" class="text-body font-medium">{{ t('compliance.assign.result.refused') }}</h3>
      <ul aria-labelledby="assign-refused" class="flex list-disc flex-col gap-1 pl-5 text-body">
        <li v-for="refusal in refused" :key="refusal.personId">
          <template v-if="refusal.code === 'compliance.assignment-already-active'">
            {{ t('compliance.assign.result.alreadyActive', { name: nameOf(refusal.personId) }) }}
          </template>
          <template v-else-if="refusal.code === 'compliance.learner-not-a-member'">
            {{ t('compliance.assign.result.notAMember', { name: nameOf(refusal.personId) }) }}
          </template>
          <template v-else>
            {{ t('compliance.assign.result.other', { name: nameOf(refusal.personId), message: refusalText(refusal) }) }}
          </template>
        </li>
      </ul>
    </div>
    <div class="flex flex-wrap gap-2">
      <Button type="button" @click="emit('again')">{{ t('compliance.assign.result.again') }}</Button>
      <Button as-child variant="outline">
        <RouterLink to="/">{{ t('compliance.assign.result.home') }}</RouterLink>
      </Button>
    </div>
  </div>
</template>
