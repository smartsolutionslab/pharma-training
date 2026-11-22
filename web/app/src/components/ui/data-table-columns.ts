import {
  columnVisibilityFeature,
  createColumnHelper,
  metaHelper,
  type RowData,
  tableFeatures,
} from '@tanstack/vue-table';
import { type MessageKey, t } from '@/i18n';

/** What a column carries for the table: a column headed from the catalogs keeps its key for the Columns control. */
export interface ColumnMeta {
  labelKey?: MessageKey;
}

/** Column visibility, for the table's Columns control, and the columns' meta (ColumnMeta); register sorting, filtering or paging here when a screen needs them. */
export const features = tableFeatures({ columnVisibilityFeature, columnMeta: metaHelper<ColumnMeta>() });

export type Features = typeof features;

/** The column helper for rows of TRow: `const notes = columnsOf<Note>()`, then `notes.columns([notes.accessor('text', ...)])`. */
export const columnsOf = <TRow extends RowData>() => createColumnHelper<Features, TRow>();

/**
 * A column's header from the catalogs, in the language shown when it renders: `notes.accessor('text', translatedHeader('notes.text'))`.
 * The table's Columns control names the column by the same key.
 */
export const translatedHeader = (key: MessageKey) => ({ header: () => t(key), meta: { labelKey: key } });
