<script setup lang="ts">
import type { EnrolmentOptionsResponse } from '../api/contracts'
import type { GcseRow, PriorQualificationRow } from '../state/enrolmentState'
import GcseRows from './GcseRows.vue'
import HobbyRows from './HobbyRows.vue'
import PriorQualificationRows from './PriorQualificationRows.vue'

defineProps<{
  options: EnrolmentOptionsResponse
  age: number | null
}>()

const dateOfBirth = defineModel<string | null>('dateOfBirth', { required: true })
const gcses = defineModel<GcseRow[]>('gcses', { required: true })
const priorQualifications = defineModel<PriorQualificationRow[]>('priorQualifications', { required: true })
const hobbies = defineModel<string[]>('hobbies', { required: true })

function setDateOfBirth(value: string): void {
  dateOfBirth.value = value === '' ? null : value
}
</script>

<template>
  <section aria-labelledby="facts-heading">
    <span class="section-eyebrow">Step 1</span>
    <h2 id="facts-heading">About you</h2>

    <div class="mb-3 date-field">
      <label for="date-of-birth" class="form-label">Date of birth</label>
      <div class="d-flex align-items-center gap-2 flex-nowrap">
        <input
          id="date-of-birth"
          type="date"
          class="form-control w-auto flex-shrink-0"
          :value="dateOfBirth ?? ''"
          @input="setDateOfBirth(($event.target as HTMLInputElement).value)"
        />
        <span v-if="age !== null" class="form-text mb-0 text-nowrap">Age: {{ age }}</span>
      </div>
    </div>

    <GcseRows v-model:rows="gcses" :subject-options="options.gcseSubjects" />
    <PriorQualificationRows
      v-model:rows="priorQualifications"
      :subject-groups="options.priorQualificationSubjects"
      :qualification-grades="options.qualificationGrades"
    />
    <HobbyRows v-model:rows="hobbies" :hobby-options="options.hobbies" />
  </section>
</template>
