namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     The three accessible A-levels — psychology, sociology and media studies — are policy-tuned so a
///     student at the borderline eligibility minimum (five GCSEs at <c>pass_grade</c>) has a green
///     programme to enrol on rather than an all-red sheet. Reaching green there needs both halves of the
///     rating to clear at a 4.0 average: the entry gate (<c>facts.PassGrade</c> rather than the shared
///     <c>standard_entry</c>, plus <c>accessible_average_entry</c> for sociology in place of the shared
///     <c>humanities_average_entry</c>) and the per-subject regression intercept, which must predict at
///     least a D. Both are scoped to these three subjects alone; the sibling tests below pin that
///     scoping, since the cheapest way to "fix" this student would be to lower a shared threshold and
///     silently open every subject with it.
/// </summary>
public sealed class AccessibleSubjectsTests
{
	private static readonly Subject[] Accessible = [Subject.Psychology, Subject.Sociology, Subject.MediaStudies];

	// Borderline-minimum students: exactly min_passes GCSEs, every one at pass_grade, so the average sits
	// on pass_grade (4.0) and every entry term is tested at its floor. English language and maths appear in
	// every set because the eligibility gate names them; the rest deliberately vary, because the policy is
	// unconditional — *any* five passes at pass_grade must yield the three greens. A single fixture cannot
	// establish that: it only shows the tier opens for whichever GCSEs that fixture happens to name, and an
	// entry term naming a subject outside it (biology, once) stays invisible.
	public static TheoryData<string, string[]> BorderlineMinimumSets() => new() {
		{ "with biology and humanities", ["maths", "english_language", "biology", "english_literature", "history"] },
		{ "no biology", ["english_language", "maths", "french", "physics", "psychology"] },
		{ "no science at all", ["english_language", "maths", "art", "music", "physical_education"] },
		{ "sciences only", ["english_language", "maths", "physics", "chemistry", "computer_studies"] },
	};

	private static (string, int)[] AtPassGrade(string[] subjects) =>
		[.. subjects.Select(s => (s, Harness.Thresholds.PassGrade))];

	private static IReadOnlyList<SubjectRating> Rate(params (string Subject, int Grade)[] gcses)
	{
		var student = new StudentInput("S-BORDERLINE", gcses.ToDictionary(g => g.Subject, g => g.Grade), []);
		var evaluator = Harness.ShippedEvaluator();
		return evaluator.EvaluateRatings(Harness.Predict(student), student.ToGcseResults());
	}

	private static Rating Of(IEnumerable<SubjectRating> ratings, Subject subject) =>
		ratings.Single(r => r.Subject == subject).Rating;

	[Theory]
	[MemberData(nameof(BorderlineMinimumSets))]
	public void the_borderline_minimum_student_is_eligible(string description, string[] subjects)
	{
		var student = new StudentInput(
			"S-BORDERLINE",
			AtPassGrade(subjects).ToDictionary(g => g.Item1, g => g.Item2),
			[]);

		var result = Harness.ShippedEngine().Evaluate(student, Harness.AsOf);

		result.Eligible.Should().BeTrue($"five passes at pass_grade ({description}) clears the gate");
	}

	[Theory]
	[MemberData(nameof(BorderlineMinimumSets))]
	public void the_three_accessible_subjects_are_green_at_the_borderline_minimum(string description, string[] subjects)
	{
		var ratings = Rate(AtPassGrade(subjects));

		foreach (var subject in Accessible) {
			Of(ratings, subject).Should()
				.Be(Rating.Green, $"{subject} must be enrollable on any five passes at pass_grade ({description})");
		}
	}

	[Theory]
	[MemberData(nameof(BorderlineMinimumSets))]
	public void the_borderline_minimum_student_has_a_green_programme_through_the_full_engine(
		string description, string[] subjects)
	{
		var student = new StudentInput(
			"S-BORDERLINE",
			AtPassGrade(subjects).ToDictionary(g => g.Item1, g => g.Item2),
			[]);

		var result = Harness.ShippedEngine().Evaluate(student, Harness.AsOf);

		result.Recommendations.Where(r => r.Rating == Rating.Green).Select(r => r.Subject)
			.Should().BeEquivalentTo(Accessible, $"the accessible tier is the whole programme ({description})");
	}

	// The scoping guard: lowering the shared humanities_average_entry or standard_entry would turn these
	// green too, so their staying red is what proves the change was scoped to the three subjects.
	[Theory]
	[InlineData("history")] // shares humanities_average_entry with sociology
	[InlineData("geography")] // shares humanities_average_entry and standard_entry
	[InlineData("law")]
	[InlineData("politics")]
	[InlineData("religious_studies")]
	[InlineData("drama")] // shares standard_entry with media_studies
	[InlineData("business_studies")]
	public void other_subjects_stay_red_at_the_borderline_minimum(string subject)
	{
		var ratings = Rate(AtPassGrade(["maths", "english_language", "biology", "english_literature", "history"]));

		Of(ratings, new(subject)).Should().Be(Rating.Red);
	}

	[Fact]
	public void psychology_and_sociology_are_recognised_gcse_subjects() => GcseSubjects.Known.Should().Contain(["psychology", "sociology"]);

	// Driven through TryEvaluate rather than Evaluate: only the validating path checks GCSE keys against
	// GcseSubjects.Known, so the unchecked path would pass this even with the vocabulary unchanged.
	[Fact]
	public void psychology_and_sociology_gcses_are_accepted_by_the_input_validator()
	{
		var student = new StudentInput(
			"S-GCSE",
			new Dictionary<string, int> {
				["maths"] = 4,
				["english_language"] = 4,
				["biology"] = 4,
				["psychology"] = 4,
				["sociology"] = 4,
			},
			[]) { DateOfBirth = new(2009, 9, 1) };

		var evaluation = Harness.ShippedEngine().TryEvaluate(student, Harness.AsOf);

		evaluation.Validation.Errors.Should().BeEmpty();
		evaluation.Value!.Eligible.Should().BeTrue();
	}
}
