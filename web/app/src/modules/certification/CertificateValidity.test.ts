import { render, screen } from '@testing-library/vue';
import { expect, test } from 'vitest';
import { setLanguage } from '@/i18n';
import CertificateValidity from './CertificateValidity.vue';

// Whether a certificate is valid or expired (#54), as the verification page shows it first and the list of one's
// certificates (#296) in its Status column: a word, which is text, never colour alone, and an icon beside it that is
// only decoration, in the theme's primary colour for valid and its destructive colour for expired.

test.each([
  ['Valid', 'Gültig', 'text-primary'],
  ['Expired', 'Abgelaufen', 'text-destructive'],
] as const)('%s reads %s, beside an icon screen readers do not hear', (validity, word, colour) => {
  const { container } = render(CertificateValidity, { props: { validity } });

  const text = screen.getByText(word);
  expect(text.closest('[aria-hidden="true"]')).toBeNull();
  const icons = container.querySelectorAll('svg');
  expect(icons).toHaveLength(1);
  expect(icons[0]).toHaveAttribute('aria-hidden', 'true');
  expect(icons[0]).toHaveClass(colour);
});

test.each([
  ['Valid', 'Valid'],
  ['Expired', 'Expired'],
] as const)('in English, %s reads %s', (validity, word) => {
  setLanguage('en-GB', { remember: false });
  render(CertificateValidity, { props: { validity } });

  expect(screen.getByText(word)).toBeInTheDocument();
});
