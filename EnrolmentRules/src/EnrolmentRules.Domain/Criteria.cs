namespace EnrolmentRules.Domain;

/// <summary>
///     A plain-English description of everything that decides one subject's rating, written for a
///     student rather than an administrator. Every string here is <em>derived</em> from the shipped
///     rules — the workflow expressions, <c>catalogue.yaml</c> and the loaded
///     <see cref="PolicyThresholds" /> — never authored alongside them, so prose cannot drift from the
///     rule it describes (Reservation 1: an untyped expression and a hand-written caption would
///     disagree silently). Retune a threshold or edit an entry rule and the wording follows.
/// </summary>
/// <param name="Subject">The subject described.</param>
/// <param name="Eligibility">The whole-college gate every student passes before any subject is rated.</param>
/// <param name="Green">What this subject's green tier requires, one bullet per criterion.</param>
/// <param name="Amber">What this subject's amber tier requires, one bullet per criterion.</param>
/// <param name="Downgrades">
///     The cross-subject relationships that can pull the subject down after it has been rated —
///     prerequisites, timetable clashes, own-time requirements, vetoes, restudy bars. Empty for a
///     subject the catalogue relates to nothing.
/// </param>
public sealed record SubjectCriteria(
	Subject Subject,
	EquatableArray<string> Eligibility,
	EquatableArray<string> Green,
	EquatableArray<string> Amber,
	EquatableArray<string> Downgrades)
{
	/// <summary>Prior qualifications that open or strengthen entry, and any bar on re-studying the subject.</summary>
	public EquatableArray<string> PriorQualifications { get; init; } = [];
}

/// <summary>
///     What a traffic light actually means to the student reading it. Fixed policy language, not
///     per-subject, so it is a static table rather than a member of every <see cref="SubjectCriteria" />.
/// </summary>
public readonly record struct RatingMeaning(Rating Rating, string Meaning)
{
	/// <summary>The three ratings in most-to-least favourable order, each with its student-facing gloss.</summary>
	public static EquatableArray<RatingMeaning> All { get; } = [
		new(Rating.Green, "You can definitely study this course."),
		new(Rating.Amber,
			"You are borderline. You may still be able to take this course, but check with a teacher first — you would need to work hard at it."),
		new(Rating.Red, "Sorry, this course is not right for you at this stage."),
	];
}
