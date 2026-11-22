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
};
