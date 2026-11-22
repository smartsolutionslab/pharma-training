import type { OrganisationJobFunction, OrganisationMembershipRole } from '@/api/generated';

// The German catalog, which defines the keys (MessageSchema). German follows docs/domain/README.md's glossary and
// addresses the user as "Sie". vue-i18n reads @ { } | $ as syntax: write a literal one as {'@'}.
export const de = {
  app: {
    name: 'pharma-training',
  },
  language: {
    label: 'Sprache',
    de: 'Deutsch',
    en: 'English',
  },
  glossary: {
    membershipRole: {
      Manager: 'Apothekenleiter',
      Employee: 'Mitarbeiter',
    } satisfies Record<OrganisationMembershipRole, string>,
    jobFunction: {
      Pharmacist: 'Apotheker',
      Pta: 'PTA (pharmazeutisch-technischer Assistent)',
      Pka: 'PKA (pharmazeutisch-kaufmännischer Angestellter)',
      Trainee: 'Azubi',
      Other: 'Andere',
    } satisfies Record<NonNullable<OrganisationJobFunction>, string>,
  },
  auth: {
    signedIn: 'Angemeldet',
    signedInAs: 'Angemeldet als {name}',
    signOut: 'Abmelden',
    signInFailed: 'Anmeldung fehlgeschlagen: {message}',
    signingIn: 'Anmeldung läuft…',
    signingOut: 'Abmeldung läuft…',
  },
  samples: {
    form: {
      name: 'Name',
    },
  },
  shell: {
    pharmacy: {
      region: 'Apotheke',
    },
  },
  toasts: {
    region: 'Benachrichtigungen',
    close: 'Benachrichtigung schließen',
  },
  dataTable: {
    density: 'Zeilenabstand',
    comfortable: 'Normal',
    compact: 'Kompakt',
    columns: 'Spalten',
    visibleColumns: 'Sichtbare Spalten',
    loading: 'Wird geladen…',
    actions: 'Aktionen',
    showing: 'Einträge {first}–{last} von {total}',
  },
  ui: {
    close: 'Schließen',
    sidebar: {
      toggle: 'Seitenleiste ein- oder ausblenden',
      title: 'Seitenleiste',
      description: 'Zeigt die Seitenleiste auf kleinen Bildschirmen.',
    },
    command: {
      title: 'Befehlspalette',
      description: 'Suchen Sie einen Befehl.',
    },
    breadcrumb: {
      label: 'Navigationspfad',
      more: 'Mehr',
    },
  },
};

/** The catalogs' shape: every key with its message. en.ts is typed by it, so a missing or extra key fails vue-tsc. */
export type MessageSchema = typeof de;

/** A key path into the catalogs (auth.signOut, glossary.jobFunction.Pta, ...); an unknown one fails vue-tsc. */
export type MessageKey = KeyPaths<MessageSchema>;

type KeyPaths<T> = {
  [K in keyof T & string]: T[K] extends string ? K : `${K}.${KeyPaths<T[K]>}`;
}[keyof T & string];
