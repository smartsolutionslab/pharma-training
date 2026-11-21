import { type App as VueApp, type Component, createApp, h } from 'vue';
import { withAuth } from './providers/auth';
import { withPinia } from './providers/pinia';
import { withQueryClient } from './providers/query';
import { withRouter } from './providers/router';
import App from './App.vue';
import './index.css';

/** Installs one library into the app and returns the root component, wrapped or as it came. */
type Provider = (app: VueApp, root: Component) => Component;

const providers: Provider[] = [
  // Outermost first: each one wraps all that follow it.
  withAuth,
  withPinia,
  withQueryClient,
  withRouter,
];

// The root is rendered at mount, once every provider has installed itself and wrapped it.
let root: Component = App;
const app = createApp({ render: () => h(root) });
root = providers.reduceRight<Component>((inner, provide) => provide(app, inner), App);

app.mount('#app');
