<script lang="ts" setup>
import { computed } from 'vue'
import type { ExplanationResponse } from '../api/contracts'
import { prettify } from '../display/formatting'

/** Mirrors EnrolmentRules.Web.Models.RatingDisplay — an amber choice keeps its amber pill, anything else reads as settled. */
const AMBER = 'Amber'
const BORDERLINE_CSS_CLASS = 'text-bg-warning'
const CHOSEN_CSS_CLASS = 'text-bg-primary'

const props = defineProps<{
  chosenALevels: readonly string[]
  explanations: readonly ExplanationResponse[]
}>()

/**
 * Each committed choice paired with its current rating. A choice is only ever green or amber (a red one is
 * ejected before it renders); the rating is missing altogether when the snapshot produced no per-subject
 * ratings — invalid facts, or the eligibility gate failed — and the pill then falls back to plain.
 */
const basket = computed(() => {
  const ratings = new Map(props.explanations.map((explanation) => [explanation.subject.value, explanation]))
  return props.chosenALevels.map((subject) => {
    const explanation = ratings.get(subject)
    const borderline = explanation?.rating === AMBER
    return {
      value: subject,
      label: explanation?.subject.label ?? prettify(subject),
      borderline,
      cssClass: borderline ? BORDERLINE_CSS_CLASS : CHOSEN_CSS_CLASS,
    }
  })
})

const hasBorderlineChoices = computed(() => basket.value.some((entry) => entry.borderline))
</script>

<template>
  <section aria-labelledby="chosen-heading" class="chosen-summary sticky-top bg-body border-bottom py-2 mb-4">
    <h2 id="chosen-heading" class="h5">Your basket</h2>
    <p v-if="basket.length === 0" class="mb-0 text-body-secondary">None chosen yet.</p>
    <template v-else>
      <!-- The amber pill carries the word "Borderline" too, so the flag survives for anyone who cannot use the colour. -->
      <ul class="list-inline mb-0">
        <li
          v-for="entry in basket"
          :key="entry.value"
          :class="entry.cssClass"
          class="list-inline-item badge rounded-pill"
        >
          {{ entry.label }}<span v-if="entry.borderline" class="basket-borderline-tag">Borderline</span>
        </li>
      </ul>
      <p v-if="hasBorderlineChoices" id="borderline-notice" class="mb-0 mt-2 small text-body-secondary">
        Borderline choices stay in your basket, but need additional authorisation before you can enrol on them.
      </p>
    </template>
  </section>
</template>
