import { CalendarX, Circle, CircleAlert, CircleCheck, CircleDot, Lock } from '@lucide/vue';
import type { Component } from 'vue';
import type { CellStatus } from './cellStatus';

/** How a status looks in the team matrix: an icon and the colour of its word, which only repeat what the word says. */
export interface StatusLook {
  icon: Component;
  /** The word's and the icon's classes, theme tokens only. */
  tone: string;
  /** The date a cell shows under the word: when it is due, or until when its completion holds. */
  line: 'due' | 'validUntil';
  /** A status that needs action: its cell is highlighted by a border, not only by colour. */
  needsAction: boolean;
}

export const statusLooks: Record<CellStatus, StatusLook> = {
  Open: { icon: Circle, tone: 'text-muted-foreground', line: 'due', needsAction: false },
  InProgress: { icon: CircleDot, tone: 'text-foreground', line: 'due', needsAction: false },
  Locked: { icon: Lock, tone: 'text-destructive', line: 'due', needsAction: false },
  Overdue: { icon: CircleAlert, tone: 'text-destructive font-medium', line: 'due', needsAction: true },
  Completed: { icon: CircleCheck, tone: 'text-primary', line: 'validUntil', needsAction: false },
  CompletedLate: { icon: CircleCheck, tone: 'text-primary', line: 'validUntil', needsAction: false },
  Expired: { icon: CalendarX, tone: 'text-destructive font-medium', line: 'validUntil', needsAction: true },
};

/** The statuses in the legend's order: pending, needing action, done, run out. */
export const statusOrder: CellStatus[] = [
  'Open',
  'InProgress',
  'Locked',
  'Overdue',
  'Completed',
  'CompletedLate',
  'Expired',
];
