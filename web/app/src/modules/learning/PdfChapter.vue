<script setup lang="ts">
import type { PDFDocumentLoadingTask, PDFDocumentProxy, RenderTask } from 'pdfjs-dist';
import { onBeforeUnmount, ref, shallowRef, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { Button } from '@/components/ui/button';
import { documentParameters, loadPdfJs } from './pdfJs';
import { useChapterMedia } from './useChapterMedia';

interface PdfChapterProps {
  chapterId: string;
  /** The chapter's title, for the document's region. */
  title: string;
  /** The server's percent: the page the chapter opens at. */
  progressPercent: number;
  /** Whether the session has the chapter done: the hint to page to the end goes. */
  completed: boolean;
}

// A PDF chapter (#44, Q1): the file the signed URL names, rendered one page at a time by pdf.js into a canvas, with the
// page and pages and buttons to turn them. The percent is the highest page shown over the pages, rounded down, emitted
// as it rises; the last page shown is 100 (README: "letzte Seite angezeigt"). It opens at the highest page whose own
// percent is at most the stored one.
const { chapterId, progressPercent } = defineProps<PdfChapterProps>();
const emit = defineEmits<{ progress: [percent: number] }>();
const { t, n } = useI18n();

const { url, failure, reload } = useChapterMedia(() => chapterId, { renew: false });
const canvas = useTemplateRef<HTMLCanvasElement>('canvas');

const pdf = shallowRef<PDFDocumentProxy>();
const pages = ref(0);
const page = ref(0);
const openFailed = ref(false);
/** The highest page shown so far; a page above it raises the percent. */
let highest = 0;
let loadingTask: PDFDocumentLoadingTask | undefined;
let renderTask: RenderTask | undefined;
/** Bumped by every opening, so only the latest one shows its document. */
let opening = 0;

const percentOf = (shown: number, count: number) => (shown >= count ? 100 : Math.floor((shown / count) * 100));

/** The highest page whose own percent is at most the stored one, else the first. */
function startPage(count: number) {
  let start = 1;
  for (let candidate = 2; candidate <= count; candidate += 1) {
    if (percentOf(candidate, count) <= progressPercent) {
      start = candidate;
    }
  }
  return start;
}

function close() {
  renderTask?.cancel();
  renderTask = undefined;
  void loadingTask?.destroy();
  loadingTask = undefined;
  pdf.value = undefined;
  pages.value = 0;
  page.value = 0;
  highest = 0;
}

async function open(source: string | undefined) {
  opening += 1;
  const current = opening;
  close();
  openFailed.value = false;
  if (source === undefined) {
    return;
  }
  try {
    const pdfJs = await loadPdfJs();
    if (current !== opening) {
      return;
    }
    const task = pdfJs.getDocument(documentParameters(source));
    loadingTask = task;
    const opened = await task.promise;
    if (current !== opening) {
      return;
    }
    pdf.value = opened;
    pages.value = opened.numPages;
    page.value = startPage(opened.numPages);
  } catch {
    if (current === opening) {
      openFailed.value = true;
    }
  }
}

watch(url, open, { immediate: true });

/** Draws the page into the canvas, as wide as the canvas is shown and sharp at the screen's pixel ratio. */
async function draw(opened: PDFDocumentProxy, shown: number) {
  renderTask?.cancel();
  renderTask = undefined;
  const pdfPage = await opened.getPage(shown);
  const element = canvas.value;
  if (element === null || pdf.value !== opened || page.value !== shown) {
    return;
  }
  const natural = pdfPage.getViewport({ scale: 1 });
  const shownWidth = element.parentElement?.clientWidth ?? 0;
  const fit = shownWidth > 0 ? shownWidth / natural.width : 1;
  const viewport = pdfPage.getViewport({ scale: fit * Math.max(1, window.devicePixelRatio) });
  element.width = Math.floor(viewport.width);
  element.height = Math.floor(viewport.height);
  const task = pdfPage.render({ canvas: element, viewport });
  renderTask = task;
  try {
    await task.promise;
  } catch {
    // A render cancelled by the next page turned is no failure; any other is.
    if (renderTask === task) {
      openFailed.value = true;
    }
  }
}

watch(
  [pdf, page],
  ([opened, shown]) => {
    if (opened === undefined || shown < 1) {
      return;
    }
    if (shown > highest) {
      highest = shown;
      emit('progress', percentOf(shown, opened.numPages));
    }
    void draw(opened, shown);
  },
  { flush: 'post' },
);

function turnTo(shown: number) {
  page.value = Math.min(Math.max(shown, 1), pages.value);
}

function retry() {
  if (failure.value !== undefined) {
    void reload();
  } else {
    void open(url.value);
  }
}

onBeforeUnmount(() => {
  opening += 1;
  close();
});
</script>

<template>
  <div class="flex flex-col gap-3">
    <p v-if="!completed" class="text-caption text-muted-foreground">{{ t('learning.player.pdf.hint') }}</p>
    <div v-if="failure !== undefined || openFailed" role="alert" class="flex flex-col items-start gap-3">
      <p class="text-body">
        {{ failure === 'notReady' ? t('learning.player.media.notReady') : t('learning.player.pdf.failed') }}
      </p>
      <Button variant="outline" @click="retry">{{ t('shell.retry') }}</Button>
    </div>
    <section :aria-label="t('learning.player.pdf.label', { title })" class="flex flex-col gap-3">
      <div class="bg-muted overflow-hidden rounded-md border">
        <canvas ref="canvas" class="block h-auto w-full" />
      </div>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <Button variant="outline" :disabled="page <= 1" @click="turnTo(page - 1)">
          {{ t('learning.player.pdf.previous') }}
        </Button>
        <p v-if="pages > 0" class="text-body tabular-nums">
          {{ t('learning.player.pdf.page', { page: n(page), pages: n(pages) }) }}
        </p>
        <Button variant="outline" :disabled="page >= pages" @click="turnTo(page + 1)">
          {{ t('learning.player.pdf.next') }}
        </Button>
      </div>
    </section>
  </div>
</template>
