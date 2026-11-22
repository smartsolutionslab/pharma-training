import { render, screen } from '@testing-library/vue';
import { expect, test } from 'vitest';
import { defineComponent, h } from 'vue';
import { setLanguage, t } from '@/i18n';
import { Breadcrumb, BreadcrumbEllipsis, BreadcrumbItem, BreadcrumbList } from './breadcrumb';
import { CommandDialog } from './command';
import { Dialog, DialogContent, DialogDescription, DialogScrollContent, DialogTitle } from './dialog';
import { Sheet, SheetContent, SheetDescription, SheetTitle } from './sheet';
import { SidebarProvider, SidebarRail, SidebarTrigger } from './sidebar';

// The words shadcn-vue's components bring of their own (a close button, the sidebar's toggle, the breadcrumb's
// landmark), in the language shown. The app shell's tests reach them as the shell uses them; these reach the rest.

const heading = (Title: typeof DialogTitle, Description: typeof DialogDescription) => [
  h(Title, null, () => 'A dialog'),
  h(Description, null, () => 'What it is for.'),
];

const OpenDialog = defineComponent({
  render: () => h(Dialog, { open: true }, () => h(DialogContent, null, () => heading(DialogTitle, DialogDescription))),
});
const OpenScrollDialog = defineComponent({
  render: () =>
    h(Dialog, { open: true }, () => h(DialogScrollContent, null, () => heading(DialogTitle, DialogDescription))),
});
const OpenSheet = defineComponent({
  render: () =>
    h(Sheet, { open: true }, () =>
      h(SheetContent, null, () => [
        h(SheetTitle, null, () => 'A sheet'),
        h(SheetDescription, null, () => 'What it is for.'),
      ]),
    ),
});
const OpenCommandDialog = defineComponent({ render: () => h(CommandDialog, { open: true }) });
const Crumbs = defineComponent({
  render: () =>
    h(Breadcrumb, null, () => h(BreadcrumbList, null, () => h(BreadcrumbItem, null, () => h(BreadcrumbEllipsis)))),
});
const SidebarControls = defineComponent({
  render: () => h(SidebarProvider, null, () => [h(SidebarTrigger), h(SidebarRail)]),
});

test.each([
  ['a dialog', OpenDialog],
  ['a dialog that scrolls', OpenScrollDialog],
  ['a sheet', OpenSheet],
])('the close button of %s is named in the language shown', async (_, Component) => {
  render(Component);

  expect(await screen.findByRole('button', { name: t('ui.close') })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Schließen' })).toBeInTheDocument();
});

test('a command dialog without a title of its own is named and described in the language shown', async () => {
  render(OpenCommandDialog);

  const dialog = await screen.findByRole('dialog', { name: t('ui.command.title') });
  expect(dialog).toHaveAccessibleDescription(t('ui.command.description'));
  expect(screen.getByRole('dialog', { name: 'Befehlspalette' })).toHaveAccessibleDescription(
    'Suchen Sie einen Befehl.',
  );
});

test("the breadcrumbs' landmark and their ellipsis are named in the language shown", () => {
  render(Crumbs);

  expect(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') })).toBeInTheDocument();
  expect(screen.getByRole('navigation', { name: 'Navigationspfad' })).toBeInTheDocument();
  expect(screen.getByText(t('ui.breadcrumb.more'))).toHaveClass('sr-only');
  expect(screen.getByText('Mehr')).toBeInTheDocument();
});

test("the sidebar's trigger and rail are named in the language shown, the rail's title too", () => {
  render(SidebarControls);

  const toggles = screen.getAllByRole('button', { name: t('ui.sidebar.toggle') });
  expect(toggles).toHaveLength(2);
  const rail = toggles.find((toggle) => toggle.dataset.sidebar === 'rail');
  expect(rail).toHaveAttribute('title', t('ui.sidebar.toggle'));
  expect(screen.getAllByRole('button', { name: 'Seitenleiste ein- oder ausblenden' })).toHaveLength(2);
});

test("in English the components' own words are English", async () => {
  setLanguage('en-GB', { remember: false });
  render(SidebarControls);
  render(Crumbs);

  expect(screen.getAllByRole('button', { name: 'Toggle sidebar' })).toHaveLength(2);
  expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toBeInTheDocument();

  // A modal dialog hides the rest of the page from screen readers: it opens last.
  render(OpenCommandDialog);
  expect(await screen.findByRole('dialog', { name: 'Command palette' })).toHaveAccessibleDescription(
    'Search for a command.',
  );
});
