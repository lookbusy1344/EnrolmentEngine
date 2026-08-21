<script lang="ts" setup>
import { watch } from 'vue'
import type { OptionItem } from '../api/contracts'
import { type GcseRow, isEmptyGcseRow, isGcseSubjectChosenElsewhere } from '../state/enrolmentState'
import GradeWheel from './GradeWheel.vue'

const props = defineProps<{
  subjectOptions: readonly OptionItem[]
}>()

const rows = defineModel<GcseRow[]>('rows', { required: true })

watch(
  () => rows.value.length === 0 || !isEmptyGcseRow(rows.value[rows.value.length - 1]),
  (needsBlankRow) => {
    if (needsBlankRow) {
      rows.value.push({ subject: '', grade: null })
    }
  },
  { immediate: true },
)

function removeRow(index: number): void {
  rows.value.splice(index, 1)
}

function availableSubjects(index: number): OptionItem[] {
  return props.subjectOptions.filter((option) => !isGcseSubjectChosenElsewhere(rows.value, index, option.value))
}

function setSubject(index: number, value: string): void {
  const row = rows.value[index]
  rows.value[index] = { subject: value, grade: row.grade }
}

function selectGrade(index: number, grade: number | null): void {
  const row = rows.value[index]
  rows.value[index] = { subject: row.subject, grade }
}
</script>

<template>
  <fieldset id="gcse-section" class="border rounded p-3 mb-3">
    <legend class="h6">GCSEs</legend>
    <template v-for="(row, index) in rows" :key="index">
      <div class="row g-2 mb-2 align-items-end">
        <!-- Three columns: the grade control and its Remove button take exactly the width they
             need, and the subject select absorbs whatever the row has left. -->
        <div class="col-12 col-md">
          <label :for="`gcse-subject-${index}`" class="form-label">Subject</label>
          <select
            :id="`gcse-subject-${index}`"
            :value="row.subject"
            class="form-select"
            @change="setSubject(index, ($event.target as HTMLSelectElement).value)"
          >
            <option value="">-- select --</option>
            <option v-for="option in availableSubjects(index)" :key="option.value" :value="option.value">
              {{ option.label }}
            </option>
          </select>
        </div>
        <!-- A row with no subject has nothing to grade. From md up the columns sit side by side, so
             the control is hidden in place and its column still holds the width that keeps every
             row's subject select the same size; below md the subject select has the line to itself
             anyway, so the empty row's control goes entirely rather than leaving a gap. Both this
             column and Remove's size to their content at every width, so they share a line wherever
             the row has room for the pair and wrap only where it has not. -->
        <div :class="row.subject === '' ? 'invisible d-none d-md-block' : ''" class="col-auto">
          <span :id="`gcse-grade-label-${index}`" class="form-label d-block">
            Grade
            <span :class="{ 'gwheel-readout--unset': row.grade === null }" aria-hidden="true" class="gwheel-readout">{{
              row.grade === null ? 'Not set' : row.grade
            }}</span>
          </span>
          <GradeWheel
            :index="index"
            :labelled-by="`gcse-grade-label-${index}`"
            :model-value="row.grade"
            @update:model-value="selectGrade(index, $event)"
          />
        </div>
        <!-- The blank trailing row has nothing to remove, but it still holds this column open, so
             its grade control lines up with the rows above rather than being pushed along by a
             subject select that grew into the gap. -->
        <div class="col-auto">
          <button
            :class="isEmptyGcseRow(row) ? 'invisible d-none d-md-inline-block' : ''"
            :disabled="isEmptyGcseRow(row)"
            :tabindex="isEmptyGcseRow(row) ? -1 : undefined"
            class="btn btn-sm btn-outline-danger row-remove-btn"
            type="button"
            @click="removeRow(index)"
          >
            Remove
          </button>
        </div>
      </div>
    </template>
  </fieldset>
</template>
