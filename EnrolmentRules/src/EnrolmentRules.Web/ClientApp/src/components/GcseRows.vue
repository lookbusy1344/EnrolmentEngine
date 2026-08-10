<script lang="ts" setup>
import { watch } from 'vue'
import type { OptionItem } from '../api/contracts'
import { type GcseRow, isEmptyGcseRow, isGcseSubjectChosenElsewhere } from '../state/enrolmentState'
import { MAX_GCSE_GRADE, MIN_GCSE_GRADE } from '../state/gcseGrade'

const props = defineProps<{
  subjectOptions: readonly OptionItem[]
}>()

const gcseGrades = Array.from({ length: MAX_GCSE_GRADE - MIN_GCSE_GRADE + 1 }, (_, i) => MIN_GCSE_GRADE + i)

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

function selectGrade(index: number, grade: number): void {
  const row = rows.value[index]
  rows.value[index] = { subject: row.subject, grade }
}
</script>

<template>
  <fieldset id="gcse-section" class="border rounded p-3 mb-3">
    <legend class="h6">GCSEs</legend>
    <template v-for="(row, index) in rows" :key="index">
      <div class="row g-2 mb-2 align-items-end">
        <div class="col-12 col-md-3">
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
        <div class="col-12 col-md-auto">
          <span :id="`gcse-grade-label-${index}`" class="form-label d-block">Grade</span>
          <div
            :aria-labelledby="`gcse-grade-label-${index}`"
            class="btn-group btn-group-sm flex-wrap gcse-grade-picker"
            role="group"
          >
            <template v-for="grade in gcseGrades" :key="grade">
              <input
                :id="`gcse-grade-${index}-${grade}`"
                :checked="row.grade === grade"
                :name="`gcse-grade-${index}`"
                autocomplete="off"
                class="btn-check"
                type="radio"
                @change="selectGrade(index, grade)"
              />
              <label :for="`gcse-grade-${index}-${grade}`" class="btn btn-outline-primary">{{ grade }}</label>
            </template>
          </div>
        </div>
        <div v-if="!isEmptyGcseRow(row)" class="col-12 col-md-auto">
          <button class="btn btn-sm btn-outline-danger" type="button" @click="removeRow(index)">Remove</button>
        </div>
      </div>
    </template>
  </fieldset>
</template>
