import './lib/zodWithoutEval';
import { type App as VueApp, type Component, createApp, h } from 'vue';
import { bootstrap } from './bootstrap';
import { withAuth } from './providers/auth';
import { withI18n } from './providers/i18n';
import { withPinia } from './providers/pinia';
import { withQueryClient } from './providers/query';
import { withRouter } from './providers/router';
import App from './App.vue';
import './index.css';

/** Installs one library into the app and returns the root component, wrapped or as it came. */
type Provider = (app: VueApp, root: Component) => Component;

const providers: Provider[] = [
  // Outermost first: each one wraps all that follow it.
  withI18n,
  withAuth,
  withPinia,
  withQueryClient,
  withRouter,
];

const target = document.querySelector<HTMLElement>('#app');
if (target === null) {
  throw new Error('index.html has no #app to mount the app into.');
}

// The providers install themselves once the runtime configuration is loaded: signing in needs the identity provider it names.
void bootstrap(target, (element) => {
  // The root is rendered at mount, once every provider has installed itself and wrapped it.
  let root: Component = App;
  const app = createApp({ render: () => h(root) });
  root = providers.reduceRight<Component>((inner, provide) => provide(app, inner), App);
  app.mount(element);
});
