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
    editorial: { title: 'Editorial' },
  },
  samples: {
    form: {
      name: 'Name',
    },
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
      goHome: 'Go to Home',
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
