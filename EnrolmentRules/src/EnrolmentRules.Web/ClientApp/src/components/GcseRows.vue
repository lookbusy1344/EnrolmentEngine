<script lang="ts" setup>
import { watch } from 'vue'
import type { OptionItem } from '../api/contracts'
import { type GcseRow, isEmptyGcseRow, isGcseSubjectChosenElsewhere } from '../state/enrolmentState'
import { normalizeGcseGrade } from '../state/gcseGrade'

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

function setGrade(index: number, value: string): void {
  const row = rows.value[index]
  rows.value[index] = { subject: row.subject, grade: value.trim() === '' ? null : Number(value) }
}

// Only on commit (blur or spinner), never per keystroke: mid-typing, "8." reads back as 8, so
// normalising on input would rewrite the field before the user could reach the decimal. Anything
// still off-scale when the request is built is normalised again by toEvaluateRequest.
function commitGrade(index: number): void {
  const row = rows.value[index]
  rows.value[index] = { subject: row.subject, grade: normalizeGcseGrade(row.grade) }
}
</script>

<template>
  <fieldset id="gcse-section" class="border rounded p-3 mb-3">
    <legend class="h6">GCSEs</legend>
    <div v-for="(row, index) in rows" :key="index" class="row g-2 mb-2 align-items-end">
      <div class="col-sm-6">
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
      <div class="col-sm-3">
        <label :for="`gcse-grade-${index}`" class="form-label">Grade</label>
        <input
          :id="`gcse-grade-${index}`"
          :value="row.grade ?? ''"
          class="form-control"
          max="9"
          min="1"
          placeholder="1-9"
          step="any"
          type="number"
          @change="commitGrade(index)"
          @input="setGrade(index, ($event.target as HTMLInputElement).value)"
        />
      </div>
      <div v-if="!isEmptyGcseRow(row)" class="col-sm-3">
        <button class="btn btn-sm btn-outline-danger" type="button" @click="removeRow(index)">Remove</button>
      </div>
    </div>
  </fieldset>
</template>
