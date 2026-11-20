import { render } from '@testing-library/vue';
import { expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import App from './App.vue';
import { createAppRouter } from './router';

// The user menu has its own test; here it stands in, so this test checks the start page.
vi.mock('@/auth/UserMenu.vue', async () => {
  const { h } = await import('vue');
  return { default: { render: () => h('p', 'The user menu') } };
});

test('the start page has no accessibility violations axe can find', async () => {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/');

  const { container } = render(App, { global: { plugins: [router] } });

  expect(await axe(container)).toHaveNoViolations();
});
