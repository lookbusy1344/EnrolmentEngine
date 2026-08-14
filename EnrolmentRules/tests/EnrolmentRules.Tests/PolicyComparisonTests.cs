namespace EnrolmentRules.Tests;

using System.Text.RegularExpressions;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Elite auxiliary policy plan, step 2.3 — <see cref="EnrolmentPolicyRegistry.Compare" />: the
///     non-destructive comparison of one shared <see cref="StudentInput" /> against one registered policy.
///     Builds a two-entry registry (Standard, plus a second "reduced" policy over a catalogue missing Art)
///     so a chosen Art A-level can be classified NotOffered without needing the real Elite assets (step 3.1).
/// </summary>
public sealed partial class PolicyComparisonTests
{
	// The "reduced" policy is a temp copy of the shipped layout with Art dropped from both the catalogue
	// and subject-ratings.yaml (a catalogue subject without a matching workflow rule — or vice versa —
	// fails startup lint, so both sides of the fixture must agree). This stands in for Elite's smaller
	// catalogue (step 3.1) without needing the real Elite assets yet.
	private static EnrolmentPolicyRegistry BuildRegistry()
	{
		var standard = new EnrolmentPolicyDefinition(new("standard"), "Standard", new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir));

		var reducedWorkflows = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "reduced-workflows-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(reducedWorkflows);
		File.Copy(Path.Combine(Harness.WorkflowsDir, "eligibility.yaml"), Path.Combine(reducedWorkflows, "eligibility.yaml"));
		File.Copy(Path.Combine(Harness.WorkflowsDir, WorkflowStore.SchemaFileName), Path.Combine(reducedWorkflows, WorkflowStore.SchemaFileName));
		var subjectRatings = File.ReadAllText(Path.Combine(Harness.WorkflowsDir, "subject-ratings.yaml"));
		var withoutArt = ArtRatingRulesRegex().Replace(subjectRatings, string.Empty);
		withoutArt.Should().NotBe(subjectRatings, "the art:green/amber/red block must actually be stripped");
		File.WriteAllText(Path.Combine(reducedWorkflows, "subject-ratings.yaml"), withoutArt);

		var reducedData = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "reduced-data-" + Guid.NewGuid().ToString("N"));
		CopyTree(Harness.DataDir, reducedData);
		File.WriteAllText(Path.Combine(reducedData, CatalogueStore.CatalogueFileName), CatalogueTests.AllSubjects(string.Empty, "art"));

		var reduced = new EnrolmentPolicyDefinition(
			new("reduced"), "Reduced", new DirectoryDataSource(reducedWorkflows, reducedData));

		return new([standard, reduced], new("standard"), static () => Harness.AsOf);
	}

	private static void CopyTree(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			var relative = Path.GetRelativePath(source, file);
			var target = Path.Combine(destination, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, true);
		}
	}

	[GeneratedRegex(
		@"  - RuleName: 'art:green'.*?  - RuleName: 'art:red'.*?\n      true\n",
		RegexOptions.Singleline)]
	private static partial Regex ArtRatingRulesRegex();

	private static StudentInput AllEights(params Subject[] chosen) =>
		new(
			"S-COMPARE",
			new Dictionary<string, int> {
				["maths"] = 8,
				["english_language"] = 8,
				["english_literature"] = 8,
				["physics"] = 8,
				["chemistry"] = 8,
				["biology"] = 8,
				["french"] = 8,
				["german"] = 8,
				["physical_education"] = 8,
				["computer_studies"] = 8,
				["history"] = 8,
				["music"] = 8,
				["art"] = 8,
			},
			[]) {
			ChosenALevels = [.. chosen],
			DateOfBirth = new(2009, 9, 1),
		};

	[Fact]
	public void a_green_chosen_subject_is_available()
	{
		var registry = BuildRegistry();

		var comparison = registry.Compare(new("standard"), AllEights(Subject.Art));

		comparison.Validation.IsValid.Should().BeTrue();
		comparison.Value!.ChoiceStatuses.Should().ContainSingle(s => s.Subject == Subject.Art && s.Status == ChoiceStatus.Available);
	}

	[Fact]
	public void a_red_chosen_subject_is_unavailable_with_its_reason()
	{
		var registry = BuildRegistry();
		// French <-> German is a hard clash: committing to both makes one red under Standard.
		var student = AllEights(Subject.French, Subject.German);

		var comparison = registry.Compare(new("standard"), student);

		comparison.Validation.IsValid.Should().BeTrue();
		// Both sides of a mutual chosen-subject clash go red (neither has the "unchosen" advantage).
		comparison.Value!.ChoiceStatuses.Should().OnlyContain(s => s.Status == ChoiceStatus.Unavailable && s.Reason != null);
	}

	[Fact]
	public void a_chosen_subject_absent_from_the_selected_catalogue_is_not_offered_not_a_validation_error()
	{
		var registry = BuildRegistry();
		var student = AllEights(Subject.Art, Subject.Music);

		var comparison = registry.Compare(new("reduced"), student);

		comparison.Validation.IsValid.Should().BeTrue();
		comparison.Value!.ChoiceStatuses.Should().ContainSingle(s => s.Subject == Subject.Art && s.Status == ChoiceStatus.NotOffered);
		comparison.Value.ChoiceStatuses.Should().ContainSingle(s => s.Subject == Subject.Music && s.Status == ChoiceStatus.Available);
	}

	[Fact]
	public void the_same_basket_can_produce_different_statuses_under_two_policies()
	{
		var registry = BuildRegistry();
		var student = AllEights(Subject.Art);

		var underStandard = registry.Compare(new("standard"), student);
		var underReduced = registry.Compare(new("reduced"), student);

		underStandard.Value!.ChoiceStatuses.Single().Status.Should().Be(ChoiceStatus.Available);
		underReduced.Value!.ChoiceStatuses.Single().Status.Should().Be(ChoiceStatus.NotOffered);
	}

	[Fact]
	public void compare_never_mutates_the_input_students_chosen_a_levels()
	{
		var registry = BuildRegistry();
		var student = AllEights(Subject.Art);

		_ = registry.Compare(new("reduced"), student);

		student.ChosenALevels.Should().Equal(Subject.Art);
	}

	[Fact]
	public void malformed_facts_return_validation_errors_without_a_value()
	{
		var registry = BuildRegistry();
		var student = new StudentInput(string.Empty, new Dictionary<string, int> {
			["not_a_real_gcse"] = 8,
		}, []);

		var comparison = registry.Compare(new("standard"), student);

		comparison.Validation.IsValid.Should().BeFalse();
		comparison.Value.Should().BeNull();
		comparison.Validation.Errors.Should().Contain(e => e.Contains("student id", StringComparison.Ordinal));
	}

	[Fact]
	public void a_duplicate_chosen_subject_is_a_structural_error_even_though_not_offered_is_not()
	{
		var registry = BuildRegistry();
		var student = AllEights(Subject.Art, Subject.Art);

		var comparison = registry.Compare(new("reduced"), student);

		comparison.Validation.IsValid.Should().BeFalse();
		comparison.Validation.Errors.Should().Contain(e => e.Contains("duplicates", StringComparison.Ordinal));
	}

	[Fact]
	public void the_result_carries_the_selected_policys_effective_min_and_max_bounds()
	{
		var registry = BuildRegistry();

		var comparison = registry.Compare(new("standard"), AllEights());

		comparison.Value!.MinChosenALevels.Should().Be(Harness.Thresholds.MinChosenALevels);
		comparison.Value.MaxChosenALevels.Should().Be(Harness.Thresholds.HighAttainmentMaxChosenALevels);
	}

	[Fact]
	public void an_unknown_policy_id_throws()
	{
		var registry = BuildRegistry();

		var act = () => registry.Compare(new("elite"), AllEights());

		act.Should().Throw<UnknownEnrolmentPolicyException>();
	}
}
