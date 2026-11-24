import type { MyPharmacyDto } from '@/api/generated';
import type { Audience } from '@/modules';

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
    case 'public':
      return true;
  }
}
