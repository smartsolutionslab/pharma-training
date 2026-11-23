<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialTestBlueprintDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { t as translate } from '@/i18n';
import { notify } from '@/lib/notify';
import TestBlueprintForm from './TestBlueprintForm.vue';
import type { TestBlueprint } from './testBlueprintSchema';
import { useTestBlueprint } from './useTestBlueprint';

interface TestBlueprintSectionProps {
  versionId: string;
  /** The blueprint as the host sends it, its numbers typed number | string. */
  blueprint: EditorialTestBlueprintDto;
  /** Whether the blueprint may be changed: in a draft only. */
  editable: boolean;
  /** Whether nothing may change for now, such as while the AI drafts the test. */
  locked?: boolean;
}

const { versionId, blueprint, editable, locked = false } = defineProps<TestBlueprintSectionProps>();
// What else belongs to the blueprint, such as the AI draft, shown while the blueprint is not being changed.
defineSlots<{ default?: () => unknown }>();
const { save, isSaving } = useTestBlueprint(() => versionId);
const { t, n } = useI18n();

const current = computed((): TestBlueprint => ({
  questions: Number(blueprint.questions),
  passingScorePercent: Number(blueprint.passingScorePercent),
  validityMonths: Number(blueprint.validityMonths),
}));
const editing = ref(false);

/** Saves the form's blueprint; a refusal is reported, and the form stays open with what was entered. */
async function saveBlueprint(values: TestBlueprint) {
  const result = await save(values);
  if (!result.ok) {
    notify.failure(result.serverMessage ?? translate('editorial.test.blueprint.saveFailed'));
    return false;
  }
  notify.success(translate('editorial.test.blueprint.saved'));
  editing.value = false;
  return true;
}
</script>

<template>
  <section aria-labelledby="course-test-blueprint" class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 id="course-test-blueprint">{{ t('editorial.test.blueprint.title') }}</h2>
      <Button v-if="editable && !editing" type="button" variant="outline" :disabled="locked" @click="editing = true">
        {{ t('editorial.test.blueprint.edit') }}
      </Button>
    </div>
    <TestBlueprintForm
      v-if="editable && editing"
      :blueprint="current"
      :on-save="saveBlueprint"
      :saving="isSaving"
      @cancel="editing = false"
    />
    <dl v-else class="text-body grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 tabular-nums">
      <dt class="text-muted-foreground">{{ t('editorial.test.blueprint.questions') }}</dt>
      <dd>{{ n(current.questions, 'integer') }}</dd>
      <dt class="text-muted-foreground">{{ t('editorial.test.blueprint.passingScore') }}</dt>
      <dd>{{ n(current.passingScorePercent / 100, 'percent') }}</dd>
      <dt class="text-muted-foreground">{{ t('editorial.test.blueprint.validity') }}</dt>
      <dd>
        {{
          t('editorial.test.blueprint.months', { count: n(current.validityMonths, 'integer') }, current.validityMonths)
        }}
      </dd>
    </dl>
    <slot v-if="!editing" />
  </section>
</template>
