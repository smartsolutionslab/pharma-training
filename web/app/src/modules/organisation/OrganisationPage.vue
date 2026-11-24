<script setup lang="ts">
import { MailPlus, Users } from '@lucide/vue';
import { useIntervalFn } from '@vueuse/core';
import { computed, h, nextTick, ref, shallowRef, useId, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type {
  ChangeOrganisationMembershipRequest,
  InviteOrganisationMemberRequest,
  PharmacyInvitationDto,
  PharmacyMemberDto,
} from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { i18n, t as translate } from '@/i18n';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import ChangeMembershipDialog from './ChangeMembershipDialog.vue';
import ConfirmMemberActionDialog from './ConfirmMemberActionDialog.vue';
import { isExpired } from './invitationExpiry';
import InviteMemberDialog from './InviteMemberDialog.vue';
import { teamFailureText, teamLoadFailureKey } from './teamErrors';
import { titleKey } from './titleKey';
import { useTeam } from './useTeam';
import { useTeamActions } from './useTeamActions';

// The pharmacy's members and invitations (#12): a manager sees its active members and its invitations, invites,
// resends, revokes, changes a membership and ends one. The page asks the host only for a membership that may manage
// members (ADR 0003: the host decides, the page only hides). Each refusal says why; revoking and ending ask first.

/** The caller's own row says it is theirs. */
type MemberRow = PharmacyMemberDto & { isYou: boolean };

/** An invitation's row says whether it has expired, by the page's clock. */
type InvitationRow = PharmacyInvitationDto & { isExpired: boolean };

const dateOf = (value: string) => i18n.global.d(new Date(value), 'date');

const memberColumns = columnsOf<MemberRow>();
const membersColumns = memberColumns.columns([
  memberColumns.accessor('name', {
    ...translatedHeader('organisation.team.columns.name'),
    cell: ({ row }) =>
      row.original.isYou ? translate('organisation.team.members.you', { name: row.original.name }) : row.original.name,
  }),
  memberColumns.accessor('jobFunction', {
    ...translatedHeader('organisation.team.columns.jobFunction'),
    cell: ({ row }) => translate(`glossary.jobFunction.${row.original.jobFunction}`),
  }),
  memberColumns.accessor('role', {
    ...translatedHeader('organisation.team.columns.role'),
    cell: ({ row }) => translate(`glossary.membershipRole.${row.original.role}`),
  }),
  memberColumns.accessor('since', {
    ...translatedHeader('organisation.team.columns.since'),
    cell: ({ row }) => dateOf(row.original.since),
  }),
]);

const invitationColumns = columnsOf<InvitationRow>();
const invitationsColumns = invitationColumns.columns([
  invitationColumns.accessor('email', translatedHeader('organisation.team.columns.email')),
  invitationColumns.accessor('jobFunction', {
    ...translatedHeader('organisation.team.columns.jobFunction'),
    cell: ({ row }) => translate(`glossary.jobFunction.${row.original.jobFunction}`),
  }),
  invitationColumns.accessor('role', {
    ...translatedHeader('organisation.team.columns.role'),
    cell: ({ row }) => translate(`glossary.membershipRole.${row.original.role}`),
  }),
  invitationColumns.accessor('expiresAt', {
    ...translatedHeader('organisation.team.columns.expires'),
    cell: ({ row }) => dateOf(row.original.expiresAt),
  }),
  invitationColumns.display({
    id: 'status',
    ...translatedHeader('organisation.team.columns.status'),
    // The word carries the meaning; the colour only repeats it.
    cell: ({ row }) =>
      row.original.isExpired
        ? h('span', { class: 'text-destructive' }, translate('organisation.team.invitations.expired'))
        : h('span', translate('organisation.team.invitations.open')),
  }),
]);

const noMembers: MemberRow[] = [];
const noInvitations: InvitationRow[] = [];

const { t } = useI18n();
const { current } = useCurrentPharmacy();
const canManage = computed(() => current.value?.canManageMembers === true);

const { members, invitations } = useTeam(() => current.value?.id, canManage);
const { data: membersPage, isPending: membersPending, isError: membersFailed, error: membersError } = members;
const {
  data: invitationsPage,
  isPending: invitationsPending,
  isError: invitationsFailed,
  error: invitationsError,
} = invitations;

const memberRows = computed<MemberRow[]>(() => {
  const items = membersPage.value?.items;
  if (items === undefined) {
    return noMembers;
  }
  return items.map((member) => ({ ...member, isYou: member.membershipId === current.value?.membershipId }));
});
// Now, ticking every minute (an interval, not every animation frame): an open invitation turns expired while the page
// stays open, without reading it again.
const now = shallowRef(new Date());
useIntervalFn(() => {
  now.value = new Date();
}, 60_000);
const invitationRows = computed<InvitationRow[]>(() => {
  const items = invitationsPage.value?.items;
  if (items === undefined) {
    return noInvitations;
  }
  return items.map((invitation) => ({ ...invitation, isExpired: isExpired(invitation.expiresAt, now.value) }));
});

const actions = useTeamActions(current);
const { refusal, renewingId, isRevoking, isEnding } = actions;

const id = useId();
const membersHeadingId = `${id}-members`;
const invitationsHeadingId = `${id}-invitations`;
const membersHeading = useTemplateRef<HTMLElement>('membersHeading');
const invitationsHeading = useTemplateRef<HTMLElement>('invitationsHeading');
const inviteOpener = useTemplateRef<InstanceType<typeof Button>>('inviteOpener');
const pageHeading = useTemplateRef<HTMLElement>('pageHeading');

// No longer allowed to manage members (demoted by their own change, say), the sections and any dialog go: the focus,
// rather than falling to the body, goes to the page's heading, above the words that say why.
watch(canManage, async (allowed, wasAllowed) => {
  if (allowed || !wasAllowed) {
    return;
  }
  await nextTick();
  const focused = document.activeElement;
  if (focused === null || focused === document.body || !focused.isConnected) {
    pageHeading.value?.focus();
  }
});

// Focus: a dialog gives it back to what opened it, unless what happened since says where it belongs (the section's
// heading once a revoke or end has run, the header's Invite once an invitation was sent).
let opener: HTMLElement | undefined;
let focusAfterClose: HTMLElement | undefined;

function openedFrom(event: Event) {
  opener = event.currentTarget instanceof HTMLElement ? event.currentTarget : undefined;
  focusAfterClose = undefined;
}

function dialogClosed(event: Event) {
  event.preventDefault();
  const target = focusAfterClose ?? (opener?.isConnected === true ? opener : undefined);
  focusAfterClose = undefined;
  target?.focus();
}

const inviteOpen = ref(false);

function openInvite(event: Event) {
  openedFrom(event);
  inviteOpen.value = true;
}

async function invite(request: InviteOrganisationMemberRequest) {
  const failure = await actions.invite(request);
  if (failure === undefined) {
    focusAfterClose = inviteOpener.value?.$el as HTMLElement | undefined;
    inviteOpen.value = false;
  }
  return failure;
}

const changeOpen = ref(false);
/** The member last asked about: the dialog keeps its words while it closes. */
const changedMember = ref<PharmacyMemberDto>();

function openChange(event: Event, member: PharmacyMemberDto) {
  openedFrom(event);
  changedMember.value = member;
  changeOpen.value = true;
}

async function change(request: ChangeOrganisationMembershipRequest) {
  const member = changedMember.value;
  if (member === undefined) {
    return undefined;
  }
  const failure = await actions.change(member, request);
  if (failure === undefined) {
    changeOpen.value = false;
  }
  return failure;
}

type Confirmation = { kind: 'revoke'; invitation: PharmacyInvitationDto } | { kind: 'end'; member: PharmacyMemberDto };

const confirmOpen = ref(false);
/** What the confirmation last asked about: it keeps its words while it closes. */
const confirmation = ref<Confirmation>();

function ask(event: Event, asked: Confirmation) {
  openedFrom(event);
  confirmation.value = asked;
  confirmOpen.value = true;
}

const question = computed(() => {
  const asked = confirmation.value;
  const pharmacy = current.value?.name ?? '';
  if (asked === undefined) {
    return { title: '', description: '', confirmLabel: '', pending: false };
  }
  if (asked.kind === 'revoke') {
    return {
      title: t('organisation.team.revoke.title', { email: asked.invitation.email }),
      description: t('organisation.team.revoke.description'),
      confirmLabel: t('organisation.team.revoke.confirm'),
      pending: isRevoking.value,
    };
  }
  const { name, membershipId } = asked.member;
  return {
    title: t('organisation.team.end.title', { name }),
    description:
      membershipId === current.value?.membershipId
        ? t('organisation.team.end.descriptionSelf', { pharmacy })
        : t('organisation.team.end.description', { name, pharmacy }),
    confirmLabel: t('organisation.team.end.confirm'),
    pending: isEnding.value,
  };
});

// Done or refused, the confirmation closes and the focus goes to its section's heading: a revoked or ended row is
// gone, and a refusal shows under that heading.
async function confirm() {
  const asked = confirmation.value;
  if (asked === undefined) {
    return;
  }
  if (asked.kind === 'revoke') {
    await actions.revoke(asked.invitation);
    focusAfterClose = invitationsHeading.value ?? undefined;
  } else {
    await actions.end(asked.member);
    focusAfterClose = membersHeading.value ?? undefined;
  }
  confirmOpen.value = false;
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div class="flex flex-col gap-1">
        <h1 ref="pageHeading" tabindex="-1" class="text-title outline-none">{{ t(titleKey) }}</h1>
        <p v-if="current !== undefined" class="text-body text-muted-foreground">
          {{
            canManage
              ? t('organisation.team.intro', { pharmacy: current.name })
              : t('organisation.team.notAllowed', { pharmacy: current.name })
          }}
        </p>
      </div>
      <Button v-if="canManage" ref="inviteOpener" type="button" @click="openInvite">
        {{ t('organisation.team.invite.open') }}
      </Button>
    </div>

    <template v-if="canManage && current !== undefined">
      <section :aria-labelledby="membersHeadingId" class="flex flex-col gap-4">
        <h2 :id="membersHeadingId" ref="membersHeading" tabindex="-1" class="text-body font-semibold">
          {{ t('organisation.team.members.title') }}
        </h2>
        <p v-if="refusal?.section === 'members'" role="alert" class="text-body text-destructive">
          {{ teamFailureText(refusal.failure) }}
        </p>
        <p v-if="membersFailed" role="alert" class="text-body text-destructive">
          {{ teamFailureText(teamLoadFailureKey(membersError, 'organisation.team.members.loadFailed')) }}
        </p>
        <DataTable v-else :columns="membersColumns" :data="memberRows" :loading="membersPending" :paging="membersPage">
          <template #empty>
            <EmptyState>
              <template #icon><Users /></template>
              {{ t('organisation.team.members.empty') }}
            </EmptyState>
          </template>
          <template #actions="{ row }">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              :aria-label="t('organisation.team.members.changeOf', { name: row.name })"
              @click="(event: Event) => openChange(event, row)"
            >
              {{ t('organisation.team.members.change') }}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              :aria-label="t('organisation.team.members.endOf', { name: row.name })"
              @click="(event: Event) => ask(event, { kind: 'end', member: row })"
            >
              {{ t('organisation.team.members.end') }}
            </Button>
          </template>
        </DataTable>
      </section>

      <section :aria-labelledby="invitationsHeadingId" class="flex flex-col gap-4">
        <h2 :id="invitationsHeadingId" ref="invitationsHeading" tabindex="-1" class="text-body font-semibold">
          {{ t('organisation.team.invitations.title') }}
        </h2>
        <p v-if="refusal?.section === 'invitations'" role="alert" class="text-body text-destructive">
          {{ teamFailureText(refusal.failure) }}
        </p>
        <p v-if="invitationsFailed" role="alert" class="text-body text-destructive">
          {{ teamFailureText(teamLoadFailureKey(invitationsError, 'organisation.team.invitations.loadFailed')) }}
        </p>
        <DataTable
          v-else
          :columns="invitationsColumns"
          :data="invitationRows"
          :loading="invitationsPending"
          :paging="invitationsPage"
        >
          <template #empty>
            <EmptyState>
              <template #icon><MailPlus /></template>
              {{ t('organisation.team.invitations.empty') }}
              <template #action>
                <Button type="button" variant="outline" @click="openInvite">
                  {{ t('organisation.team.invitations.inviteFirst') }}
                </Button>
              </template>
            </EmptyState>
          </template>
          <template #actions="{ row }">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              :disabled="renewingId === row.membershipId"
              :aria-label="t('organisation.team.invitations.renewOf', { email: row.email })"
              @click="actions.renew(row)"
            >
              {{ t('organisation.team.invitations.renew') }}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              :aria-label="t('organisation.team.invitations.revokeOf', { email: row.email })"
              @click="(event: Event) => ask(event, { kind: 'revoke', invitation: row })"
            >
              {{ t('organisation.team.invitations.revoke') }}
            </Button>
          </template>
        </DataTable>
      </section>

      <InviteMemberDialog :open="inviteOpen" :invite="invite" @cancel="inviteOpen = false" @closed="dialogClosed" />
      <ChangeMembershipDialog
        :open="changeOpen"
        :member="changedMember"
        :pharmacy="current.name"
        :change="change"
        @cancel="changeOpen = false"
        @closed="dialogClosed"
      />
      <ConfirmMemberActionDialog
        :open="confirmOpen"
        :title="question.title"
        :description="question.description"
        :confirm-label="question.confirmLabel"
        :pending="question.pending"
        @confirm="confirm"
        @cancel="confirmOpen = false"
        @closed="dialogClosed"
      />
    </template>
  </main>
</template>
