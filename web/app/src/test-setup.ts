import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/vue';
import { afterEach } from 'vitest';
import * as axeMatchers from 'vitest-axe/matchers';
import { expect } from 'vitest';

expect.extend(axeMatchers);

// jsdom measures nothing: the command palette scrolls the item it highlights into view.
Element.prototype.scrollIntoView ??= () => undefined;

// Vitest runs without globals, so Testing Library cannot register its own cleanup.
afterEach(cleanup);
