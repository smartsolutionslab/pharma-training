import type {
  EditorialChapterKind,
  EditorialCourseVersionStatus,
  OrganisationJobFunction,
  OrganisationMembershipRole,
} from '@/api/generated';

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
      Other: 'Sonstige',
    } satisfies Record<NonNullable<OrganisationJobFunction>, string>,
    courseVersionStatus: {
      Draft: 'Entwurf',
      InReview: 'In Prüfung',
      Published: 'Veröffentlicht',
      Superseded: 'Abgelöst',
      Retired: 'Zurückgezogen',
    } satisfies Record<EditorialCourseVersionStatus, string>,
    chapterKind: {
      Text: 'Text',
      Pdf: 'PDF',
      Video: 'Video',
    } satisfies Record<EditorialChapterKind, string>,
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
      entries: {
        acceptInvitation: { title: 'Einladung annehmen' },
      },
    },
    editorial: {
      title: 'Redaktion',
      details: {
        version: { title: 'Schulungsversion' },
      },
    },
  },
  organisation: {
    register: {
      title: 'Apotheke registrieren',
      intro: 'Sie verwalten die Apotheke: Sie laden Ihr Team ein und weisen ihm Schulungen zu.',
      name: 'Name der Apotheke',
      jobFunction: 'Ihre Tätigkeit',
      choose: 'Bitte wählen…',
      submit: 'Registrieren',
      nameRequired: 'Geben Sie den Namen der Apotheke an.',
      nameTooLong: 'Der Name einer Apotheke hat höchstens 120 Zeichen.',
      nameHasNul: 'Der Name einer Apotheke darf kein Nullzeichen enthalten.',
      jobFunctionRequired: 'Wählen Sie Ihre Tätigkeit in der Apotheke.',
      registered: 'Die Apotheke {name} wurde registriert.',
      registeredNotOpened:
        'Die Apotheke {name} wurde registriert, konnte aber nicht geöffnet werden. Wählen Sie sie unter „Apotheke“ in der Seitenleiste.',
      failed: 'Die Apotheke konnte nicht registriert werden. Versuchen Sie es erneut.',
    },
    accept: {
      title: 'Einladung annehmen',
      accepting: 'Die Einladung wird angenommen …',
      accepted: 'Sie sind jetzt Mitglied der Apotheke {name}.',
      acceptedUnnamed: 'Sie haben die Einladung angenommen.',
      acceptedNotOpened:
        'Sie haben die Einladung angenommen, die Apotheke konnte aber nicht geöffnet werden. Wählen Sie sie unter „Apotheke“ in der Seitenleiste.',
      missing: {
        title: 'Kein gültiger Einladungslink',
        text: 'Öffnen Sie den Link aus Ihrer Einladungs-E-Mail noch einmal, vollständig und in diesem Browser. Da Sie jetzt angemeldet sind, wird die Einladung dann sofort angenommen.',
      },
      notFound: {
        title: 'Einladung nicht gefunden',
        text: 'Dieser Einladungslink gilt nicht mehr: Er wurde schon benutzt, zurückgezogen oder durch einen neueren ersetzt. Nutzen Sie den Link aus der neuesten Einladungs-E-Mail, oder bitten Sie Ihre Apothekenleitung um eine neue Einladung.',
      },
      expired: {
        title: 'Einladung abgelaufen',
        text: 'Diese Einladung ist abgelaufen. Bitten Sie Ihre Apothekenleitung, sie erneut zu senden.',
      },
      anotherAddress: {
        title: 'Einladung für eine andere Adresse',
        text: 'Diese Einladung gilt für eine andere E-Mail-Adresse als die Ihres Kontos. Wurde sie an Sie weitergeleitet, bitten Sie die Apothekenleitung, Ihre eigene Adresse einzuladen. Ist es Ihre Einladung, melden Sie sich mit der eingeladenen Adresse an und öffnen Sie den Link erneut.',
      },
      emailNotVerified: {
        title: 'E-Mail-Adresse nicht bestätigt',
        text: 'Ihre E-Mail-Adresse ist noch nicht bestätigt. Bestätigen Sie sie über den Link in der Bestätigungs-E-Mail, und öffnen Sie dann den Einladungslink erneut.',
      },
      identityIncomplete: {
        title: 'Konto unvollständig',
        text: 'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil, und öffnen Sie dann den Einladungslink erneut.',
      },
      alreadyMember: {
        title: 'Bereits Mitglied',
        text: 'Sie sind bereits Mitglied dieser Apotheke. Wählen Sie sie unter „Apotheke“ in der Seitenleiste.',
      },
      goToCourses: 'Zu „Meine Schulungen“',
      tooMany: {
        title: 'Zu viele Versuche',
        text: 'Versuchen Sie es in einigen Minuten erneut.',
      },
      failed: {
        title: 'Einladung nicht angenommen',
        text: 'Die Einladung konnte nicht angenommen werden. Versuchen Sie es erneut.',
      },
    },
  },
  editorial: {
    inbox: {
      title: 'Eingang',
      statuses: 'Status',
      columns: {
        title: 'Schulung',
        number: 'Version',
        status: 'Status',
      },
      loadFailed: 'Der Eingang konnte nicht geladen werden.',
      empty: {
        Draft: 'Keine Entwürfe.',
        InReview: 'Keine Versionen in Prüfung.',
        Published: 'Keine veröffentlichten Versionen.',
      },
    },
    course: {
      new: 'Neue Schulung',
      form: {
        label: 'Neue Schulung anlegen',
        description: 'Die Schulung beginnt mit Version 1 als Entwurf.',
        title: 'Titel',
        submit: 'Anlegen',
        titleRequired: 'Geben Sie der Schulung einen Titel.',
        titleTooLong: 'Der Titel einer Schulung hat höchstens 150 Zeichen.',
        titleHasNul: 'Der Titel darf kein Nullzeichen enthalten.',
      },
      created: 'Die Schulung {title} wurde angelegt.',
      createFailed: 'Die Schulung {title} konnte nicht angelegt werden.',
    },
    cancel: 'Abbrechen',
    version: {
      number: 'Version {number}',
      loadFailed: 'Die Version konnte nicht geladen werden.',
      notFound: 'Diese Version gibt es nicht.',
      backToInbox: 'Zurück zum Eingang',
      readOnly: {
        InReview: 'Diese Version ist in Prüfung und kann nicht bearbeitet werden.',
        Published:
          'Diese Version ist veröffentlicht und kann nicht mehr geändert werden. Änderungen machen Sie in einer neuen Version.',
        Superseded: 'Diese Version wurde durch eine neuere abgelöst und kann nicht geändert werden.',
        Retired: 'Diese Version wurde zurückgezogen und kann nicht geändert werden.',
      },
    },
    chapters: {
      title: 'Kapitel',
      empty: 'Diese Version hat noch keine Kapitel.',
      add: 'Kapitel hinzufügen',
      position: 'Kapitel {position}',
      duration: 'Dauer {duration}',
      file: 'Datei hochgeladen',
      edit: '„{title}“ bearbeiten',
      remove: '„{title}“ löschen',
      moveUp: '„{title}“ nach oben verschieben',
      moveDown: '„{title}“ nach unten verschieben',
      dragHint: 'Zum Verschieben ziehen',
      moved: '„{title}“ steht jetzt an Position {position} von {count}.',
      reorderFailed:
        'Die Reihenfolge konnte nicht gespeichert werden. Die vorherige Reihenfolge ist wiederhergestellt.',
      removeConfirm: {
        title: 'Kapitel löschen?',
        text: '„{title}“ wird gelöscht.',
        questions:
          'Mit dem Kapitel wird eine Frage gelöscht, die sich darauf bezieht. | Mit dem Kapitel werden {count} Fragen gelöscht, die sich darauf beziehen.',
        confirm: 'Löschen',
      },
      removed: 'Das Kapitel „{title}“ wurde gelöscht.',
      removeFailed: 'Das Kapitel „{title}“ konnte nicht gelöscht werden.',
      saved: 'Das Kapitel „{title}“ wurde gespeichert.',
      saveFailed: 'Das Kapitel „{title}“ konnte nicht gespeichert werden.',
    },
    chapterForm: {
      addTitle: 'Neues Kapitel',
      editTitle: 'Kapitel bearbeiten',
      title: 'Titel',
      kind: 'Art',
      text: 'Inhalt (Markdown)',
      write: 'Schreiben',
      preview: 'Vorschau',
      previewEmpty: 'Noch nichts zur Vorschau.',
      fileKept: 'Die bisherige Datei bleibt, wenn Sie keine neue wählen.',
      requalify:
        'Nach dem Speichern muss eine freigegebene Frage zu diesem Kapitel erneut geprüft werden. | Nach dem Speichern müssen {count} freigegebene Fragen zu diesem Kapitel erneut geprüft werden.',
      submit: 'Speichern',
      titleRequired: 'Geben Sie dem Kapitel einen Titel.',
      titleTooLong: 'Der Titel eines Kapitels hat höchstens 150 Zeichen.',
      titleHasNul: 'Der Titel darf kein Nullzeichen enthalten.',
      textRequired: 'Schreiben Sie den Inhalt des Kapitels.',
      textTooLong: 'Der Inhalt eines Kapitels hat höchstens {max} Zeichen.',
      textHasNul: 'Der Inhalt darf kein Nullzeichen enthalten.',
      fileRequired: 'Wählen Sie eine Datei.',
      file: { Pdf: 'PDF-Datei', Video: 'Video (MP4)' },
      fileWrongType: { Pdf: 'Wählen Sie eine PDF-Datei.', Video: 'Wählen Sie ein Video im Format MP4.' },
      fileEmpty: 'Die Datei ist leer.',
      fileTooLarge: 'Die Datei ist zu groß: höchstens {max}.',
      duration: 'Dauer (Minuten:Sekunden)',
      durationDetected: 'Aus dem Video gelesen.',
      durationUnknown: 'Die Dauer ließ sich nicht aus dem Video lesen. Bitte geben Sie sie ein.',
      durationRequired: 'Geben Sie die Dauer an, z. B. 12:30.',
      durationInvalid: 'Die Dauer liegt zwischen 0:01 und 240:00.',
    },
    upload: {
      progress: 'Upload von {name}',
      progressValue: '{percent} hochgeladen',
      cancel: 'Upload abbrechen',
      cancelled: 'Der Upload wurde abgebrochen; das Kapitel wurde nicht gespeichert.',
      failed: 'Der Upload ist fehlgeschlagen. Versuchen Sie es erneut.',
    },
  },
  samples: {
    manage: 'Verwalten',
    permissionsFailed: 'Die Berechtigungen für {module} konnten nicht geladen werden.',
    title: 'Beispiele',
    loadFailed: 'Die Beispiele konnten nicht geladen werden.',
    empty: 'Noch keine Beispiele.',
    createFirst: 'Erstes Beispiel anlegen',
    form: {
      label: 'Neues Beispiel',
      name: 'Name',
      submit: 'Anlegen',
      nameRequired: 'Geben Sie dem Beispiel einen Namen.',
      nameTooLong: 'Der Name eines Beispiels hat höchstens 100 Zeichen.',
    },
    created: 'Das Beispiel {name} wurde angelegt.',
    createFailed: 'Das Beispiel {name} konnte nicht angelegt werden.',
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
    pagination: 'Seiten',
    previous: 'Vorherige Seite',
    next: 'Nächste Seite',
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
