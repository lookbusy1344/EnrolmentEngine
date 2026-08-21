// jsdom implements neither Element.scrollTo nor layout, so the grade wheel's scroll calls would
// throw under test. Stub it as a no-op; the wheel's selection logic is exercised through its
// radios and the pure geometry in curvature.test.ts, not through real scroll positions.
if (typeof Element.prototype.scrollTo !== 'function') {
  Element.prototype.scrollTo = () => undefined
}
