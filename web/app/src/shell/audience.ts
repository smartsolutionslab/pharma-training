import type { MyPharmacyDto } from '@/api/generated';
import { type Audience, type ModulePage, type ModuleRoute, moduleRoutes } from '@/modules';

/** What decides who sees a page: the membership in the chosen pharmacy, if any, and the realm role editor. */
export interface Reach {
  current: MyPharmacyDto | undefined;
  isEditor: boolean;
}

/**
 * Whether a page for this audience is the user's: an employee's needs a chosen pharmacy, a manager's a membership that
 * may manage members or assign courses (its capabilities, not its role's name), an editor's the realm role editor, and
 * a signed-in user's nothing more.
 */
export function canReach(audience: Audience, { current, isEditor }: Reach): boolean {
  switch (audience) {
    case 'employee':
      return current !== undefined;
    case 'manager':
      return current !== undefined && (current.canManageMembers || current.canAssignCourses);
    case 'editor':
      return isEditor;
    case 'signed-in':
      return true;
  }
}

/** The module an address is in, by its first segment; none for the start page or an address no module has. */
export function moduleOf(path: string): ModuleRoute | undefined {
  const [first] = path.split('/').filter((segment) => segment !== '');
  return first === undefined ? undefined : moduleRoutes.find((route) => route.path === first);
}

/**
 * What governs an address: the module's page below it named by the address's second segment, otherwise the module the
 * address is in; none for the start page or an address no module has.
 */
export function pageOf(path: string): ModulePage | undefined {
  const module = moduleOf(path);
  if (module === undefined) {
    return undefined;
  }
  const [, second] = path.split('/').filter((segment) => segment !== '');
  return module.pages?.find((page) => page.path === second) ?? module;
}
