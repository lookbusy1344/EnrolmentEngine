<script lang="ts" setup>
import { watch } from 'vue'
import type { OptionItem, QualificationGradeOptions, QualificationSubjectGroup } from '../api/contracts'
import { isEmptyPriorQualificationRow, type PriorQualificationRow } from '../state/enrolmentState'

const props = defineProps<{
  subjectGroups: readonly QualificationSubjectGroup[]
  qualificationGrades: readonly QualificationGradeOptions[]
}>()

function gradeOptionsFor(type: string): readonly OptionItem[] {
  return props.qualificationGrades.find((entry) => entry.type === type)?.grades ?? []
}

/** The exact qualification type whichever group `subjectValue` belongs to represents — inferred rather than picked directly. */
function typeForSubject(subjectValue: string): string {
  return props.subjectGroups.find((group) => group.subjects.some((option) => option.value === subjectValue))?.type ?? ''
}

const rows = defineModel<PriorQualificationRow[]>('rows', { required: true })

watch(
  () => rows.value.length === 0 || !isEmptyPriorQualificationRow(rows.value[rows.value.length - 1]),
  (needsBlankRow) => {
    if (needsBlankRow) {
      rows.value.push({ subject: '', type: '', grade: '' })
    }
  },
  { immediate: true },
)

function removeRow(index: number): void {
  rows.value.splice(index, 1)
}

function setSubject(index: number, value: string): void {
  rows.value[index] = { ...rows.value[index], subject: value, type: typeForSubject(value), grade: '' }
}

function setGrade(index: number, value: string): void {
  rows.value[index] = { ...rows.value[index], grade: value }
}
</script>

<template>
  <fieldset id="qualifications-section" class="border rounded p-3 mb-3">
    <legend class="h6">Other qualifications</legend>
    <div v-for="(row, index) in rows" :key="index" class="row g-2 mb-2 align-items-end">
      <div class="col-sm-6">
        <label :for="`prior-subject-${index}`" class="form-label">Subject</label>
        <select
          :id="`prior-subject-${index}`"
          :value="row.subject"
          class="form-select"
          @change="setSubject(index, ($event.target as HTMLSelectElement).value)"
        >
          <option value="">-- select --</option>
          <optgroup v-for="group in subjectGroups" :key="group.type" :label="group.label">
            <option v-for="option in group.subjects" :key="option.value" :value="option.value">
              {{ option.label }}
            </option>
          </optgroup>
        </select>
      </div>
      <div class="col-sm-4">
        <label :for="`prior-grade-${index}`" class="form-label">Grade</label>
        <select
          :id="`prior-grade-${index}`"
          :value="row.grade"
          class="form-select"
          @change="setGrade(index, ($event.target as HTMLSelectElement).value)"
        >
          <option value="">-- select --</option>
          <option v-for="option in gradeOptionsFor(row.type)" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </div>
      <div v-if="!isEmptyPriorQualificationRow(row)" class="col-sm-2">
        <button class="btn btn-sm btn-outline-danger" type="button" @click="removeRow(index)">Remove</button>
      </div>
    </div>
  </fieldset>
</template>
