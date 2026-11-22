import type { MessageSchema } from './de';

// The English catalog. It follows the glossary's code column; the keys are the German catalog's.
export const en: MessageSchema = {
  app: {
    name: 'pharma-training',
  },
  language: {
    label: 'Language',
    de: 'Deutsch',
    en: 'English',
  },
  glossary: {
    membershipRole: {
      Manager: 'Pharmacy manager',
      Employee: 'Employee',
    },
    jobFunction: {
      Pharmacist: 'Pharmacist',
      Pta: 'PTA (pharmaceutical-technical assistant)',
      Pka: 'PKA (pharmaceutical-commercial employee)',
      Trainee: 'Trainee',
      Other: 'Other',
    },
  },
  auth: {
    signedIn: 'Signed in',
    signedInAs: 'Signed in as {name}',
    signOut: 'Sign out',
    signInFailed: 'Sign-in failed: {message}',
    signingIn: 'Signing in…',
    signingOut: 'Signing out…',
  },
  theme: {
    label: 'Theme',
    light: 'Light',
    dark: 'Dark',
    system: 'System',
  },
  home: {
    choosePharmacy: 'Choose your pharmacy',
    noPharmacy: {
      title: 'No pharmacy yet',
      text: 'You are not a member of any pharmacy yet. Register your pharmacy, or ask its manager for an invitation.',
    },
  },
  modules: {
    learning: { title: 'My courses' },
    assessment: { title: 'Tests' },
    certification: { title: 'Certificates' },
    compliance: { title: 'Assignments' },
    catalog: { title: 'Course catalogue' },
    organisation: {
      title: 'Members',
      pages: {
        register: { title: 'Register a pharmacy' },
      },
    },
    editorial: {
      title: 'Editorial',
      details: {
        version: { title: 'Course version' },
      },
    },
  },
  organisation: {
    register: {
      title: 'Register a pharmacy',
      intro: 'You will manage the pharmacy: invite your team and assign their training.',
      name: 'Pharmacy name',
      jobFunction: 'Your job function',
      choose: 'Choose…',
      submit: 'Register pharmacy',
      nameRequired: 'Give the pharmacy a name.',
      nameTooLong: "A pharmacy's name is at most 120 characters.",
      nameHasNul: "A pharmacy's name cannot contain a nul character.",
      jobFunctionRequired: 'Choose what you do in the pharmacy.',
      registered: 'The pharmacy {name} was registered.',
      registeredNotOpened:
        'The pharmacy {name} was registered, but it could not be opened. Choose it under “Pharmacy” in the sidebar.',
      failed: 'The pharmacy could not be registered. Try again.',
    },
  },
  samples: {
    manage: 'Manage',
    permissionsFailed: 'Permissions for {module} could not be loaded.',
    title: 'Samples',
    loadFailed: 'The samples could not be loaded.',
    empty: 'No samples yet.',
    createFirst: 'Create a sample',
    form: {
      label: 'New sample',
      name: 'Name',
      submit: 'Create sample',
      nameRequired: 'Give the sample a name.',
      nameTooLong: "A sample's name is at most 100 characters.",
    },
    created: 'The sample {name} was created.',
    createFailed: 'The sample {name} could not be created.',
  },
  shell: {
    home: 'Home',
    navigation: {
      label: 'Sections',
    },
    palette: {
      open: 'Find a page…',
      title: 'Open a page',
      description: 'Choose the page you want to open.',
      pages: 'Pages',
      empty: 'No page matches.',
      shortcut: 'Ctrl K',
    },
    gate: {
      loading: 'Loading…',
      notAvailable: 'Not available',
      notForYou: 'This page is not available to you.',
      notInPharmacy: 'This page is not available to you in {name}.',
      pharmaciesFailed: 'Your pharmacies could not be loaded.',
      noPharmacy: 'You are not a member of any pharmacy yet.',
      choosePharmacy: 'Choose a pharmacy to see this page.',
      goHome: 'Back to Home',
    },
    retry: 'Try again',
    pharmacy: {
      region: 'Pharmacy',
      list: 'Pharmacies',
      choose: 'Choose a pharmacy',
      current: 'Pharmacy: {name}',
      failed: 'Pharmacies could not be loaded.',
      none: 'You are not a member of any pharmacy yet.',
      register: 'Register a pharmacy',
    },
  },
  toasts: {
    region: 'Notifications',
    close: 'Close notification',
  },
  dataTable: {
    density: 'Density',
    comfortable: 'Comfortable',
    compact: 'Compact',
    columns: 'Columns',
    visibleColumns: 'Visible columns',
    loading: 'Loading…',
    actions: 'Actions',
    showing: 'Showing {first}–{last} of {total}',
    pagination: 'Pages',
    previous: 'Previous page',
    next: 'Next page',
  },
  ui: {
    close: 'Close',
    sidebar: {
      toggle: 'Toggle sidebar',
      title: 'Sidebar',
      description: 'Shows the sidebar on small screens.',
    },
    command: {
      title: 'Command palette',
      description: 'Search for a command.',
    },
    breadcrumb: {
      label: 'Breadcrumb',
      more: 'More',
    },
  },
};
