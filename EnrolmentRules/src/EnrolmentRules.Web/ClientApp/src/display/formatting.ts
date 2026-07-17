// Mirrors EnrolmentRules.Web.Models.TextFormatting.Prettify — used for a raw subject/hobby key the
// options payload hasn't labelled yet (e.g. a chosen A-level echoed before options finish loading).

export function prettify(key: string): string {
  if (key.length === 0) {
    return key
  }

  return key
    .split('_')
    .filter((word) => word.length > 0)
    .map((word) => word[0].toUpperCase() + word.slice(1))
    .join(' ')
}

/** Whole-years age for a "YYYY-MM-DD" date of birth as of `today` — display only, mirrors AgeCalculator.WholeYears. */
export function wholeYears(dateOfBirth: string, today: Date): number {
  const parts = dateOfBirth.split('-').map(Number)
  if (parts.length !== 3) {
    return 0
  }

  const [birthYear, birthMonth, birthDay] = parts as [number, number, number]
  let age = today.getFullYear() - birthYear
  const birthdayPassedThisYear =
    today.getMonth() + 1 > birthMonth || (today.getMonth() + 1 === birthMonth && today.getDate() >= birthDay)
  if (!birthdayPassedThisYear) {
    age -= 1
  }

  return age
}
