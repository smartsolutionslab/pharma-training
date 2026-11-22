<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { Button } from '@/components/ui/button';

interface UploadProgressProps {
  /** The file's name, which names the progress bar. */
  name: string;
  loaded: number;
  total: number;
}

// How much of a file went out to storage, and the way to stop it.
const { name, loaded, total } = defineProps<UploadProgressProps>();
const emit = defineEmits<{ cancel: [] }>();
const { t, n } = useI18n();

const share = computed(() => (total > 0 ? Math.min(1, loaded / total) : 0));
const percent = computed(() => Math.round(share.value * 100));
</script>

<template>
  <div class="flex flex-col gap-2">
    <progress
      class="bg-muted [&::-moz-progress-bar]:bg-primary [&::-webkit-progress-bar]:bg-muted [&::-webkit-progress-value]:bg-primary h-2 w-full overflow-hidden rounded-full"
      max="100"
      :value="percent"
      aria-valuemin="0"
      aria-valuemax="100"
      :aria-valuenow="percent"
      :aria-label="t('editorial.upload.progress', { name })"
    />
    <div class="flex items-center justify-between gap-2">
      <p class="text-caption text-muted-foreground tabular-nums">
        {{ t('editorial.upload.progressValue', { percent: n(share, 'percent') }) }}
      </p>
      <Button type="button" variant="outline" size="sm" @click="emit('cancel')">
        {{ t('editorial.upload.cancel') }}
      </Button>
    </div>
  </div>
</template>
