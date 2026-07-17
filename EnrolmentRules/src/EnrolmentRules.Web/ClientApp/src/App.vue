<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { EnrolmentEvaluateResponse, EnrolmentOptionsResponse, OptionItem } from './api/contracts'
import { EnrolmentApiError, EvaluationRequester, fetchOptions } from './api/enrolmentApi'
import ChosenBasket from './components/ChosenBasket.vue'
import FactsForm from './components/FactsForm.vue'
import HeroSection from './components/HeroSection.vue'
import ResultsPanel from './components/ResultsPanel.vue'
import { wholeYears } from './display/formatting'
import { debounce } from './state/debounce'
import { toEvaluateRequest, type GcseRow, type PriorQualificationRow } from './state/enrolmentState'
import { clearSnapshot, loadSnapshot, saveSnapshot } from './state/localStorageSnapshot'

const SAVE_DEBOUNCE_MS = 400
const EVALUATE_DEBOUNCE_MS = 400

const options = ref<EnrolmentOptionsResponse | null>(null)
const optionsError = ref<string | null>(null)
const evaluation = ref<EnrolmentEvaluateResponse | null>(null)
const evaluateError = ref<string | null>(null)
const pending = ref(false)
const ejectedNotice = ref<readonly string[]>([])

const restored = loadSnapshot(window.localStorage)
const snapshot = reactive<{
  dateOfBirth: string | null
  gcses: GcseRow[]
  priorQualifications: PriorQualificationRow[]
  hobbies: string[]
  chosenALevels: string[]
}>({
  dateOfBirth: restored.dateOfBirth,
  gcses: [...restored.gcses],
  priorQualifications: [...restored.priorQualifications],
  hobbies: [...restored.hobbies],
  chosenALevels: [...restored.chosenALevels],
})

const requester = new EvaluationRequester()
let suppressSnapshotSideEffects = false

const age = computed(() => (snapshot.dateOfBirth === null ? null : wholeYears(snapshot.dateOfBirth, new Date())))

const hasFacts = computed(
  () =>
    snapshot.dateOfBirth !== null ||
    snapshot.gcses.some((row) => row.subject.trim() !== '') ||
    snapshot.priorQualifications.some((row) => row.subject.trim() !== '') ||
    snapshot.hobbies.some((hobby) => hobby.trim() !== ''),
)

function hasEditableSnapshot(): boolean {
  return (
    snapshot.dateOfBirth !== null ||
    snapshot.gcses.length > 0 ||
    snapshot.priorQualifications.length > 0 ||
    snapshot.hobbies.length > 0 ||
    snapshot.chosenALevels.length > 0
  )
}

async function runEvaluate(): Promise<void> {
  pending.value = true
  evaluateError.value = null
  try {
    const result = await requester.evaluate(toEvaluateRequest(snapshot))
    if (result === null) {
      return
    }

    if (result.ejectedChoices.length > 0) {
      await ejectStaleChoices(result.ejectedChoices)
      return
    }

    evaluation.value = result
  } catch (error) {
    evaluateError.value = error instanceof EnrolmentApiError ? error.message : 'Could not reach the enrolment service.'
  } finally {
    pending.value = false
  }
}

/**
 * Drop choices the engine has refused and evaluate again. A subject was green or amber when it was chosen,
 * so a red one means the student's facts moved underneath it — typically their GCSE grades were lowered.
 * The engine will not evaluate a snapshot that still names one, so the basket is pruned and re-posted; the
 * second call is the one whose result the student sees. One round is always enough: dropping choices only
 * removes downgrades, so nothing left in the basket can newly turn red.
 */
async function ejectStaleChoices(ejected: readonly OptionItem[]): Promise<void> {
  const dropped = new Set(ejected.map((choice) => choice.value))
  snapshot.chosenALevels = snapshot.chosenALevels.filter((subject) => !dropped.has(subject))
  ejectedNotice.value = ejected.map((choice) => choice.label)
  saveSnapshot(snapshot, window.localStorage)
  await runEvaluate()
}

const saveDebounced = debounce(() => {
  saveSnapshot(snapshot, window.localStorage)
}, SAVE_DEBOUNCE_MS)

const evaluateDebounced = debounce(() => {
  void runEvaluate()
}, EVALUATE_DEBOUNCE_MS)

// Facts (date of birth, GCSEs, prior qualifications, hobbies) debounce; choosing/removing a subject
// evaluates immediately instead (see chooseSubject/removeSubject below).
watch(
  () => [
    snapshot.dateOfBirth,
    JSON.stringify(snapshot.gcses),
    JSON.stringify(snapshot.priorQualifications),
    JSON.stringify(snapshot.hobbies),
  ],
  () => {
    if (suppressSnapshotSideEffects) {
      suppressSnapshotSideEffects = false
      return
    }

    // Clear before re-evaluating, not after: the notice describes the ejection this round of facts caused,
    // so raising grades back must retire it rather than leave a stale warning above the basket.
    ejectedNotice.value = []
    saveDebounced.call()
    evaluateDebounced.call()
  },
)

function evaluateImmediately(): void {
  evaluateDebounced.cancel()
  saveDebounced.cancel()
  ejectedNotice.value = []
  saveSnapshot(snapshot, window.localStorage)
  void runEvaluate()
}

function chooseSubject(subject: string): void {
  if (!snapshot.chosenALevels.includes(subject)) {
    snapshot.chosenALevels.push(subject)
  }

  evaluateImmediately()
}

function removeSubject(subject: string): void {
  snapshot.chosenALevels = snapshot.chosenALevels.filter((value) => value !== subject)
  evaluateImmediately()
}

function startOver(): void {
  evaluateDebounced.cancel()
  saveDebounced.cancel()
  clearSnapshot(window.localStorage)
  suppressSnapshotSideEffects = true
  snapshot.dateOfBirth = options.value?.defaultDateOfBirth ?? null
  snapshot.gcses = []
  snapshot.priorQualifications = []
  snapshot.hobbies = []
  snapshot.chosenALevels = []
  evaluation.value = null
  ejectedNotice.value = []
  void runEvaluate()
}

async function loadOptionsAndEvaluate(): Promise<void> {
  try {
    options.value = await fetchOptions()
  } catch (error) {
    optionsError.value = error instanceof EnrolmentApiError ? error.message : 'Could not load enrolment options.'
    return
  }

  if (!hasEditableSnapshot()) {
    suppressSnapshotSideEffects = true
    snapshot.dateOfBirth = options.value.defaultDateOfBirth
  }

  await runEvaluate()
}

onMounted(() => {
  void loadOptionsAndEvaluate()
})
</script>

<template>
  <HeroSection />
  <ChosenBasket :chosen-a-levels="snapshot.chosenALevels" />

  <div v-if="ejectedNotice.length > 0" class="alert alert-warning" role="status">
    Removed from your basket — no longer available with your current grades:
    {{ ejectedNotice.join(', ') }}.
  </div>

  <div v-if="optionsError !== null" class="alert alert-danger" role="alert">
    {{ optionsError }}
  </div>
  <template v-else-if="options !== null">
    <form class="facts-form mb-4" @submit.prevent>
      <FactsForm
        v-model:date-of-birth="snapshot.dateOfBirth"
        v-model:gcses="snapshot.gcses"
        v-model:prior-qualifications="snapshot.priorQualifications"
        v-model:hobbies="snapshot.hobbies"
        :options="options"
        :age="age"
      />
    </form>

    <button type="button" class="btn btn-outline-secondary mb-4" @click="startOver">Start over</button>

    <div v-if="evaluateError !== null" class="alert alert-danger" role="alert">
      {{ evaluateError }}
    </div>
    <ResultsPanel
      v-else
      :evaluation="evaluation"
      :chosen-a-levels="snapshot.chosenALevels"
      :has-facts="hasFacts"
      @choose="chooseSubject"
      @remove="removeSubject"
    />
    <p v-if="pending" class="text-body-secondary" role="status">Updating…</p>
  </template>
  <p v-else>Loading…</p>
</template>
