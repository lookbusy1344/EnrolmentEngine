namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Cli;
using Domain;

/// <summary>
///     Criteria-narration tests. The narrator turns the shipped rule expressions into student-facing
///     English, so these pin the wording <em>and</em> the guard that matters most: every expression in
///     the shipped workflows must narrate, because a rule the narrator cannot read would otherwise be
///     silently omitted from a student's criteria list.
/// </summary>
public sealed class ExpressionNarratorTests
{
	private static PolicyThresholds Thresholds => Harness.Thresholds;

	[Fact]
	public void gcse_entry_clause_names_the_subject_and_the_resolved_grade()
	{
		var bullets = ExpressionNarrator.Narrate("facts.Gcse(\"maths\") >= facts.ExceptionalEntry", Thresholds);

		bullets.Should().ContainSingle()
			.Which.Should().Be("GCSE Maths at grade 8 or above.");
	}

	[Fact]
	public void each_top_level_and_becomes_its_own_bullet()
	{
		var bullets = ExpressionNarrator.Narrate(
			"facts.Gcse(\"chemistry\") >= facts.StandardEntry && facts.Predicted(\"chemistry\") >= ALevelGrade.D", Thresholds);

		bullets.Should().HaveCount(2);
		bullets[0].Should().Be("GCSE Chemistry at grade 5 or above.");
		bullets[1].Should().Contain("predicted").And.Contain("grade D");
	}

	[Fact]
	public void an_or_group_is_narrated_as_alternatives_in_one_bullet()
	{
		var bullets = ExpressionNarrator.Narrate(
			"(facts.Gcse(\"biology\") >= facts.PassGrade || facts.Gcse(\"psychology\") >= facts.PassGrade)", Thresholds);

		bullets.Should().ContainSingle()
			.Which.Should().Be("Either GCSE Biology at grade 4 or above, or GCSE Psychology at grade 4 or above.");
	}

	[Fact]
	public void entry_equivalent_clause_mentions_a_prior_qualification()
	{
		var bullets = ExpressionNarrator.Narrate("facts.HasEntryEquivalent(\"biology\")", Thresholds);

		bullets.Should().ContainSingle()
			.Which.Should().Contain("qualification");
	}

	[Fact]
	public void dfe_confidence_clause_is_narrated_as_a_percentage_of_students()
	{
		var bullets = ExpressionNarrator.Narrate(
			"facts.DfeProbabilityAtOrAbove(\"physics\", ALevelGrade.D) >= facts.MinDfeGreenProbabilityAtOrAbove", Thresholds);

		bullets.Should().ContainSingle();
		bullets[0].Should().Contain("60%").And.Contain("grade D");
	}

	[Fact]
	public void average_gcse_clause_resolves_its_threshold()
	{
		var bullets = ExpressionNarrator.Narrate("facts.Average >= facts.FurtherMathsAverageEntry", Thresholds);

		bullets.Should().ContainSingle()
			.Which.Should().Contain("average").And.Contain("7");
	}

	[Fact]
	public void the_eligibility_pass_count_clause_reads_its_local_parameter()
	{
		var bullets = ExpressionNarrator.Narrate(
			"passCount >= policy.MinPasses",
			Thresholds,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["passCount"] = "gcses.Count(g => g.Grade >= policy.PassGrade)" });

		bullets.Should().ContainSingle();
		bullets[0].Should().Contain("5").And.Contain("grade 4");
	}

	/// <summary>
	///     A threshold may itself depend on a fact about the student (Art's entry grade is age-gated).
	///     Both branches must be stated — reporting only one would tell half the students the wrong bar.
	/// </summary>
	[Fact]
	public void a_conditional_threshold_spells_out_both_branches()
	{
		var bullets = ExpressionNarrator.Narrate(
			"facts.Gcse(\"art\") >= (facts.Age >= facts.AdultAge ? facts.TopEntry : facts.StandardEntry)", Thresholds);

		bullets.Should().ContainSingle()
			.Which.Should().Be("GCSE Art at grade 7 or above if you are 19 or older, or grade 5 or above if not.");
	}

	[Fact]
	public void an_unrecognised_expression_fails_loudly_rather_than_being_omitted()
	{
		var narrate = () => ExpressionNarrator.Narrate("facts.SomethingNobodyTaughtMe() >= 3", Thresholds);

		narrate.Should().Throw<CriteriaNarrationException>()
			.WithMessage("*SomethingNobodyTaughtMe*");
	}

	/// <summary>
	///     The guard for the whole feature. A future YAML edit that introduces an expression shape the
	///     narrator cannot read must break the build here, not quietly drop a criterion from the advice a
	///     student reads.
	/// </summary>
	[Fact]
	public void every_expression_in_the_shipped_workflows_narrates()
	{
		var (workflows, _) = Harness.BuildFromShippedWorkflows();

		foreach (var workflow in workflows) {
			foreach (var rule in workflow.Rules) {
				if (string.IsNullOrWhiteSpace(rule.Expression) || rule.Expression.Trim() == "true") {
					continue;
				}

				var locals = (rule.LocalParams ?? [])
					.Where(static param => !string.IsNullOrWhiteSpace(param.Expression))
					.ToDictionary(static param => param.Name, static param => param.Expression!, StringComparer.Ordinal);

				var narrate = () => ExpressionNarrator.Narrate(rule.Expression!, Harness.Thresholds, locals);

				narrate.Should().NotThrow($"rule '{rule.RuleName}' in '{workflow.WorkflowName}' must be explainable to a student");
				ExpressionNarrator.Narrate(rule.Expression!, Harness.Thresholds, locals).Should().NotBeEmpty();
			}
		}
	}
}

/// <summary>
///     Criteria-composition tests: the explainer stitches the eligibility gate, the subject's own tier
///     rules and the catalogue relationships into one student-facing document.
/// </summary>
public sealed class CriteriaExplainerTests
{
	[Fact]
	public void the_eligibility_gate_is_described_for_every_subject()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.Art);

		criteria.Eligibility.Should().HaveCount(3);
		criteria.Eligibility.Should().ContainSingle(bullet => bullet.Contains("English Language", StringComparison.Ordinal));
		criteria.Eligibility.Should().ContainSingle(bullet => bullet.Contains("Maths", StringComparison.Ordinal));
		criteria.Eligibility.Should().ContainSingle(bullet =>
			bullet.Contains('5', StringComparison.Ordinal) && bullet.Contains("grade 4", StringComparison.Ordinal));
	}

	[Fact]
	public void green_demands_at_least_as_much_as_amber()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.Chemistry);

		criteria.Green.Should().NotBeEmpty();
		criteria.Amber.Should().NotBeEmpty();
		criteria.Green.Should().ContainSingle(bullet =>
			bullet.Contains("predicted", StringComparison.Ordinal) && bullet.Contains("grade D", StringComparison.Ordinal));
		criteria.Amber.Should().ContainSingle(bullet =>
			bullet.Contains("predicted", StringComparison.Ordinal) && bullet.Contains("grade E", StringComparison.Ordinal));
	}

	[Fact]
	public void an_own_time_requirement_and_its_veto_are_both_described()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.Music);

		criteria.Downgrades.Should().ContainSingle(bullet =>
			bullet.Contains("plays_", StringComparison.Ordinal) && bullet.Contains("amber", StringComparison.OrdinalIgnoreCase));
		criteria.Downgrades.Should().ContainSingle(bullet =>
			bullet.Contains("plays_trombone", StringComparison.Ordinal) && bullet.Contains("red", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void a_hard_prerequisite_names_the_subject_it_depends_on()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.FurtherMaths);

		criteria.Downgrades.Should().ContainSingle(bullet =>
			bullet.Contains("Maths", StringComparison.Ordinal) && bullet.Contains("red", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void an_advisory_prerequisite_is_distinguished_from_a_hard_one()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.Economics);

		criteria.Downgrades.Should().ContainSingle(bullet =>
			bullet.Contains("Maths", StringComparison.Ordinal) && bullet.Contains("amber", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void a_timetable_clash_names_the_other_subject_and_its_severity()
	{
		var french = Harness.ShippedEngine().Describe(Subject.French);
		var history = Harness.ShippedEngine().Describe(Subject.History);

		french.Downgrades.Should().ContainSingle(bullet =>
			bullet.Contains("German", StringComparison.Ordinal) && bullet.Contains("red", StringComparison.OrdinalIgnoreCase));
		history.Downgrades.Should().ContainSingle(bullet =>
			bullet.Contains("Art", StringComparison.Ordinal) && bullet.Contains("amber", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void prior_qualifications_cover_both_the_entry_equivalent_and_the_restudy_bar()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.Biology);

		criteria.PriorQualifications.Should().ContainSingle(bullet =>
			bullet.Contains("Applied Science", StringComparison.Ordinal));
		criteria.PriorQualifications.Should().ContainSingle(bullet =>
			bullet.Contains("already", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Qualification names are acronyms, so the indefinite article follows the spoken sound.</summary>
	[Fact]
	public void qualification_names_read_as_english_not_as_enum_tokens()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.Biology);

		criteria.PriorQualifications.Should().ContainSingle(bullet =>
			bullet.StartsWith("A BTEC Diploma in Applied Science", StringComparison.Ordinal));
		criteria.PriorQualifications.Should().ContainSingle(bullet =>
			bullet.Contains("an A-level in Biology", StringComparison.Ordinal));
	}

	[Fact]
	public void a_subject_with_no_relationships_has_no_downgrade_bullets()
	{
		var criteria = Harness.ShippedEngine().Describe(Subject.Sociology);

		criteria.Downgrades.Should().BeEmpty();
		criteria.PriorQualifications.Should().BeEmpty();
	}

	/// <summary>Every catalogued subject must be describable — a gap here is a subject a student cannot be advised on.</summary>
	[Fact]
	public void every_catalogued_subject_can_be_described()
	{
		var engine = Harness.ShippedEngine();

		foreach (var subject in Harness.Catalogue.Subjects) {
			var criteria = engine.Describe(subject);

			criteria.Subject.Should().Be(subject);
			criteria.Green.Should().NotBeEmpty($"{subject} must explain how to reach green");
			criteria.Amber.Should().NotBeEmpty($"{subject} must explain how to reach amber");
		}
	}

	[Fact]
	public void describing_a_subject_outside_the_catalogue_is_rejected()
	{
		var describe = () => Harness.ShippedEngine().Describe(new("underwater_basket_weaving"));

		describe.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void the_three_ratings_are_explained_in_plain_english()
	{
		RatingMeaning.All.Should().HaveCount(3);
		RatingMeaning.All.Select(static meaning => meaning.Rating)
			.Should().Equal(Rating.Green, Rating.Amber, Rating.Red);
		RatingMeaning.All.Should().OnlyContain(meaning => meaning.Meaning.EndsWith('.'));
	}
}

/// <summary>
///     <c>--criteria</c> end-to-end through the CLI over the shipped rules and data.
/// </summary>
public sealed class CriteriaCliTests
{
	private static (int Exit, string Stdout, string Stderr) Run(params string[] args)
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var exit = CliRunner.Run(args, stdout, stderr, () => Harness.WorkflowsDir, () => Harness.DataDir);
		return (exit, stdout.ToString(), stderr.ToString());
	}

	[Fact]
	public void criteria_prints_every_section_for_a_subject_that_has_one()
	{
		var (exit, stdout, _) = Run("--criteria", "music");

		exit.Should().Be(CliRunner.ExitOk);
		stdout.Should().Contain("# What you need for A-level Music");
		stdout.Should().Contain("## What the colours mean");
		stdout.Should().Contain("## Everyone needs these first");
		stdout.Should().Contain("## To get a green light in Music");
		stdout.Should().Contain("## To get at least an amber light in Music");
		stdout.Should().Contain("## Other things that can affect Music");
	}

	[Fact]
	public void the_three_colours_are_explained_before_the_criteria()
	{
		var (_, stdout, _) = Run("--criteria", "history");

		stdout.Should().Contain("You can definitely study this course");
		stdout.Should().Contain("You are borderline");
		stdout.Should().Contain("not right for you at this stage");
		stdout.IndexOf("What the colours mean", StringComparison.Ordinal)
			.Should().BeLessThan(stdout.IndexOf("Everyone needs these first", StringComparison.Ordinal));
	}

	/// <summary>A section with nothing to say is omitted rather than printed empty.</summary>
	[Fact]
	public void sections_with_no_content_are_omitted()
	{
		var (_, stdout, _) = Run("--criteria", "sociology");

		stdout.Should().NotContain("Other things that can affect");
		stdout.Should().NotContain("If you already have other qualifications");
	}

	[Fact]
	public void an_unknown_subject_is_an_input_error_that_lists_what_is_available()
	{
		var (exit, stdout, stderr) = Run("--criteria", "underwater_basket_weaving");

		exit.Should().Be(CliRunner.ExitInput);
		stdout.Should().BeEmpty();
		stderr.Should().Contain("not a subject offered");
		stderr.Should().Contain("maths");
	}

	[Fact]
	public void criteria_requires_a_subject_argument()
	{
		var (exit, _, stderr) = Run("--criteria");

		exit.Should().Be(CliRunner.ExitUsage);
		stderr.Should().Contain("--criteria");
	}

	/// <summary>
	///     The narrated thresholds must be the loaded ones, not constants baked into the prose — this is the
	///     whole reason the English is derived rather than authored.
	/// </summary>
	[Fact]
	public void narrated_grades_come_from_the_loaded_thresholds()
	{
		var (_, stdout, _) = Run("--criteria", "chemistry");

		stdout.Should().Contain($"grade {Harness.Thresholds.StandardEntry} or above");
		stdout.Should().ContainEquivalentOf($"at least {Harness.Thresholds.MinPasses} GCSEs");
	}
}
