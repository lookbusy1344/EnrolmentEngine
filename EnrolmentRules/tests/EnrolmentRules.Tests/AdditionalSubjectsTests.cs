namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;

/// <summary>
///     The twelve additional A-levels (economics … design technology) added as a pure data exercise —
///     catalogue entries + green/amber/red workflow triples + real DfE transition rows, no compiled
///     change. Each expectation is hand-derived from the entry gate (§1.4 thresholds), the
///     <see cref="PredictionModel" /> tier and the DfE confidence floors, then driven
///     <em>
///         through the
///         engine
///     </em>
///     ; the cross-subject economics/business clash is exercised through the full
///     <see cref="EnrolmentEngine" /> so the constraint pass actually runs.
/// </summary>
public sealed class AdditionalSubjectsTests
{
	private static readonly Subject[] Added = [
		Subject.Economics, Subject.Geography, Subject.Psychology, Subject.Sociology,
		Subject.BusinessStudies, Subject.Politics, Subject.ReligiousStudies, Subject.Drama,
		Subject.MediaStudies, Subject.Law, Subject.Spanish, Subject.DesignTechnology,
	];

	// A full set of GCSEs at one uniform grade, so the average equals that grade.
	private static (string, int)[] Uniform(int grade) => [
		("maths", grade), ("english_language", grade), ("physics", grade),
		("chemistry", grade), ("biology", grade), ("english_literature", grade),
		("french", grade), ("german", grade), ("physical_education", grade),
		("computer_studies", grade), ("history", grade), ("music", grade), ("art", grade),
	];

	private static IReadOnlyList<SubjectRating> Rate(params (string Subject, int Grade)[] gcses)
	{
		var student = new StudentInput("S-TEST", gcses.ToDictionary(g => g.Subject, g => g.Grade), []);
		var evaluator = Harness.ShippedEvaluator();
		return evaluator.EvaluateRatings(Harness.Predict(student), student.ToGcseResults());
	}

	private static Rating Of(IEnumerable<SubjectRating> ratings, Subject subject) =>
		ratings.Single(r => r.Subject == subject).Rating;

	[Fact]
	public void all_twelve_added_subjects_are_present_in_the_catalogue()
	{
		var ratings = Rate(Uniform(6));

		Added.Should().BeSubsetOf(Catalogue.Subjects);
		Added.Should().OnlyContain(subject => ratings.Any(r => r.Subject == subject));
	}

	[Fact]
	public void a_top_student_is_green_in_every_added_subject()
	{
		// Average 9.0 ⇒ entry gates met and predicted grades clear the green (B) tier; the suppressed noisy
		// >=9 DfE cells fall back to the well-sampled 8-to-9 band, which clears the green confidence floor.
		var ratings = Rate(Uniform(9));

		Added.Should().OnlyContain(subject => Of(ratings, subject) == Rating.Green);
	}

	[Fact]
	public void a_weak_student_is_red_in_every_added_subject()
	{
		// Average 4.0 ⇒ every entry gate fails (supporting GCSEs and the average are all too low).
		var ratings = Rate(Uniform(4));

		Added.Should().OnlyContain(subject => Of(ratings, subject) == Rating.Red);
	}

	[Fact]
	public void economics_entry_requires_maths_at_strong_entry()
	{
		// English at top entry throughout, only Maths moves across the strong-entry boundary.
		var below = Rate(
			("maths", Harness.Thresholds.StrongEntry - 1), ("english_language", Harness.Thresholds.TopEntry));
		Of(below, Subject.Economics).Should().Be(Rating.Red);

		var met = Rate(
			("maths", Harness.Thresholds.TopEntry), ("english_language", Harness.Thresholds.TopEntry));
		Of(met, Subject.Economics).Should().NotBe(Rating.Red);
	}

	[Fact]
	public void geography_gates_on_the_humanities_average_not_a_geography_gcse()
	{
		// There is no geography GCSE key, so entry is the humanities average plus English. One full set just
		// below the humanities-average entry fails; lifting the average over the threshold opens entry.
		var below = Rate(Uniform((int)Harness.Thresholds.HumanitiesAverageEntry - 1));
		Of(below, Subject.Geography).Should().Be(Rating.Red);

		var met = Rate(Uniform((int)Harness.Thresholds.HumanitiesAverageEntry + 2));
		Of(met, Subject.Geography).Should().NotBe(Rating.Red);
	}

	[Fact]
	public void spanish_requires_a_modern_language_gcse_at_strong_entry()
	{
		// No spanish GCSE key exists, so a French or German GCSE at strong entry stands proxy for prior
		// language study. Strong English alone is not enough.
		var noLanguage = Rate(
			("english_language", 9), ("maths", 9), ("history", 9));
		Of(noLanguage, Subject.Spanish).Should().Be(Rating.Red);

		var withFrench = Rate(
			("french", Harness.Thresholds.StrongEntry), ("english_language", 9), ("maths", 9), ("history", 9));
		Of(withFrench, Subject.Spanish).Should().NotBe(Rating.Red);
	}

	[Fact]
	public void economics_and_business_studies_are_a_soft_timetable_clash()
	{
		// Both green at the top; the amber mutual exclusion demotes the lower-UCAS-weight subject
		// (business studies, 34) and leaves the winner (economics, 45) green.
		var engine = Harness.ShippedEngine();
		var student = new StudentInput("S-CLASH", Uniform(9).ToDictionary(g => g.Item1, g => g.Item2), []);

		var result = engine.Evaluate(student);
		var economics = result.Recommendations.Single(r => r.Subject == Subject.Economics);
		var business = result.Recommendations.Single(r => r.Subject == Subject.BusinessStudies);

		economics.Rating.Should().Be(Rating.Green);
		business.Rating.Should().Be(Rating.Amber);
		business.Reason.Should().Contain("Mutual exclusion").And.Contain(EnumNames.NameOf(Subject.Economics));
	}
}
