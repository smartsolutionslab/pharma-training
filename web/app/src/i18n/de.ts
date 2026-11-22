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
  theme: {
    label: 'Farbschema',
    light: 'Hell',
    dark: 'Dunkel',
    system: 'System',
  },
  home: {
    choosePharmacy: 'Wählen Sie Ihre Apotheke',
    noPharmacy: {
      title: 'Noch keine Apotheke',
      text: 'Sie sind noch in keiner Apotheke Mitglied. Registrieren Sie Ihre Apotheke, oder bitten Sie deren Apothekenleiter um eine Einladung.',
    },
  },
  modules: {
    learning: { title: 'Meine Schulungen' },
    assessment: { title: 'Tests' },
    certification: { title: 'Teilnahmebestätigungen' },
    compliance: { title: 'Zuweisungen' },
    catalog: { title: 'Schulungskatalog' },
    organisation: {
      title: 'Mitglieder',
      pages: {
        register: { title: 'Apotheke registrieren' },
      },
    },
    editorial: { title: 'Redaktion' },
  },
  samples: {
    form: {
      name: 'Name',
    },
  },
  shell: {
    home: 'Startseite',
    navigation: {
      label: 'Bereiche',
    },
    palette: {
      open: 'Seite suchen…',
      title: 'Seite öffnen',
      description: 'Wählen Sie die Seite, die Sie öffnen möchten.',
      pages: 'Seiten',
      empty: 'Keine Seite gefunden.',
      shortcut: 'Strg K',
    },
    gate: {
      loading: 'Wird geladen…',
      notAvailable: 'Nicht verfügbar',
      notForYou: 'Diese Seite steht Ihnen nicht zur Verfügung.',
      notInPharmacy: 'Diese Seite steht Ihnen in {name} nicht zur Verfügung.',
      pharmaciesFailed: 'Ihre Apotheken konnten nicht geladen werden.',
      noPharmacy: 'Sie sind noch in keiner Apotheke Mitglied.',
      choosePharmacy: 'Wählen Sie eine Apotheke, um diese Seite zu sehen.',
      goHome: 'Zur Startseite',
    },
    retry: 'Erneut versuchen',
    pharmacy: {
      region: 'Apotheke',
      list: 'Apotheken',
      choose: 'Apotheke wählen',
      current: 'Apotheke: {name}',
      failed: 'Die Apotheken konnten nicht geladen werden.',
      none: 'Sie sind noch in keiner Apotheke Mitglied.',
      register: 'Apotheke registrieren',
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
