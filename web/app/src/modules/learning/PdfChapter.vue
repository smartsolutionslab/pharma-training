<script setup lang="ts">
import type { PDFDocumentLoadingTask, PDFDocumentProxy, RenderTask, TextLayer } from 'pdfjs-dist/legacy/build/pdf.mjs';
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
// page's text laid over it (pdf.js's TextLayer: transparent, but read by screen readers and selectable), the page and
// pages in a polite live region, and buttons to turn them. The percent is the highest page drawn over the pages,
// rounded down, emitted as it rises once the page is drawn; the last page drawn is 100 (README: "letzte Seite
// angezeigt"). It opens at the highest page whose own percent is at most the stored one. A file that fails to open is
// opened once more with its signed URL read again, since the URL may have expired; Retry reads it again too.
const { chapterId, progressPercent } = defineProps<PdfChapterProps>();
const emit = defineEmits<{ progress: [percent: number] }>();
const { t, n } = useI18n();

const { url, failure, reload } = useChapterMedia(() => chapterId);
const canvas = useTemplateRef<HTMLCanvasElement>('canvas');
const textLayerElement = useTemplateRef<HTMLElement>('textLayer');

const pdf = shallowRef<PDFDocumentProxy>();
const pages = ref(0);
const page = ref(0);
const openFailed = ref(false);
/** The highest page shown so far; a page above it raises the percent. */
let highest = 0;
let loadingTask: PDFDocumentLoadingTask | undefined;
let renderTask: RenderTask | undefined;
let textLayer: TextLayer | undefined;
/** Whether the latest failure to open has read the URL again already; the next one is shown. */
let readAgain = false;
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
  textLayer?.cancel();
  textLayer = undefined;
  textLayerElement.value?.replaceChildren();
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
    readAgain = false;
    pdf.value = opened;
    pages.value = opened.numPages;
    page.value = startPage(opened.numPages);
  } catch {
    if (current !== opening) {
      return;
    }
    if (readAgain) {
      openFailed.value = true;
      return;
    }
    readAgain = true;
    await openAfresh();
  }
}

watch(url, open, { immediate: true });

/** Reads the signed URL again and opens the file with it; a URL that came back the same is opened again too. */
async function openAfresh() {
  const before = url.value;
  await reload();
  if (url.value === before) {
    await open(url.value);
  }
}

/**
 * Draws the page into the canvas, as wide as the canvas is shown and sharp at the screen's pixel ratio, counts it as
 * shown once drawn, and lays its text over it at the size it is shown.
 */
async function draw(opened: PDFDocumentProxy, shown: number) {
  renderTask?.cancel();
  renderTask = undefined;
  textLayer?.cancel();
  textLayer = undefined;
  textLayerElement.value?.replaceChildren();
  const [pdfJs, pdfPage] = await Promise.all([loadPdfJs(), opened.getPage(shown)]);
  const element = canvas.value;
  const isShown = () => pdf.value === opened && page.value === shown;
  if (element === null || !isShown()) {
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
    return;
  }
  if (!isShown()) {
    return;
  }
  if (shown > highest) {
    highest = shown;
    emit('progress', percentOf(shown, opened.numPages));
  }
  await layText(pdfJs, pdfPage, fit);
}

/** Lays the page's text over the canvas: pdf.js sizes it by --total-scale-factor, the page's scale on the screen. */
async function layText(
  pdfJs: Awaited<ReturnType<typeof loadPdfJs>>,
  pdfPage: Awaited<ReturnType<PDFDocumentProxy['getPage']>>,
  fit: number,
) {
  const container = textLayerElement.value;
  if (container === null) {
    return;
  }
  container.style.setProperty('--scale-factor', String(fit));
  container.style.setProperty('--total-scale-factor', String(fit));
  const layer = new pdfJs.TextLayer({
    textContentSource: await pdfPage.getTextContent(),
    container,
    viewport: pdfPage.getViewport({ scale: fit }),
  });
  if (textLayer !== undefined || renderTask === undefined) {
    // Another page was asked for meanwhile; its own drawing lays its text.
    return;
  }
  textLayer = layer;
  try {
    await layer.render();
  } catch {
    // A text layer cancelled by the next page turned; the page stays drawn either way.
  }
}

watch(
  [pdf, page],
  ([opened, shown]) => {
    if (opened === undefined || shown < 1) {
      return;
    }
    void draw(opened, shown);
  },
  { flush: 'post' },
);

function turnTo(shown: number) {
  page.value = Math.min(Math.max(shown, 1), pages.value);
}

/** Reads the signed URL again, whatever failed: the URL's request, or the file opened with it. */
function retry() {
  if (failure.value !== undefined) {
    void reload();
    return;
  }
  openFailed.value = false;
  readAgain = true;
  void openAfresh();
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
        <div class="relative">
          <canvas ref="canvas" class="block h-auto w-full" />
          <!-- pdf.js's text layer (its rules in src/index.css): the page's text, transparent over the canvas. -->
          <div ref="textLayer" class="textLayer" />
        </div>
      </div>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <Button variant="outline" :disabled="page <= 1" @click="turnTo(page - 1)">
          {{ t('learning.player.pdf.previous') }}
        </Button>
        <p v-if="pages > 0" aria-live="polite" class="text-body tabular-nums">
          {{ t('learning.player.pdf.page', { page: n(page), pages: n(pages) }) }}
        </p>
        <Button variant="outline" :disabled="page >= pages" @click="turnTo(page + 1)">
          {{ t('learning.player.pdf.next') }}
        </Button>
      </div>
    </section>
  </div>
</template>
