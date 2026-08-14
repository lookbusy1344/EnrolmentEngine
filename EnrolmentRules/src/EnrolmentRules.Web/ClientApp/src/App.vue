<script lang="ts" setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type { EnrolmentApiResult, EnrolmentEvaluateResponse, EnrolmentOptionsResponse } from './api/contracts'
import { EnrolmentApiError, EvaluationRequester, fetchOptions, OptionsRequester } from './api/enrolmentApi'
import ChosenBasket from './components/ChosenBasket.vue'
import FactsForm from './components/FactsForm.vue'
import HeroSection from './components/HeroSection.vue'
import ResultsPanel from './components/ResultsPanel.vue'
import { wholeYears } from './display/formatting'
import { debounce } from './state/debounce'
import { type GcseRow, type PriorQualificationRow, toEvaluateRequest } from './state/enrolmentState'
import { clearSnapshot, loadSnapshot, saveSnapshot } from './state/localStorageSnapshot'

const SAVE_DEBOUNCE_MS = 400
const EVALUATE_DEBOUNCE_MS = 400
const POLICY_QUERY_PARAM = 'policy'

const options = ref<EnrolmentOptionsResponse | null>(null)
const optionsError = ref<string | null>(null)
const evaluation = ref<EnrolmentEvaluateResponse | null>(null)
const lastValidComparison = ref<EnrolmentApiResult | null>(null)
const evaluateError = ref<string | null>(null)
const pending = ref(false)
const selectedPolicyId = ref<string | null>(null)

const restored = loadSnapshot(window.localStorage)
const snapshot = reactive<{
  dateOfBirth: string | null
  gcses: GcseRow[]
  priorQualifications: PriorQualificationRow[]
  hobbies: string[]
  chosenALevels: string[]
}>({
  dateOfBirth: restored.snapshot.dateOfBirth,
  gcses: [...restored.snapshot.gcses],
  priorQualifications: [...restored.snapshot.priorQualifications],
  hobbies: [...restored.snapshot.hobbies],
  chosenALevels: [...restored.snapshot.chosenALevels],
})

const requester = new EvaluationRequester()
const optionsRequester = new OptionsRequester()
let suppressSnapshotSideEffects = false

// Bumped on every runEvaluate invocation. EvaluationRequester aborts a superseded in-flight request and
// resolves it to null rather than rejecting, so that call's own finally still runs — without this guard it
// would clear `pending` even though a newer call (the one whose result the student will actually see) is
// still in flight, and "Updating…" would disappear before the real response arrives.
let evaluationGeneration = 0

const age = computed(() => (snapshot.dateOfBirth === null ? null : wholeYears(snapshot.dateOfBirth, new Date())))

// A partially edited row makes the document structurally invalid, so the engine correctly returns
// validation errors without a comparison value. Keep the latest successful annotations for this same
// policy while the student completes the row; otherwise missing statuses make unavailable choices look
// available. Never carry annotations across policies.
const basketComparison = computed(() => {
  const current = evaluation.value?.result
  if (current) {
    return current
  }

  return lastValidComparison.value?.policy.id === selectedPolicyId.value ? lastValidComparison.value : null
})

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
  const generation = ++evaluationGeneration
  pending.value = true
  evaluateError.value = null
  try {
    const result = await requester.evaluate(toEvaluateRequest(snapshot), selectedPolicyId.value ?? undefined)
    if (result === null) {
      return
    }

    if (generation === evaluationGeneration) {
      evaluation.value = result
      if (result.result !== null) {
        lastValidComparison.value = result.result
      }
    }
  } catch (error) {
    if (generation === evaluationGeneration) {
      evaluateError.value =
        error instanceof EnrolmentApiError ? error.message : 'Could not reach the enrolment service.'
    }
  } finally {
    // Only the current invocation may clear `pending` — a superseded call's own finally must not hide
    // that a newer, still-in-flight call is the one the student is actually waiting on.
    if (generation === evaluationGeneration) {
      pending.value = false
    }
  }
}

const saveDebounced = debounce(() => {
  saveSnapshot(snapshot, selectedPolicyId.value, window.localStorage)
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

    saveDebounced.call()
    evaluateDebounced.call()
  },
)

function evaluateImmediately(): void {
  evaluateDebounced.cancel()
  saveDebounced.cancel()
  saveSnapshot(snapshot, selectedPolicyId.value, window.localStorage)
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
  clearSnapshot(selectedPolicyId.value, window.localStorage)
  suppressSnapshotSideEffects = true
  snapshot.dateOfBirth = options.value?.defaultDateOfBirth ?? null
  snapshot.gcses = []
  snapshot.priorQualifications = []
  snapshot.hobbies = []
  snapshot.chosenALevels = []
  evaluation.value = null
  void runEvaluate()
}

function urlPolicyId(): string | null {
  return new URLSearchParams(window.location.search).get(POLICY_QUERY_PARAM)
}

function replaceUrlPolicyId(policyId: string): void {
  const url = new URL(window.location.href)
  url.searchParams.set(POLICY_QUERY_PARAM, policyId)
  window.history.replaceState(window.history.state as unknown, '', url)
}

/**
 * Selection precedence on initial load: a valid `?policy=` URL value, then the locally stored last-viewed
 * policy id, then the server's own default — whichever of those the server accepts first. Trying each in
 * turn (rather than pre-flighting) means an unknown or stale id never needs a dedicated round trip: the
 * `/api/enrolment/options?policy=` 400 response for a bad id doubles as the rejection signal.
 */
async function resolveOptions(candidates: readonly (string | undefined)[]): Promise<EnrolmentOptionsResponse> {
  let lastError: unknown
  for (const candidate of candidates) {
    try {
      return await fetchOptions(candidate)
    } catch (error) {
      if (error instanceof EnrolmentApiError && error.status === 400) {
        lastError = error
        continue
      }

      throw error
    }
  }

  throw lastError
}

async function loadOptionsAndEvaluate(): Promise<void> {
  try {
    options.value = await resolveOptions([
      urlPolicyId() ?? undefined,
      restored.selectedPolicyId ?? undefined,
      undefined,
    ])
  } catch (error) {
    optionsError.value = error instanceof EnrolmentApiError ? error.message : 'Could not load enrolment options.'
    return
  }

  selectedPolicyId.value = options.value.selectedPolicy.id
  replaceUrlPolicyId(options.value.selectedPolicy.id)

  if (!hasEditableSnapshot()) {
    suppressSnapshotSideEffects = true
    snapshot.dateOfBirth = options.value.defaultDateOfBirth
  }

  saveSnapshot(snapshot, selectedPolicyId.value, window.localStorage)
  await runEvaluate()
}

/** Switching keeps the exact facts and basket — only the policy, and therefore the comparison, changes. */
async function switchPolicy(policyId: string): Promise<void> {
  optionsError.value = null
  try {
    const selected = await optionsRequester.fetch(policyId)
    if (selected === null) {
      return
    }

    options.value = selected
  } catch (error) {
    optionsError.value = error instanceof EnrolmentApiError ? error.message : 'Could not load enrolment options.'
    return
  }

  selectedPolicyId.value = options.value.selectedPolicy.id
  replaceUrlPolicyId(options.value.selectedPolicy.id)
  saveSnapshot(snapshot, selectedPolicyId.value, window.localStorage)
  await runEvaluate()
}

onMounted(() => {
  void loadOptionsAndEvaluate()
})
</script>

<template>
  <HeroSection
    :available-policies="options?.availablePolicies ?? []"
    :selected-policy="options?.selectedPolicy ?? null"
    @switch-policy="switchPolicy"
  />
  <ChosenBasket
    :choice-statuses="basketComparison?.choiceStatuses ?? []"
    :chosen-a-levels="snapshot.chosenALevels"
    :explanations="basketComparison?.explanations ?? []"
    :max-choices="basketComparison?.maxChoices ?? null"
  />

  <div v-if="optionsError !== null" class="alert alert-danger" role="alert">
    {{ optionsError }}
  </div>
  <template v-else-if="options !== null">
    <form class="facts-form mb-4" @submit.prevent>
      <FactsForm
        v-model:date-of-birth="snapshot.dateOfBirth"
        v-model:gcses="snapshot.gcses"
        v-model:hobbies="snapshot.hobbies"
        v-model:prior-qualifications="snapshot.priorQualifications"
        :age="age"
        :options="options"
      />
    </form>

    <button class="btn btn-outline-secondary mb-4" type="button" @click="startOver">Start over</button>

    <div v-if="evaluateError !== null" class="alert alert-danger" role="alert">
      {{ evaluateError }}
    </div>
    <ResultsPanel
      v-else
      :chosen-a-levels="snapshot.chosenALevels"
      :evaluation="evaluation"
      :has-facts="hasFacts"
      @choose="chooseSubject"
      @remove="removeSubject"
    />
    <p v-if="pending" class="text-body-secondary" role="status">Updating…</p>
  </template>
  <p v-else>Loading…</p>
</template>
