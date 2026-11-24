import { useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, ref, toValue, watch } from 'vue';
import { useRouter } from 'vue-router';
import {
  type ChangeOrganisationMembershipRequest,
  getGetOrganisationPharmaciesIdInvitationsQueryKey,
  getGetOrganisationPharmaciesIdMembersQueryKey,
  getGetOrganisationPharmaciesMineQueryKey,
  type InviteOrganisationMemberRequest,
  type MyPharmacyDto,
  type PharmacyInvitationDto,
  type PharmacyMemberDto,
  useDeleteOrganisationPharmaciesIdInvitationsMembershipId,
  useDeleteOrganisationPharmaciesIdMembersMembershipId,
  usePatchOrganisationPharmaciesIdMembersMembershipId,
  usePostOrganisationPharmaciesIdInvitations,
  usePostOrganisationPharmaciesIdInvitationsMembershipIdRenew,
} from '@/api/generated';
import { t } from '@/i18n';
import { notify } from '@/lib/notify';
import { refusesMembership, type TeamAction, type TeamFailure, teamFailureKey, teamFailureText } from './teamErrors';

/** A section of the page, whose heading a refusal of revoke or end shows under. */
export type TeamSection = 'members' | 'invitations';

/** Where the user goes once their own membership has ended: the start page. */
const startPage = '/';

/**
 * The page's five actions on the current pharmacy's members and invitations (#12): each reports what it did, reads
 * again what it changed and, refused, says why (teamFailureKey) in a toast and returns the failure for the page to show
 * in place. A refused revoke or end also stays readable under its section's heading until the next action.
 */
export function useTeamActions(pharmacy: MaybeRefOrGetter<MyPharmacyDto | undefined>) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const inviting = usePostOrganisationPharmaciesIdInvitations();
  const renewing = usePostOrganisationPharmaciesIdInvitationsMembershipIdRenew();
  const revoking = useDeleteOrganisationPharmaciesIdInvitationsMembershipId();
  const changing = usePatchOrganisationPharmaciesIdMembersMembershipId();
  const ending = useDeleteOrganisationPharmaciesIdMembersMembershipId();

  /**
   * The last refused revoke or end, under its section's heading until the next action or another pharmacy. It keeps
   * the failure, not its words, so the page says it in the language of the moment.
   */
  const refusal = ref<{ section: TeamSection; failure: TeamFailure }>();
  watch(
    () => toValue(pharmacy)?.id,
    () => {
      refusal.value = undefined;
    },
  );

  /** Whether the page still shows the pharmacy an action was taken in: a refusal answered after a switch is not its. */
  const stillShown = (takenIn: string | undefined) => takenIn !== undefined && toValue(pharmacy)?.id === takenIn;

  const current = () => {
    const shown = toValue(pharmacy);
    if (shown === undefined) {
      throw new Error('No pharmacy is current: the page offers no action without one.');
    }
    return shown;
  };

  const readMembers = (id: string) =>
    queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesIdMembersQueryKey(id) });
  const readInvitations = (id: string) =>
    queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesIdInvitationsQueryKey(id) });
  const readMine = () => queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });

  /**
   * Runs an action: refused, it says why and reads both lists again (and the memberships, when the membership may be
   * gone), never retrying; done, it runs what follows success.
   */
  async function attempt(
    action: TeamAction,
    values: Record<string, unknown>,
    run: (id: string) => Promise<unknown>,
    done: (id: string) => Promise<unknown>,
    takenIn: MyPharmacyDto = current(),
  ): Promise<TeamFailure | undefined> {
    refusal.value = undefined;
    const { id, name } = takenIn;
    try {
      await run(id);
    } catch (error) {
      const failure = teamFailureKey(action, error, values);
      const reason = teamFailureText(failure);
      // Answered after a switch, the toast shows over another pharmacy: it names the one that refused.
      notify.failure(stillShown(id) ? reason : t('organisation.team.errors.inPharmacy', { pharmacy: name, reason }));
      void readMembers(id);
      void readInvitations(id);
      if (refusesMembership(error)) {
        void readMine();
      }
      return failure;
    }
    await done(id);
    return undefined;
  }

  /** Invites into the pharmacy the dialog was opened on (by default the current one), even if sent after a switch. */
  async function invite(request: InviteOrganisationMemberRequest, takenIn: MyPharmacyDto = current()) {
    const values = { email: request.email };
    return attempt(
      'invite',
      values,
      (id) => inviting.mutateAsync({ id, data: request }),
      async (id) => {
        notify.success(t('organisation.team.invite.invited', values));
        await readInvitations(id);
      },
      takenIn,
    );
  }

  async function renew(invitation: PharmacyInvitationDto) {
    const values = { email: invitation.email };
    return attempt(
      'renew',
      values,
      (id) => renewing.mutateAsync({ id, membershipId: invitation.membershipId }),
      async (id) => {
        notify.success(t('organisation.team.invitations.renewed', values));
        await readInvitations(id);
      },
    );
  }

  async function revoke(invitation: PharmacyInvitationDto) {
    const takenIn = toValue(pharmacy)?.id;
    const values = { email: invitation.email };
    const failure = await attempt(
      'revoke',
      values,
      (id) => revoking.mutateAsync({ id, membershipId: invitation.membershipId }),
      async (id) => {
        notify.success(t('organisation.team.revoke.revoked', values));
        await readInvitations(id);
      },
    );
    if (failure !== undefined && stillShown(takenIn)) {
      refusal.value = { section: 'invitations', failure };
    }
    return failure;
  }

  /** Changes a membership of the pharmacy the dialog was opened on (by default the current one). */
  async function change(
    member: PharmacyMemberDto,
    request: ChangeOrganisationMembershipRequest,
    takenIn: MyPharmacyDto = current(),
  ) {
    const values = { name: member.name };
    const own = member.membershipId === takenIn.membershipId;
    return attempt(
      'change',
      values,
      (id) => changing.mutateAsync({ id, membershipId: member.membershipId, data: request }),
      async (id) => {
        notify.success(t('organisation.team.change.changed', values));
        await Promise.all([readMembers(id), own ? readMine() : undefined]);
      },
      takenIn,
    );
  }

  async function end(member: PharmacyMemberDto) {
    const { id: takenIn, name, membershipId } = current();
    const own = member.membershipId === membershipId;
    const values = { name: member.name, pharmacy: name };
    const failure = await attempt(
      'end',
      values,
      (id) => ending.mutateAsync({ id, membershipId: member.membershipId }),
      async (id) => {
        if (own) {
          notify.success(t('organisation.team.end.endedSelf', values));
          // Only the pharmacy it ended leaves for the start page; switched to another, the page stays on that one.
          const leave = stillShown(takenIn);
          await readMine();
          if (leave) {
            await router.push(startPage);
          }
          return;
        }
        notify.success(t('organisation.team.end.ended', values));
        await readMembers(id);
      },
    );
    if (failure !== undefined && stillShown(takenIn)) {
      refusal.value = { section: 'members', failure };
    }
    return failure;
  }

  /** The invitation whose renewal is on its way: its row's button is disabled until the host has answered. */
  const renewingId = computed(() => (renewing.isPending.value ? renewing.variables.value?.membershipId : undefined));

  return {
    invite,
    renew,
    revoke,
    change,
    end,
    refusal,
    isInviting: inviting.isPending,
    renewingId,
    isRevoking: revoking.isPending,
    isChanging: changing.isPending,
    isEnding: ending.isPending,
  };
}
