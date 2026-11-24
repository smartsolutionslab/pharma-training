import { loadRuntimeConfig } from '@/auth/runtimeConfig';
import { i18n, t } from '@/i18n';
import { applyLanguage } from '@/i18n/language';

/**
 * Loads the runtime configuration (the edge's /config.json), then mounts the app into the target. Without it the app
 * cannot sign anyone in, so a failed or invalid answer renders an alert in the user's language instead.
 */
export async function bootstrap(target: HTMLElement, mount: (element: HTMLElement) => void): Promise<void> {
  try {
    await loadRuntimeConfig();
  } catch {
    renderUnavailable(target);
    return;
  }
  mount(target);
}

function renderUnavailable(target: HTMLElement): void {
  applyLanguage(i18n.global.locale.value);
  const alert = document.createElement('p');
  alert.setAttribute('role', 'alert');
  alert.className = 'p-4 text-body text-destructive';
  alert.textContent = t('auth.configurationUnavailable');
  target.replaceChildren(alert);
}
