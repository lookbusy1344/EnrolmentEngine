<script setup lang="ts">
import { watch } from 'vue'
import type { OptionItem } from '../api/contracts'
import { isEmptyPriorQualificationRow, type PriorQualificationRow } from '../state/enrolmentState'

defineProps<{
  subjectOptions: readonly OptionItem[]
  qualificationTypes: readonly OptionItem[]
}>()

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
  rows.value[index] = { ...rows.value[index], subject: value }
}

function setType(index: number, value: string): void {
  rows.value[index] = { ...rows.value[index], type: value }
}

function setGrade(index: number, value: string): void {
  rows.value[index] = { ...rows.value[index], grade: value }
}
</script>

<template>
  <fieldset id="qualifications-section" class="border rounded p-3 mb-3">
    <legend class="h6">Prior qualifications</legend>
    <div v-for="(row, index) in rows" :key="index" class="row g-2 mb-2 align-items-end">
      <div class="col-sm-4">
        <label :for="`prior-subject-${index}`" class="form-label">Subject</label>
        <select
          :id="`prior-subject-${index}`"
          class="form-select"
          :value="row.subject"
          @change="setSubject(index, ($event.target as HTMLSelectElement).value)"
        >
          <option value="">-- select --</option>
          <option v-for="option in subjectOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </div>
      <div class="col-sm-3">
        <label :for="`prior-type-${index}`" class="form-label">Type</label>
        <select
          :id="`prior-type-${index}`"
          class="form-select"
          :value="row.type"
          @change="setType(index, ($event.target as HTMLSelectElement).value)"
        >
          <option value="">-- select --</option>
          <option v-for="option in qualificationTypes" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </div>
      <div class="col-sm-3">
        <label :for="`prior-grade-${index}`" class="form-label">Grade</label>
        <input
          :id="`prior-grade-${index}`"
          type="text"
          class="form-control"
          :value="row.grade"
          @input="setGrade(index, ($event.target as HTMLInputElement).value)"
        />
      </div>
      <div v-if="!isEmptyPriorQualificationRow(row)" class="col-sm-2">
        <button type="button" class="btn btn-sm btn-outline-danger" @click="removeRow(index)">Remove</button>
      </div>
    </div>
  </fieldset>
</template>
