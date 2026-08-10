<script lang="ts" setup>
import { computed } from 'vue'
import type { ExplanationResponse } from '../api/contracts'

const props = defineProps<{
  explanation: ExplanationResponse
  chosen: boolean
}>()

const emit = defineEmits<{
  choose: [subject: string]
  remove: [subject: string]
}>()

const canChoose = computed(() => props.explanation.rating !== 'Red')
const reasonId = computed(() => `reason-${props.explanation.subject.value}`)
</script>

<template>
  <div class="col">
    <article class="card h-100">
      <div class="card-body">
        <h3 class="card-title h6 d-flex justify-content-between align-items-center">
          {{ explanation.subject.label }}
          <span :class="explanation.ratingCssClass" class="badge">
            <span class="visually-hidden">Rating: </span>{{ explanation.rating }}
          </span>
        </h3>
        <p :id="reasonId" class="card-text">
          {{ explanation.reason }}
        </p>
        <details>
          <summary>Details</summary>
          <dl class="mb-0">
            <dt>Base rating</dt>
            <dd>{{ explanation.baseRating }} ({{ explanation.baseReason }})</dd>
            <dt>Rule</dt>
            <dd>{{ explanation.rule }}</dd>
            <dt>Predicted points</dt>
            <dd>{{ explanation.predictedPoints.toFixed(2) }}</dd>
            <template v-if="explanation.entryEquivalentReason !== null">
              <dt>Entry equivalence</dt>
              <dd>{{ explanation.entryEquivalentReason }}</dd>
            </template>
            <template v-if="explanation.overrides.length > 0">
              <dt>Overrides</dt>
              <dd>
                <ul class="mb-0">
                  <li v-for="(adjustment, index) in explanation.overrides" :key="index">
                    {{ adjustment.from }} → {{ adjustment.to }}: {{ adjustment.reason }}
                  </li>
                </ul>
              </dd>
            </template>
          </dl>
        </details>
        <button
          v-if="chosen"
          class="btn btn-sm btn-outline-danger mt-2"
          type="button"
          @click="emit('remove', explanation.subject.value)"
        >
          Remove
        </button>
        <button
          v-else-if="canChoose"
          class="btn btn-sm btn-outline-primary mt-2"
          type="button"
          @click="emit('choose', explanation.subject.value)"
        >
          Choose
        </button>
        <button
          v-else
          :aria-describedby="reasonId"
          class="btn btn-sm btn-outline-secondary mt-2"
          disabled
          type="button"
        >
          Unavailable
        </button>
      </div>
    </article>
  </div>
</template>
