<script setup lang="ts">
import { nextTick, ref, useId, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { Button } from '@/components/ui/button';
import { formatDuration } from '@/lib/formatDuration';
import { useChapterMedia } from './useChapterMedia';
import { useSeekLimit } from './useSeekLimit';

interface VideoChapterProps {
  chapterId: string;
  /** The chapter's title, for the video's name. */
  title: string;
  /** The editor's duration in seconds: the percent's denominator, whatever the file says (Q3). */
  durationSeconds: number;
  /** The server's percent: where the video resumes, and the least the learner may skip ahead to. */
  progressPercent: number;
  /** Whether the session has the chapter done: seeking is free. */
  completed: boolean;
}

// A video chapter (#44): the signed URL in a native <video> with its own controls, without a speed control (Q2: the
// rate stays 1) and without auto-play. Skipping ahead stops at what was watched (useSeekLimit). It emits the percent
// watched at once at a pause and at the end (`progress`), on every timeupdate while playing (`tracking`, which the
// reporter sends at most every 30 s), and what the learner should hear in the page's polite status region (`announce`).
// The signed URL is read again when the media fails (an expired URL fails once the browser asks storage for more), and
// the new one picks up where the old one was.
const { chapterId, durationSeconds, progressPercent, completed } = defineProps<VideoChapterProps>();
const emit = defineEmits<{ progress: [percent: number]; tracking: [percent: number]; announce: [text: string] }>();
const { t } = useI18n();

/** A resume this close to the end starts from the beginning. */
const nearTheEnd = 1;
/** The end reached by playing this close to the editor's duration is the whole video. */
const endTolerance = 2;

const { url, failure, reload } = useChapterMedia(() => chapterId);
const video = useTemplateRef<HTMLVideoElement>('video');
const limit = useSeekLimit(() => completed);
const hintId = useId();

/** The server's progress, from this device or another, raises the maximum. */
const serverPosition = () => (progressPercent / 100) * durationSeconds;
watch(
  () => progressPercent,
  () => limit.raise(serverPosition()),
  { immediate: true },
);

const percentWatched = () =>
  durationSeconds > 0 ? Math.min(100, Math.floor((limit.maximum() / durationSeconds) * 100)) : 0;

/** Where a new source (the URL read again after an error, or Retry) picks up, and whether it plays on. */
let pickUp: { at: number; playing: boolean } | undefined;
let resumed = false;
/** Whether a media error has read the URL once more already; the next one is shown. */
let readAgainAfterError = false;
const playFailed = ref(false);

function keepPosition() {
  const element = video.value;
  if (element !== null && pickUp === undefined) {
    pickUp = { at: element.currentTime, playing: !element.paused };
  }
}

// A new URL starts the element from 0, paused: it picks up where the old one was.
watch(url, (next, previous) => {
  if (previous !== undefined && next !== previous) {
    keepPosition();
  }
});

function onLoadedMetadata() {
  const element = video.value;
  if (element === null) {
    return;
  }
  readAgainAfterError = false;
  if (pickUp !== undefined) {
    const { at, playing } = pickUp;
    pickUp = undefined;
    limit.placeAt(at);
    element.currentTime = at;
    if (playing) {
      void element.play();
    }
    return;
  }
  if (resumed) {
    return;
  }
  resumed = true;
  const resume = serverPosition();
  if (resume <= 0 || resume >= durationSeconds - nearTheEnd) {
    return;
  }
  limit.raise(resume);
  limit.placeAt(resume);
  element.currentTime = resume;
  emit('announce', t('learning.player.video.resumed', { position: formatDuration(resume) }));
}

function onTimeUpdate() {
  const element = video.value;
  if (element === null || element.seeking) {
    return;
  }
  limit.played(element.currentTime);
  if (!element.paused) {
    emit('tracking', percentWatched());
  }
}

function onSeeking() {
  const element = video.value;
  if (element === null) {
    return;
  }
  const back = limit.seek(element.currentTime);
  if (back !== undefined) {
    element.currentTime = back;
    emit('announce', t('learning.player.video.seekRefused', { position: formatDuration(back) }));
  }
}

function onPause() {
  emit('progress', percentWatched());
}

function onEnded() {
  emit('progress', limit.maximum() >= durationSeconds - endTolerance ? 100 : percentWatched());
}

function onRateChange() {
  const element = video.value;
  if (element !== null && element.playbackRate !== 1) {
    element.playbackRate = 1;
  }
}

/** Reads the URL again and plays it from where it was; the element reloads also when the URL is the same. */
async function reloadSource() {
  const before = url.value;
  keepPosition();
  await reload();
  await nextTick();
  if (url.value === before) {
    video.value?.load();
  }
}

function onError() {
  if (readAgainAfterError) {
    playFailed.value = true;
    return;
  }
  readAgainAfterError = true;
  void reloadSource();
}

function retry() {
  playFailed.value = false;
  readAgainAfterError = false;
  if (url.value === undefined) {
    void reload();
  } else {
    void reloadSource();
  }
}
</script>

<template>
  <div class="flex flex-col gap-3">
    <div v-if="playFailed || failure !== undefined" role="alert" class="flex flex-col items-start gap-3">
      <p class="text-body">
        {{ failure === 'notReady' ? t('learning.player.media.notReady') : t('learning.player.video.failed') }}
      </p>
      <Button variant="outline" @click="retry">{{ t('shell.retry') }}</Button>
    </div>
    <video
      v-if="url !== undefined"
      ref="video"
      :src="url"
      controls
      controlslist="nodownload noplaybackrate"
      preload="metadata"
      playsinline
      class="bg-muted aspect-video w-full rounded-md"
      :aria-label="t('learning.player.video.label', { title })"
      :aria-describedby="hintId"
      @loadedmetadata="onLoadedMetadata"
      @timeupdate="onTimeUpdate"
      @seeking="onSeeking"
      @pause="onPause"
      @ended="onEnded"
      @ratechange="onRateChange"
      @error="onError"
    />
    <p :id="hintId" class="text-caption text-muted-foreground">{{ t('learning.player.video.seekHint') }}</p>
  </div>
</template>
