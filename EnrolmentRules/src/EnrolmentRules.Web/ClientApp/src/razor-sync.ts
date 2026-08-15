import { clearSnapshot, loadSnapshot, saveSnapshot } from './state/localStorageSnapshot'
import type { EnrolmentSnapshot } from './state/enrolmentState'

/**
 * Keeps /razor's server-rendered facts in the same localStorage key/shape /app owns
 * (`enrolmentRules.vue.snapshot.v1`), so switching between the two front ends — or closing and
 * reopening the browser — carries the same selections. /razor's own per-request state travels in
 * a plain cookie purely to bridge its POST-redirect-GET hop; this script is what makes that
 * cookie-backed render agree with localStorage, in both directions.
 */
function main(): void {
  const snapshotElement = document.getElementById('enrolment-snapshot')
  const flags = document.getElementById('enrolment-sync-flags')
  const hydrateForm = document.getElementById('hydrate-form')
  if (
    !(snapshotElement instanceof HTMLScriptElement) ||
    !(flags instanceof HTMLElement) ||
    !(hydrateForm instanceof HTMLFormElement)
  ) {
    return
  }

  const rendered = JSON.parse(snapshotElement.textContent) as EnrolmentSnapshot
  const rawPolicyId = flags.dataset.selectedPolicy
  const selectedPolicyId = rawPolicyId === undefined || rawPolicyId === '' ? null : rawPolicyId
  const isEmpty = flags.dataset.empty === 'true'
  const justCleared = flags.dataset.cleared === 'true'

  if (isEmpty && justCleared) {
    clearSnapshot(selectedPolicyId, window.localStorage)
    return
  }

  if (isEmpty) {
    const stored = loadSnapshot(localStorage)
    if (!isEmptySnapshot(stored.snapshot)) {
      submitHydrateForm(hydrateForm, stored.snapshot, stored.selectedPolicyId)
      return
    }
  }

  saveSnapshot(rendered, selectedPolicyId, window.localStorage)
}

function isEmptySnapshot(snapshot: EnrolmentSnapshot): boolean {
  return (
    snapshot.dateOfBirth === null &&
    snapshot.gcses.length === 0 &&
    snapshot.priorQualifications.length === 0 &&
    snapshot.hobbies.length === 0 &&
    snapshot.chosenALevels.length === 0
  )
}

function submitHydrateForm(form: HTMLFormElement, snapshot: EnrolmentSnapshot, selectedPolicyId: string | null): void {
  if (snapshot.dateOfBirth !== null) {
    appendHidden(form, 'DateOfBirth', snapshot.dateOfBirth)
  }

  snapshot.gcses.forEach((row, index) => {
    appendHidden(form, `Gcses[${String(index)}].Subject`, row.subject)
    if (row.grade !== null) {
      appendHidden(form, `Gcses[${String(index)}].Grade`, String(row.grade))
    }
  })

  snapshot.priorQualifications.forEach((row, index) => {
    appendHidden(form, `PriorQualifications[${String(index)}].Subject`, row.subject)
    appendHidden(form, `PriorQualifications[${String(index)}].Type`, row.type)
    appendHidden(form, `PriorQualifications[${String(index)}].Grade`, row.grade)
  })

  snapshot.hobbies.forEach((hobby, index) => {
    appendHidden(form, `Hobbies[${String(index)}]`, hobby)
  })

  snapshot.chosenALevels.forEach((subject, index) => {
    appendHidden(form, `chosenALevels[${String(index)}]`, subject)
  })

  if (selectedPolicyId !== null) {
    const url = new URL(form.action)
    url.searchParams.set('policy', selectedPolicyId)
    form.action = url.toString()
  }

  form.submit()
}

function appendHidden(form: HTMLFormElement, name: string, value: string): void {
  const input = document.createElement('input')
  input.type = 'hidden'
  input.name = name
  input.value = value
  form.append(input)
}

// DOMContentLoaded has already fired by the time a module script this late in <body> executes,
// but guard anyway so this stays correct if the script tag ever moves to <head>.
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', main)
} else {
  main()
}
