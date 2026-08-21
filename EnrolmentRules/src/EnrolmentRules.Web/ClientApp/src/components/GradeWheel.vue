<script lang="ts" setup>
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { createWheelController, type WheelController } from '../gradeWheel/controller'
import { MAX_GCSE_GRADE, MIN_GCSE_GRADE } from '../state/gcseGrade'

defineProps<{
  index: number
  labelledBy: string
}>()

const grade = defineModel<number | null>({ required: true })

const grades = Array.from({ length: MAX_GCSE_GRADE - MIN_GCSE_GRADE + 1 }, (_, i) => MIN_GCSE_GRADE + i)

// Cells are laid out [unset, 1..9], so a cell index equals its grade; index 0 is "Not set".
function valueAtIndex(cellIndex: number): number | null {
  return cellIndex === 0 ? null : grades[cellIndex - 1]
}
function indexOfValue(value: number | null): number {
  return value === null ? 0 : value - MIN_GCSE_GRADE + 1
}

const track = ref<HTMLDivElement | null>(null)
let controller: WheelController | undefined

onMounted(() => {
  const el = track.value
  if (el === null) {
    return
  }
  const cells = Array.from(el.querySelectorAll<HTMLElement>('.gwheel__cell'))
  controller = createWheelController(el, cells, (cellIndex) => {
    grade.value = valueAtIndex(cellIndex)
  })
  controller.setIndex(indexOfValue(grade.value), false)
})

// External changes (a row reindex after a remove, a prior-qualification carry-through) re-centre the
// drum; a change this component itself emitted lands on the same index, a no-op scroll.
watch(grade, (value) => {
  controller?.setIndex(indexOfValue(value), true)
})

onBeforeUnmount(() => {
  controller?.destroy()
})
</script>

<template>
  <div :aria-labelledby="labelledBy" class="gcse-grade-picker gwheel" role="group">
    <div
      ref="track"
      :aria-labelledby="labelledBy"
      :aria-valuenow="grade ?? undefined"
      :aria-valuetext="grade === null ? 'Not set' : `Grade ${grade}`"
      aria-valuemax="9"
      aria-valuemin="1"
      class="gwheel__track"
      role="slider"
      tabindex="0"
    >
      <div aria-hidden="true" class="gwheel__pad"></div>
      <span aria-hidden="true" class="gwheel__cell gwheel__cell--unset" tabindex="-1">–</span>
      <template v-for="g in grades" :key="g">
        <input
          :id="`gcse-grade-${index}-${g}`"
          :checked="grade === g"
          :name="`gcse-grade-${index}`"
          :value="g"
          aria-hidden="true"
          autocomplete="off"
          class="btn-check gwheel__radio"
          tabindex="-1"
          type="radio"
        />
        <label :for="`gcse-grade-${index}-${g}`" class="gwheel__cell">{{ g }}</label>
      </template>
      <div aria-hidden="true" class="gwheel__pad"></div>
    </div>
    <div aria-hidden="true" class="gwheel__lens"></div>
    <div aria-hidden="true" class="gwheel__shade"></div>
  </div>
</template>
