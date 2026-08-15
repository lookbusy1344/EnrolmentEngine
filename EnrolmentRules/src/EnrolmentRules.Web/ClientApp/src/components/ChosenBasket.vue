<script lang="ts" setup>
import { computed, ref, watch } from 'vue'
import type { ChoiceStatus, ExplanationResponse } from '../api/contracts'
import { prettify } from '../display/formatting'
import { gcseScoreboard, type GcseRow } from '../state/enrolmentState'

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
    gcses?: readonly GcseRow[]
    maxChoices?: number | null
  }>(),
  { gcses: () => [], maxChoices: null },
)

const emit = defineEmits<{
  remove: [subject: string]
  clear: []
}>()

// Two-step confirm rather than window.confirm: a native dialog blocks the page (and automated tests)
// and cannot be styled. Reset if the basket empties by another route while the prompt is open.
const confirmingEmpty = ref(false)
watch(
  () => props.chosenALevels.length,
  (length) => {
    if (length === 0) {
      confirmingEmpty.value = false
    }
  },
)

function confirmEmpty(): void {
  confirmingEmpty.value = false
  emit('clear')
}

const scoreboard = computed(() => gcseScoreboard(props.gcses))

/**
 * Every committed choice, kept in the basket regardless of status — a red or not-offered choice is never
 * dropped here, only flagged. Rating (for the amber "Borderline" pill) comes from `explanations`, which
 * only carries offered subjects; a choice outside the selected policy's catalogue has no rating at all.
 */
const basket = computed(() => {
  const ratings = new Map(props.explanations.map((explanation) => [explanation.subject.value, explanation]))
  const statuses = new Map(props.choiceStatuses.map((status) => [status.subject.value, status]))
  return (
    props.chosenALevels
      .map((subject) => {
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
        const unavailable = status?.status === 'Unavailable'
        const notOffered = status?.status === 'NotOffered'
        return {
          value: subject,
          label: status?.subject.label ?? explanation?.subject.label ?? prettify(subject),
          available: status?.status === 'Available',
          borderline,
          unavailable,
          notOffered,
          invalid: unavailable || notOffered,
          reason: status?.reason ?? null,
          cssClass,
        }
      })
      // Valid choices (available or unrated) before invalid ones (unavailable/not-offered), each group
      // alphabetical by label.
      .sort((a, b) => Number(a.invalid) - Number(b.invalid) || a.label.localeCompare(b.label))
  )
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
    <div class="d-flex align-items-baseline justify-content-between gap-3 flex-wrap mb-2">
      <h2 id="chosen-heading" class="h5 mb-0">Your basket</h2>
      <dl v-if="scoreboard.count > 0" id="gcse-scoreboard" aria-label="GCSE scoreboard" class="d-flex gap-3 mb-0 small">
        <div>
          <dt class="d-inline text-body-secondary">GCSEs</dt>
          <dd class="d-inline fw-bold ms-1 text-success" data-testid="scoreboard-count">{{ scoreboard.count }}</dd>
        </div>
        <div>
          <dt class="d-inline text-body-secondary">Pts</dt>
          <dd class="d-inline fw-bold ms-1 text-success" data-testid="scoreboard-total">{{ scoreboard.total }}</dd>
        </div>
        <div>
          <dt class="d-inline text-body-secondary">Avg</dt>
          <dd class="d-inline fw-bold ms-1 text-success" data-testid="scoreboard-average">
            {{ scoreboard.average.toFixed(1) }}
          </dd>
        </div>
      </dl>
    </div>
    <p v-if="basket.length === 0" class="mb-0 text-body-secondary">None chosen yet.</p>
    <template v-else>
      <!-- The amber pill carries the word "Borderline" too, so the flag survives for anyone who cannot use the colour. -->
      <div class="d-flex justify-content-between align-items-start gap-2">
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
            <button
              :aria-label="`Remove ${entry.label}`"
              class="basket-remove"
              type="button"
              @click="emit('remove', entry.value)"
            >
              <span aria-hidden="true">&times;</span>
            </button>
          </li>
        </ul>
        <div class="flex-shrink-0">
          <button
            v-if="!confirmingEmpty"
            id="basket-empty"
            aria-label="Empty basket"
            class="btn btn-sm btn-outline-danger basket-empty-btn"
            title="Empty basket"
            type="button"
            @click="confirmingEmpty = true"
          >
            <svg
              aria-hidden="true"
              fill="currentColor"
              height="16"
              viewBox="0 0 16 16"
              width="16"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5m2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5m3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0z"
              />
              <path
                d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H6a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1h3.5a1 1 0 0 1 1 1zM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4zM2.5 3h11V2h-11z"
              />
            </svg>
          </button>
          <span v-else id="basket-empty-confirm" class="d-inline-flex align-items-center gap-2">
            <button
              aria-label="Confirm empty basket"
              class="btn btn-sm btn-danger basket-empty-btn"
              title="Confirm empty basket"
              type="button"
              @click="confirmEmpty"
            >
              <svg
                aria-hidden="true"
                fill="currentColor"
                height="16"
                viewBox="0 0 16 16"
                width="16"
                xmlns="http://www.w3.org/2000/svg"
              >
                <path
                  d="M13.854 3.646a.5.5 0 0 1 0 .708l-7 7a.5.5 0 0 1-.708 0l-3-3a.5.5 0 1 1 .708-.708L6.5 10.293l6.646-6.647a.5.5 0 0 1 .708 0"
                />
              </svg>
            </button>
            <!-- Cancel sits where the trash button was: a bounced double-tap after opening the confirm lands here, not on the destructive action. -->
            <button
              aria-label="Cancel"
              class="btn btn-sm btn-outline-secondary basket-empty-btn"
              title="Cancel"
              type="button"
              @click="confirmingEmpty = false"
            >
              <svg
                aria-hidden="true"
                fill="currentColor"
                height="16"
                viewBox="0 0 16 16"
                width="16"
                xmlns="http://www.w3.org/2000/svg"
              >
                <path
                  d="M2.146 2.146a.5.5 0 0 1 .708 0L8 7.293l5.146-5.147a.5.5 0 1 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8 2.146 2.854a.5.5 0 0 1 0-.708"
                />
              </svg>
            </button>
          </span>
        </div>
      </div>
      <p v-if="hasBorderlineChoices" id="borderline-notice" class="mb-0 mt-2 small text-body-secondary">
        Borderline choices stay in your basket, but need additional authorisation before you can enrol on them.
      </p>
      <p
        v-if="exceedsChoiceLimit"
        id="basket-choice-limit-error"
        class="alert alert-danger mb-0 mt-2 py-2"
        role="alert"
      >
        Too many subjects chosen: this policy allows at most {{ maxChoices }}, but your basket contains
        {{ availableChoiceCount }}. Remove a choice.
      </p>
    </template>
  </section>
</template>
