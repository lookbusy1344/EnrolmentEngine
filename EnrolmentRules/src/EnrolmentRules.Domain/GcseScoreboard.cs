namespace EnrolmentRules.Domain;

/// <summary>
///     The live GCSE tally shown in the basket: how many GCSEs the student has entered, their summed points
///     and their mean, all on the 1–9 grade scale. <see cref="Average" /> is defined exactly as the
///     prediction stage's own average (mean of the graded results, zero when none are entered), so the
///     scoreboard and the enrolment decision never disagree about "your GCSE average". The TypeScript front
///     end mirrors this in <c>state/enrolmentState.ts</c>.
/// </summary>
public readonly record struct GcseScoreboard(int Count, int Total, double Average)
{
	public static GcseScoreboard From(IReadOnlyCollection<GcseResult> gcses)
	{
		ArgumentNullException.ThrowIfNull(gcses);
		if (gcses.Count == 0) {
			return new(0, 0, 0.0);
		}

		var total = gcses.Sum(static g => g.Grade);
		return new(gcses.Count, total, total / (double)gcses.Count);
	}
}
