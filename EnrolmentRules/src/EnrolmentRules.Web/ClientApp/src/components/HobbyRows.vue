<script lang="ts" setup>
import { watch } from 'vue'
import type { OptionItem } from '../api/contracts'

defineProps<{
  hobbyOptions: readonly OptionItem[]
}>()

const rows = defineModel<string[]>('rows', { required: true })

watch(
  () => rows.value.length === 0 || rows.value[rows.value.length - 1].trim() !== '',
  (needsBlankRow) => {
    if (needsBlankRow) {
      rows.value.push('')
    }
  },
  { immediate: true },
)

function removeRow(index: number): void {
  rows.value.splice(index, 1)
}

function setHobby(index: number, value: string): void {
  rows.value[index] = value
}
</script>

<template>
  <fieldset id="hobbies-section" class="border rounded p-3 mb-3">
    <legend class="h6">Hobbies</legend>
    <div v-for="(row, index) in rows" :key="index" class="row g-2 mb-2 align-items-end">
      <div class="col-sm-6">
        <label :for="`hobby-${index}`" class="form-label">Hobby</label>
        <select
          :id="`hobby-${index}`"
          :value="row"
          class="form-select"
          @change="setHobby(index, ($event.target as HTMLSelectElement).value)"
        >
          <option value="">-- select --</option>
          <option v-for="option in hobbyOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </div>
      <div v-if="row.trim() !== ''" class="col-sm-3">
        <button class="btn btn-sm btn-outline-danger" type="button" @click="removeRow(index)">Remove</button>
      </div>
    </div>
  </fieldset>
</template>
