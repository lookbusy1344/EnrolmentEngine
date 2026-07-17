<script setup lang="ts">
import { prettify } from '../display/formatting'

defineProps<{
  chosenALevels: readonly string[]
}>()
</script>

<template>
  <section class="chosen-summary sticky-top bg-body border-bottom py-2 mb-4" aria-labelledby="chosen-heading">
    <h2 id="chosen-heading" class="h5">Your basket</h2>
    <p v-if="chosenALevels.length === 0" class="mb-0 text-body-secondary">None chosen yet.</p>
    <ul v-else class="list-inline mb-0">
      <li v-for="subject in chosenALevels" :key="subject" class="list-inline-item badge text-bg-primary rounded-pill">
        {{ prettify(subject) }}
      </li>
    </ul>
  </section>
</template>
