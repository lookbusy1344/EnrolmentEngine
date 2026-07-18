namespace EnrolmentRules.Engine;

using Domain;

/// <summary>
///     The rules-as-prose surface: what a subject requires, in English a student can act on, with no
///     student input involved. Separate from <see cref="IEnrolmentEvaluator" /> because it answers a
///     different question — "what would I need for Physics?" rather than "what did this student get?" —
///     and a host that only publishes a course prospectus needs no evaluation surface at all.
/// </summary>
public interface IEnrolmentCriteriaExplainer
{
	/// <summary>Every criterion that decides <paramref name="subject" />'s rating, as student-facing bullets.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="subject" /> is not in the loaded catalogue.</exception>
	SubjectCriteria Describe(Subject subject);
}
