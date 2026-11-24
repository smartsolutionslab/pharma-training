<script setup lang="ts">
import type { CertificateValidity } from '@/api/generated';

// Stub for #54 (T3, red first): whether a certificate is valid or expired, as an icon and a word.
defineProps<{ validity: CertificateValidity }>();
</script>

<template>
  <span></span>
</template>
