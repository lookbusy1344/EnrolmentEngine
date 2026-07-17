<script setup lang="ts">
import type { EnrolmentEvaluateResponse } from '../api/contracts'
import SubjectCard from './SubjectCard.vue'

const props = defineProps<{
  evaluation: EnrolmentEvaluateResponse | null
  chosenALevels: readonly string[]
  hasFacts: boolean
}>()

const emit = defineEmits<{
  choose: [subject: string]
  remove: [subject: string]
}>()

function isChosen(subject: string): boolean {
  return props.chosenALevels.includes(subject)
}
</script>

<template>
  <section aria-labelledby="results-heading">
    <span class="section-eyebrow">Step 2</span>
    <h2 id="results-heading">What's open to you</h2>

    <p v-if="evaluation === null && !hasFacts">
      Add a GCSE, a prior qualification or your date of birth — your options sprout up here.
    </p>
    <p v-else-if="evaluation === null">Working out your options…</p>
    <div v-else-if="evaluation.validationErrors.length > 0" class="alert alert-danger" role="alert">
      <ul class="mb-0">
        <li v-for="error in evaluation.validationErrors" :key="error">
          {{ error }}
        </li>
      </ul>
    </div>
    <div v-else-if="evaluation.result === null" class="alert alert-danger" role="alert">
      <p class="fw-bold">Not eligible.</p>
    </div>
    <div v-else-if="!evaluation.result.eligible" class="alert alert-danger" role="alert">
      <p class="fw-bold">Not eligible.</p>
      <ul class="mb-0">
        <li v-for="reason in evaluation.result.eligibilityReasons" :key="reason">
          {{ reason }}
        </li>
      </ul>
    </div>
    <template v-else>
      <div
        v-if="evaluation.result.choiceLimitReason !== null"
        id="choice-limit-notice"
        class="alert alert-info"
        role="status"
      >
        <p class="fw-bold mb-1">Choice limit reached.</p>
        <p class="mb-0">
          {{ evaluation.result.choiceLimitReason }}
        </p>
      </div>

      <div class="row row-cols-1 row-cols-md-2 row-cols-lg-3 g-3">
        <SubjectCard
          v-for="explanation in evaluation.result.explanations"
          :key="explanation.subject.value"
          :explanation="explanation"
          :chosen="isChosen(explanation.subject.value)"
          @choose="emit('choose', $event)"
          @remove="emit('remove', $event)"
        />
      </div>
    </template>
  </section>
</template>
