<script setup lang="ts">
import { useForm } from 'vee-validate';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialChapterDto, EditorialChapterRequest, EditorialQuestionDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import type { MessageKey } from '@/i18n';
import { type FileKind, fileLimits, fileProblemKey, isFileProblem, maxMegabytes } from './chapterFile';
import { chapterSchema, maxTextLength } from './chapterSchema';
import { formatDuration } from './formatDuration';
import MarkdownEditor from './MarkdownEditor.vue';
import { readVideoDuration } from './readVideoDuration';
import UploadProgress from './UploadProgress.vue';
import { useChapterUpload } from './useChapterUpload';

interface ChapterFormProps {
  /** The version the chapter belongs to, which a new file is uploaded for. */
  versionId: string;
  /** The chapter to replace; none to add one. */
  chapter?: EditorialChapterDto;
  /** The version's questions: replacing a chapter makes its approved ones need reviewing again. */
  questions: EditorialQuestionDto[];
  /** Saves the chapter; resolves to whether it was saved. */
  onSave: (request: EditorialChapterRequest) => Promise<boolean>;
  saving: boolean;
}

// Adds a chapter or replaces one: its title, its kind and, for a text chapter, its Markdown, for a PDF or video chapter
// its file (and a video's duration), checked here as the host checks them (chapterSchema). A new file goes straight to
// storage first (useChapterUpload), with progress and a cancel; the chapter is handed on only once it is stored. What
// saving answers, and what happens then, are the page's.
const { versionId, chapter, questions, onSave, saving } = defineProps<ChapterFormProps>();
// Further actions beside the submit button, such as the dialog's Cancel; busy while a file goes to storage, when only
// its cancel works.
defineSlots<{ actions?: (props: { busy: boolean }) => unknown }>();
const emit = defineEmits<{
  /** Whether a file is on its way to storage, during which nothing around the form should work either. */
  busy: [busy: boolean];
}>();

const kinds = ['Text', 'Pdf', 'Video'] as const;
const fields = {
  title: 'chapter-title',
  kind: 'chapter-kind',
  text: 'chapter-text',
  file: 'chapter-file',
  duration: 'chapter-duration',
};

const {
  defineField,
  errors: checked,
  handleSubmit,
  setFieldValue,
  values,
} = useForm({
  validationSchema: toTypedSchema(chapterSchema(chapter)),
  initialValues: {
    title: chapter?.title ?? '',
    kind: chapter?.kind ?? 'Text',
    text: chapter?.text ?? '',
    file: undefined,
    duration:
      chapter?.durationSeconds === null || chapter?.durationSeconds === undefined
        ? ''
        : formatDuration(chapter.durationSeconds),
  },
});
const [title, titleAttributes] = defineField('title');
const [kind, kindAttributes] = defineField('kind');
const [text] = defineField('text');
const [duration, durationAttributes] = defineField('duration');
const { t, n } = useI18n();
const upload = useChapterUpload();

// The refusals show once a submit was refused, all of them at once, and follow the fields from then on: nothing is
// refused while the editor is still writing. A chosen file is checked at once, before anything is sent.
const refused = ref(false);
const fileChosen = ref(false);
const errors = computed(() => {
  if (refused.value) {
    return checked.value;
  }
  return fileChosen.value && checked.value.file !== undefined ? { file: checked.value.file } : {};
});

/** A file chapter's kind, while the form is on one. */
const fileKind = computed((): FileKind | undefined => (kind.value === 'Text' ? undefined : kind.value));

/** The file refused by the limits the host issued with the upload URL, until another file or kind is chosen. */
const issuedRefusal = ref<{ key: MessageKey; max: number }>();
/** The refusal shown at the file input: the form's own check first, else the issued limits'. */
const fileError = computed(() => {
  const own = errors.value.file;
  if (own !== undefined && fileKind.value !== undefined) {
    return { key: own as MessageKey, max: maxMegabytes(fileKind.value) };
  }
  return issuedRefusal.value;
});

/** The file already stored for a chapter not saved yet, and its blob's name: saving again reuses it. */
let stored: { file: File; blobName: string } | undefined;

/** Whether a video's duration came from the file or could not; nothing until a video is chosen. */
const durationRead = ref<'detected' | 'unknown'>();
let reading = 0;

function choose(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  fileChosen.value = file !== undefined;
  issuedRefusal.value = undefined;
  void setFieldValue('file', file);
  durationRead.value = undefined;
  const read = ++reading;
  if (file === undefined || kind.value !== 'Video' || file.type !== fileLimits.Video.contentType || file.size === 0) {
    return;
  }
  void readVideoDuration(file).then((seconds) => {
    if (read !== reading) {
      return;
    }
    if (seconds === undefined) {
      durationRead.value = 'unknown';
    } else {
      duration.value = formatDuration(seconds);
      durationRead.value = 'detected';
    }
  });
}

// A file chosen for one kind is none of another's.
watch(kind, () => {
  reading++;
  fileChosen.value = false;
  issuedRefusal.value = undefined;
  durationRead.value = undefined;
  void setFieldValue('file', undefined, false);
});

/** The approved questions of the chapter, which need reviewing again once it is replaced. */
const approved = computed(
  () => questions.filter((each) => each.chapterId === chapter?.id && each.review === 'Approved').length,
);
const keepsFile = computed(() => chapter !== undefined && values.kind !== 'Text' && chapter.kind === values.kind);

const uploading = computed(() => {
  const state = upload.state.value;
  return state.status === 'requesting' || state.status === 'uploading' ? state : undefined;
});
const transferring = computed(() => uploading.value !== undefined);
watch(transferring, (busy) => emit('busy', busy));
/** Whether the last upload failed on its way to storage (not for the file itself), as the form says until a retry. */
const uploadFailed = computed(() => {
  const state = upload.state.value;
  return state.status === 'failed' && !isFileProblem(state.reason);
});
/** The file going to storage, which names the progress bar. */
const sending = ref<File>();

/** A file the issued limits refuse is refused at the file input, naming the issued limit, and takes the focus. */
function refuseIssued(kindOfFile: FileKind) {
  const state = upload.state.value;
  if (state.status !== 'failed' || !isFileProblem(state.reason)) {
    return;
  }
  const maxBytes = upload.issuedLimits.value?.maxBytes ?? fileLimits[kindOfFile].maxBytes;
  issuedRefusal.value = {
    key: fileProblemKey(kindOfFile, state.reason),
    max: maxBytes / 1_048_576,
  };
  document.getElementById(fields.file)?.focus();
}

// A refused field takes the focus, the first one, so the editor corrects it where the refusal is read.
const submit = handleSubmit(
  async ({ request, upload: file }) => {
    if (file === undefined || request.kind === 'Text') {
      await onSave(request);
      return;
    }
    let blobName = stored?.file === file ? stored.blobName : undefined;
    if (blobName === undefined) {
      stored = undefined;
      sending.value = file;
      blobName = await upload.start(versionId, request.kind, file);
      if (blobName === undefined) {
        refuseIssued(request.kind);
        return;
      }
      stored = { file, blobName };
    }
    if (await onSave({ ...request, file: blobName })) {
      stored = undefined;
      upload.attached();
    }
  },
  ({ errors: found }) => {
    refused.value = true;
    const first = (['title', 'text', 'file', 'duration'] as const).find((field) => found[field] !== undefined);
    if (first !== undefined) {
      document.getElementById(fields[first])?.focus();
    }
  },
);
</script>

<template>
  <form novalidate class="flex flex-col gap-4" @submit="submit">
    <div class="flex flex-col gap-2">
      <label :for="fields.title" class="text-body font-medium">{{ t('editorial.chapterForm.title') }}</label>
      <Input
        :id="fields.title"
        v-model="title"
        v-bind="titleAttributes"
        autocomplete="off"
        aria-required="true"
        :disabled="transferring"
        :aria-invalid="errors.title ? true : undefined"
        :aria-describedby="errors.title ? `${fields.title}-error` : undefined"
      />
      <p v-if="errors.title" :id="`${fields.title}-error`" role="alert" class="text-caption text-destructive">
        {{ t(errors.title) }}
      </p>
    </div>
    <div class="flex flex-col gap-2">
      <span :id="fields.kind" class="text-body font-medium">{{ t('editorial.chapterForm.kind') }}</span>
      <div role="radiogroup" :aria-labelledby="fields.kind" class="flex flex-wrap gap-4">
        <label v-for="each in kinds" :key="each" class="text-body flex items-center gap-2">
          <input
            v-model="kind"
            v-bind="kindAttributes"
            type="radio"
            name="kind"
            :value="each"
            :disabled="transferring"
            class="accent-primary"
          />
          {{ t(`glossary.chapterKind.${each}`) }}
        </label>
      </div>
    </div>
    <template v-if="fileKind === undefined">
      <MarkdownEditor
        :id="fields.text"
        v-model="text"
        :invalid="errors.text !== undefined"
        :error-id="`${fields.text}-error`"
      />
      <p v-if="errors.text" :id="`${fields.text}-error`" role="alert" class="text-caption text-destructive">
        {{ t(errors.text, { max: n(maxTextLength, 'integer') }) }}
      </p>
    </template>
    <template v-else>
      <div class="flex flex-col gap-2">
        <label :for="fields.file" class="text-body font-medium">{{
          t(`editorial.chapterForm.file.${fileKind}`)
        }}</label>
        <input
          :id="fields.file"
          :key="fileKind"
          type="file"
          :accept="fileLimits[fileKind].contentType"
          :disabled="transferring"
          :aria-required="keepsFile ? undefined : true"
          :aria-invalid="fileError ? true : undefined"
          :aria-describedby="fileError ? `${fields.file}-error` : undefined"
          class="text-body file:bg-secondary file:text-secondary-foreground file:mr-3 file:rounded-md file:border-0 file:px-3 file:py-1"
          @change="choose"
        />
        <p v-if="keepsFile" class="text-caption text-muted-foreground">{{ t('editorial.chapterForm.fileKept') }}</p>
        <p v-if="fileError" :id="`${fields.file}-error`" role="alert" class="text-caption text-destructive">
          {{ t(fileError.key, { max: n(fileError.max, 'megabytes') }) }}
        </p>
      </div>
      <div v-if="fileKind === 'Video'" class="flex flex-col gap-2">
        <label :for="fields.duration" class="text-body font-medium">{{ t('editorial.chapterForm.duration') }}</label>
        <Input
          :id="fields.duration"
          v-model="duration"
          v-bind="durationAttributes"
          autocomplete="off"
          inputmode="numeric"
          aria-required="true"
          :disabled="transferring"
          :aria-invalid="errors.duration ? true : undefined"
          :aria-describedby="
            [durationRead ? `${fields.duration}-read` : '', errors.duration ? `${fields.duration}-error` : '']
              .join(' ')
              .trim() || undefined
          "
        />
        <p
          v-if="durationRead === 'detected'"
          :id="`${fields.duration}-read`"
          class="text-caption text-muted-foreground"
        >
          {{ t('editorial.chapterForm.durationDetected') }}
        </p>
        <p v-else-if="durationRead === 'unknown'" :id="`${fields.duration}-read`" class="text-caption">
          {{ t('editorial.chapterForm.durationUnknown') }}
        </p>
        <p v-if="errors.duration" :id="`${fields.duration}-error`" role="alert" class="text-caption text-destructive">
          {{ t(errors.duration) }}
        </p>
      </div>
    </template>
    <p v-if="approved > 0" class="text-caption bg-muted rounded-md p-3">
      {{ t('editorial.chapterForm.requalify', { count: approved }, approved) }}
    </p>
    <UploadProgress
      v-if="uploading !== undefined && sending !== undefined"
      :name="sending.name"
      :loaded="uploading.status === 'uploading' ? uploading.loaded : 0"
      :total="uploading.status === 'uploading' ? uploading.total : sending.size"
      @cancel="upload.cancel()"
    />
    <p v-if="upload.state.value.status === 'cancelled'" class="text-caption">{{ t('editorial.upload.cancelled') }}</p>
    <p v-if="uploadFailed" role="alert" class="text-caption text-destructive">{{ t('editorial.upload.failed') }}</p>
    <div class="flex justify-end gap-2">
      <slot name="actions" :busy="transferring" />
      <Button type="submit" :disabled="saving || transferring">{{ t('editorial.chapterForm.submit') }}</Button>
    </div>
  </form>
</template>
