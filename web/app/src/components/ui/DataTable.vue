<script setup lang="ts" generic="TRow extends RowData">
import { type ColumnDef, FlexRender, type RowData, useTable } from '@tanstack/vue-table';
import { computed, ref, toRef, useId, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type { MessageKey } from '@/i18n';
import { cn } from '@/lib/utils';
import { Button } from './button';
import { Skeleton } from './skeleton';
import { features, type Features } from './data-table-columns';

/**
 * A table over TanStack Table; keep columns stable (module scope). Its header stays in view while the rows scroll; above
 * them, the reader picks the density and the columns shown. While the query is pending (`loading`), the table is busy
 * and shows skeleton rows in its own shape instead of data; with no rows it shows its `empty` slot, such as an
 * `<EmptyState>`. The `actions` slot fills a row's last cell, shown while the row is hovered or has focus; given the
 * page its rows are of (`paging`, a `PagedResult` as the host answers it), it says below them which rows it shows of
 * how many ("Einträge 1–25 von 312") and, for a list longer than a page, pages through it: Previous and Next ask the
 * list's owner for that page with `update:page`, since the table does not page itself. Its texts come from the catalogs (`dataTable.*`), in the language shown.
 * `rowId` names each row by its record's stable id (#477, #479). It is required: a list read again while shown can put
 * a record before the others, and rows told apart by their place would then hand a row's cells, controls and actions
 * to the record now in its place, so a click acts on another record. A read-only table passes its records' id too.
 */
const props = defineProps<{
  columns: ColumnDef<Features, TRow, unknown>[];
  data: TRow[];
  loading?: boolean;
  rowId: (row: TRow) => string;
  paging?: { page: number | string; pageSize: number | string; totalCount: number | string };
}>();

const emit = defineEmits<{ 'update:page': [page: number] }>();

const slots = defineSlots<{ empty?: () => unknown; actions?: (props: { row: TRow }) => unknown }>();

type Density = 'comfortable' | 'compact';

const densities: { value: Density; labelKey: MessageKey }[] = [
  { value: 'comfortable', labelKey: 'dataTable.comfortable' },
  { value: 'compact', labelKey: 'dataTable.compact' },
];
const cellPadding: Record<Density, string> = { comfortable: 'px-2 py-2', compact: 'px-2 py-1' };
const skeletonRows = ['first', 'second', 'third'];

const { t, n } = useI18n();
const table = useTable({
  features,
  columns: props.columns,
  data: toRef(() => props.data),
  getRowId: props.rowId,
});
const density = ref<Density>('comfortable');
const choosing = ref(false);
const columnsButton = useTemplateRef<InstanceType<typeof Button>>('columnsButton');
const id = useId();
const rows = computed(() => table.getRowModel().rows);
const allColumns = computed(() => table.getAllLeafColumns());
const visibleColumns = computed(() => table.getVisibleLeafColumns());
const span = computed(() => visibleColumns.value.length + (slots.actions ? 1 : 0));
const cell = computed(() => cn('border-b', cellPadding[density.value]));

/** "Einträge 26–50 von 312", or nothing while loading, without a page or for a page without rows. */
const summary = computed(() => {
  const { paging, loading } = props;
  if (loading || paging === undefined || rows.value.length === 0) {
    return undefined;
  }

  const first = (Number(paging.page) - 1) * Number(paging.pageSize) + 1;
  return t('dataTable.showing', {
    first: n(first, 'integer'),
    last: n(first + rows.value.length - 1, 'integer'),
    total: n(Number(paging.totalCount), 'integer'),
  });
});

/** The page shown and how many there are, for a list longer than a page; nothing for a list on one page. */
const pager = computed(() => {
  const { paging } = props;
  if (paging === undefined) {
    return undefined;
  }

  const page = Number(paging.page);
  const pages = Math.ceil(Number(paging.totalCount) / Number(paging.pageSize));
  return pages > 1 ? { page, pages } : undefined;
});

/** A column's name in the Columns control: its header from the catalogs, its header text, or else its id. */
function labelOf(column: (typeof allColumns.value)[number]): string {
  const { header, meta } = column.columnDef;
  if (meta?.labelKey !== undefined) {
    return t(meta.labelKey);
  }

  return typeof header === 'string' ? header : column.id;
}

function closeOnEscape(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    choosing.value = false;
    (columnsButton.value?.$el as HTMLElement | undefined)?.focus();
  }
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex flex-wrap items-center justify-end gap-2">
      <div role="group" :aria-label="t('dataTable.density')" class="flex gap-1">
        <Button
          v-for="option in densities"
          :key="option.value"
          type="button"
          :variant="density === option.value ? 'secondary' : 'ghost'"
          size="sm"
          :aria-pressed="density === option.value"
          @click="density = option.value"
        >
          {{ t(option.labelKey) }}
        </Button>
      </div>
      <div class="relative">
        <Button
          ref="columnsButton"
          type="button"
          variant="outline"
          size="sm"
          :aria-expanded="choosing"
          :aria-controls="`${id}-columns`"
          @click="choosing = !choosing"
        >
          {{ t('dataTable.columns') }}
        </Button>
        <fieldset
          :id="`${id}-columns`"
          :hidden="!choosing"
          class="bg-popover text-popover-foreground absolute right-0 z-20 mt-1 flex min-w-40 flex-col gap-1 rounded-md border p-2 shadow-elevation-2"
          @keydown="closeOnEscape"
        >
          <legend class="sr-only">{{ t('dataTable.visibleColumns') }}</legend>
          <label v-for="column in allColumns" :key="column.id" class="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              :checked="column.getIsVisible()"
              :disabled="column.getIsVisible() && visibleColumns.length === 1"
              @change="column.toggleVisibility()"
            />
            {{ labelOf(column) }}
          </label>
        </fieldset>
      </div>
    </div>
    <table
      class="w-full text-sm"
      :data-density="density"
      :aria-busy="loading || undefined"
      :aria-describedby="summary === undefined ? undefined : `${id}-showing`"
    >
      <caption v-if="loading" class="sr-only">
        {{
          t('dataTable.loading')
        }}
      </caption>
      <thead>
        <tr v-for="group in table.getHeaderGroups()" :key="group.id">
          <th
            v-for="header in group.headers"
            :key="header.id"
            :class="cn('bg-background sticky top-0 z-10 text-left font-medium', cell)"
          >
            <FlexRender v-if="!header.isPlaceholder" :header="header" />
          </th>
          <th v-if="$slots.actions" :class="cn('bg-background sticky top-0 z-10', cell)">
            <span class="sr-only">{{ t('dataTable.actions') }}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        <template v-if="loading">
          <tr v-for="row in skeletonRows" :key="row" aria-hidden="true">
            <td v-for="column in visibleColumns" :key="column.id" :class="cell">
              <Skeleton class="h-4 w-full" />
            </td>
            <td v-if="$slots.actions" :class="cell" />
          </tr>
        </template>
        <template v-else>
          <tr v-for="row in rows" :key="row.id" class="group hover:bg-muted/50">
            <td v-for="rowCell in row.getVisibleCells()" :key="rowCell.id" :class="cell">
              <FlexRender :cell="rowCell" />
            </td>
            <td v-if="$slots.actions" :class="cell">
              <div
                class="flex justify-end gap-1 opacity-0 motion-safe:transition-opacity group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100"
              >
                <slot name="actions" :row="row.original" />
              </div>
            </td>
          </tr>
          <tr v-if="rows.length === 0 && $slots.empty">
            <td :colspan="span" class="p-2">
              <slot name="empty" />
            </td>
          </tr>
        </template>
      </tbody>
    </table>
    <p v-if="summary !== undefined" :id="`${id}-showing`" class="text-muted-foreground text-caption">
      {{ summary }}
    </p>
    <nav v-if="pager !== undefined" :aria-label="t('dataTable.pagination')" class="flex justify-end gap-2">
      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="pager.page <= 1"
        @click="emit('update:page', pager.page - 1)"
      >
        {{ t('dataTable.previous') }}
      </Button>
      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="pager.page >= pager.pages"
        @click="emit('update:page', pager.page + 1)"
      >
        {{ t('dataTable.next') }}
      </Button>
    </nav>
  </div>
</template>
