<script lang="ts" setup>
import { computed } from 'vue'
import type { ChoiceStatus, ExplanationResponse } from '../api/contracts'
import { prettify } from '../display/formatting'

/** Mirrors EnrolmentRules.Web.Models.BasketEntry — status (Available/Unavailable/NotOffered) drives the pill first; an amber Available choice keeps its own "Borderline" flag. */
const AMBER = 'Amber'
const BORDERLINE_CSS_CLASS = 'text-bg-warning'
const CHOSEN_CSS_CLASS = 'text-bg-primary'
const UNAVAILABLE_CSS_CLASS = 'text-bg-danger'

const props = withDefaults(
  defineProps<{
    chosenALevels: readonly string[]
    explanations: readonly ExplanationResponse[]
    choiceStatuses: readonly ChoiceStatus[]
    maxChoices?: number | null
  }>(),
  { maxChoices: null },
)

/**
 * Every committed choice, kept in the basket regardless of status — a red or not-offered choice is never
 * dropped here, only flagged. Rating (for the amber "Borderline" pill) comes from `explanations`, which
 * only carries offered subjects; a choice outside the selected policy's catalogue has no rating at all.
 */
const basket = computed(() => {
  const ratings = new Map(props.explanations.map((explanation) => [explanation.subject.value, explanation]))
  const statuses = new Map(props.choiceStatuses.map((status) => [status.subject.value, status]))
  return props.chosenALevels.map((subject) => {
    const status = statuses.get(subject)
    const explanation = ratings.get(subject)
    const borderline = status?.status === 'Available' && explanation?.rating === AMBER
    const cssClass =
      status?.status === 'Unavailable'
        ? UNAVAILABLE_CSS_CLASS
        : status?.status === 'NotOffered'
          ? UNAVAILABLE_CSS_CLASS
          : borderline
            ? BORDERLINE_CSS_CLASS
            : CHOSEN_CSS_CLASS
    return {
      value: subject,
      label: status?.subject.label ?? explanation?.subject.label ?? prettify(subject),
      available: status?.status === 'Available',
      borderline,
      unavailable: status?.status === 'Unavailable',
      notOffered: status?.status === 'NotOffered',
      reason: status?.reason ?? null,
      cssClass,
    }
  })
})

const hasBorderlineChoices = computed(() => basket.value.some((entry) => entry.borderline))
const availableChoiceCount = computed(() => basket.value.filter((entry) => entry.available).length)
const exceedsChoiceLimit = computed(() => props.maxChoices !== null && availableChoiceCount.value > props.maxChoices)
</script>

<template>
  <section
    :class="exceedsChoiceLimit ? ['bg-danger-subtle', 'border', 'border-danger', 'rounded', 'px-3'] : []"
    aria-labelledby="chosen-heading"
    class="chosen-summary sticky-top bg-body border-bottom py-2 mb-4"
  >
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
          {{ entry.label }}<span v-if="entry.borderline" class="basket-borderline-tag"> - Borderline</span
          ><span v-if="entry.unavailable" class="basket-unavailable-tag"> - Unavailable</span
          ><span v-if="entry.notOffered" class="basket-not-offered-tag"> - Not offered</span>
        </li>
      </ul>
      <p v-if="hasBorderlineChoices" id="borderline-notice" class="mb-0 mt-2 small text-body-secondary">
        Borderline choices stay in your basket, but need additional authorisation before you can enrol on them.
      </p>
      <p
        v-if="exceedsChoiceLimit"
        id="basket-choice-limit-error"
        class="alert alert-danger mb-0 mt-2 py-2"
        role="alert"
      >
        Too many available choices: this policy allows at most {{ maxChoices }}, but your basket contains
        {{ availableChoiceCount }}. Remove a choice.
      </p>
    </template>
  </section>
</template>
