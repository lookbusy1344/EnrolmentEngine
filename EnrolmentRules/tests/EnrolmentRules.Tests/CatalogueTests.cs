namespace EnrolmentRules.Tests;

using System.Text;
using Domain;
using Engine;
using FluentAssertions;

/// <summary>
///     The subject catalogue is now rules-as-data: <c>data/catalogue.yaml</c>, loaded and schema-validated
///     at startup (the cross-subject constraint policy, §1.5–1.6). These tests drive the loader and its
///     guards directly, inspecting the returned <see cref="CatalogueData" /> instance. The catalogue holder
///     exposes only an immutable shipped <see cref="Catalogue.Default" />, so there is no active table to
///     mutate and the suite needs no serialised process-global phase.
/// </summary>
public sealed class CatalogueTests
{
	private static string DataDir => Path.Combine(Harness.RepoRoot, "data");

	[Fact]
	public void shipped_catalogue_loads_and_carries_the_expected_policy()
	{
		var data = CatalogueStore.LoadAndValidate(DataDir);

		var furtherMaths = data.Meta(Subject.FurtherMaths).Prerequisites.Should().ContainSingle().Which;
		furtherMaths.AnyOf.Should().Equal(Subject.Maths);
		furtherMaths.Severity.Should().Be(Rating.Red);
		data.Meta(Subject.Maths).UcasWeight.Should().Be(50);
		data.Meta(Subject.FurtherMaths).UcasWeight.Should().Be(56);

		data.Meta(Subject.French).Exclusions.Should().ContainSingle()
			.Which.Should().Be(new SubjectExclusion(Subject.German, Rating.Red));
		data.Meta(Subject.History).Exclusions.Should().ContainSingle()
			.Which.Should().Be(new SubjectExclusion(Subject.Art, Rating.Amber));

		data.Meta(Subject.Music).RequiredActivities.Should().Equal("plays_");
		data.Meta(Subject.Music).BlockingActivities.Should().Equal("plays_trombone");
		data.Meta(Subject.Maths).Regression.Should().Be(new PredictionModel.Coefficients(0.80, -1.00));
		data.Meta(Subject.FurtherMaths).Regression.Should().Be(new PredictionModel.Coefficients(1.00, -2.00));
		data.Meta(Subject.Biology).EntryEquivalents.Should().ContainSingle()
			.Which.Should().Be(new EntryEquivalent("applied_science", QualificationType.BtecDiploma, "distinction"));
		data.Meta(Subject.Biology).RestudyBar.Should().NotBeNull();
		data.Meta(Subject.Biology).RestudyBar!.Value.Types.Should().Equal(QualificationType.ALevel);
		data.Meta(Subject.Biology).RestudyBar!.Value.Severity.Should().Be(Rating.Red);
	}

	[Fact]
	public void shipped_catalogue_exclusion_pairs_are_deduplicated_lower_enum_first()
	{
		var data = CatalogueStore.LoadAndValidate(DataDir);

		// Lower catalogue-order value first: French < German; History < Art; Economics < Business Studies.
		data.ExclusionPairs.Should().BeEquivalentTo([
			new(Subject.French, Subject.German, Rating.Red), new(Subject.History, Subject.Art, Rating.Amber),
			new ExclusionPair(Subject.Economics, Subject.BusinessStudies, Rating.Amber),
		]);
	}

	[Fact]
	public void exclusion_pairs_use_a_named_public_type()
	{
		typeof(CatalogueData).GetProperty(nameof(CatalogueData.ExclusionPairs))!.PropertyType.GetGenericArguments()[0]
			.Should().Be<ExclusionPair>();
	}

	[Fact]
	public void every_subject_has_an_entry_in_the_shipped_catalogue()
	{
		var data = CatalogueStore.LoadAndValidate(DataDir);

		// Meta is total over Subjects only because the coverage invariant passed at load.
		Catalogue.Subjects.Should().OnlyContain(subject => data.Meta(subject).UcasWeight > 0);
	}

	[Fact]
	public void a_catalogue_can_introduce_a_subject_not_predeclared_in_code()
	{
		const string yaml = """
							subjects:
							  - subject: maths
							    ucas_weight: 50
							    regression: { slope: 0.80, intercept: -1.00 }
							  - subject: drama
							    ucas_weight: 42
							    regression: { slope: 0.75, intercept: -0.50 }
							    prerequisites:
							      - any_of: [maths]
							""";

		var data = Catalogue.Load(yaml);

		data.Subjects.Should().Equal(Subject.Maths, new Subject("drama"));
		data.Meta(new("drama")).UcasWeight.Should().Be(42);
		data.Meta(new("drama")).Prerequisites.Should().ContainSingle()
			.Which.AnyOf.Should().Equal(Subject.Maths);
	}

	[Fact]
	public void a_catalogue_can_load_from_a_memory_stream()
	{
		var yaml = File.ReadAllText(Path.Combine(DataDir, CatalogueStore.CatalogueFileName));

		using var catalogueStream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
		using var schemaStream = File.OpenRead(Path.Combine(DataDir, CatalogueStore.SchemaFileName));

		var data = CatalogueStore.LoadAndValidate(catalogueStream, schemaStream);

		data.Subjects.Should().Equal(Harness.Catalogue.Subjects);
	}

	[Fact]
	public void catalogue_build_uses_the_injected_scale_for_entry_equivalent_validation()
	{
		const string yaml = """
							subjects:
							  - subject: biology
							    ucas_weight: 44
							    regression: { slope: 0.90, intercept: -2.30 }
							    entry_equivalents:
							      - { subject: applied_science, type: btec_diploma, min_grade: platinum }
							""";

		var node = YamlConverter.ToJsonNode(yaml);
		var validatingScale = new QualificationScale([
			new(QualificationType.BtecDiploma, "pass", 0, ALevelGrade.C),
			new(QualificationType.BtecDiploma, "merit", 1, ALevelGrade.B),
			new(QualificationType.BtecDiploma, "platinum", 2, ALevelGrade.A),
		]);
		var incompleteScale = new QualificationScale([
			new(QualificationType.BtecDiploma, "pass", 0, ALevelGrade.C),
			new(QualificationType.BtecDiploma, "merit", 1, ALevelGrade.B),
		]);

		var data = Catalogue.Build(node, validatingScale);

		data.Meta(Subject.Biology).EntryEquivalents.Should().ContainSingle()
			.Which.Should().Be(new EntryEquivalent("applied_science", QualificationType.BtecDiploma, "platinum"));

		var act = () => Catalogue.Build(node, incompleteScale);

		act.Should().Throw<InvalidDataException>().WithMessage("*unknown qualification*platinum*");
	}

	[Fact]
	public void a_catalogue_with_one_subject_is_valid()
	{
		const string yaml = """
							subjects:
							  - subject: maths
							    ucas_weight: 50
							    regression: { slope: 0.80, intercept: -1.00 }
							""";

		var data = Catalogue.Load(yaml);

		data.Subjects.Should().Equal(Subject.Maths);
	}

	[Fact]
	public void an_exclusion_to_an_undefined_subject_is_rejected()
	{
		const string yaml = """
							subjects:
							  - subject: maths
							    ucas_weight: 50
							    regression: { slope: 0.80, intercept: -1.00 }
							    exclusions:
							      - { other: drama, severity: red }
							""";

		var act = () => Catalogue.Load(yaml);

		act.Should().Throw<InvalidDataException>().WithMessage("*undefined subject*drama*");
	}

	[Fact]
	public void a_prerequisite_to_an_undefined_subject_is_rejected_even_when_it_is_the_default_subject()
	{
		// The unknown-prerequisite guard must reject any subject absent from the table — including
		// default(Subject), which a `!= default` sentinel check would silently let through.
		var entries = new Dictionary<Subject, SubjectMeta> {
			[Subject.Maths] = new(
				50, new(0.80, -1.00), [], [], [],
				[new([default], Rating.Red)]),
		};

		var act = () => new CatalogueData(entries);

		act.Should().Throw<InvalidDataException>().WithMessage("*undefined subject*prerequisites*maths*");
	}

	[Fact]
	public void prerequisites_parse_alternatives_and_default_severity_to_red()
	{
		var yaml = AllSubjects("""
							     - subject: further_maths
							       ucas_weight: 56
							       regression: { slope: 1.00, intercept: -2.00 }
							       prerequisites:
							         - any_of: [maths, physics]
							         - any_of: [chemistry]
							           severity: amber
							   """, "further_maths");

		var prereqs = Catalogue.Load(yaml).Meta(Subject.FurtherMaths).Prerequisites;

		prereqs.Should().HaveCount(2);
		prereqs[0].AnyOf.Should().Equal(Subject.Maths, Subject.Physics);
		prereqs[0].Severity.Should().Be(Rating.Red); // omitted ⇒ hard requirement
		prereqs[0].Requires.Should().Be(PrerequisiteSatisfaction.Qualifying); // omitted ⇒ qualifying
		prereqs[1].AnyOf.Should().Equal(Subject.Chemistry);
		prereqs[1].Severity.Should().Be(Rating.Amber);
	}

	[Fact]
	public void a_prerequisite_can_require_a_committed_choice()
	{
		var yaml = AllSubjects("""
							     - subject: further_maths
							       ucas_weight: 56
							       regression: { slope: 1.00, intercept: -2.00 }
							       prerequisites:
							         - any_of: [maths]
							           requires: chosen
							   """, "further_maths");

		var group = Catalogue.Load(yaml).Meta(Subject.FurtherMaths).Prerequisites.Should().ContainSingle().Which;

		group.Requires.Should().Be(PrerequisiteSatisfaction.Chosen);
	}

	[Fact]
	public void a_prerequisite_group_with_no_alternatives_is_rejected()
	{
		var yaml = AllSubjects("""
							     - subject: further_maths
							       ucas_weight: 56
							       regression: { slope: 1.00, intercept: -2.00 }
							       prerequisites:
							         - any_of: []
							   """, "further_maths");

		var act = () => Catalogue.Load(yaml);

		act.Should().Throw<InvalidDataException>().WithMessage("*prerequisite group with no alternatives*");
	}

	[Fact]
	public void a_duplicate_subject_entry_is_rejected()
	{
		var yaml = AllSubjects("""
							     - subject: maths
							       ucas_weight: 99
							       regression: { slope: 0.80, intercept: -1.00 }
							   """, null);

		var act = () => Catalogue.Load(yaml);

		act.Should().Throw<InvalidDataException>().WithMessage("*duplicate entry*maths*");
	}

	[Fact]
	public void an_asymmetric_exclusion_fails_the_symmetry_invariant()
	{
		// French excludes German, but German does not name French back: undirected clashes must be mirrored.
		var yaml = AllSubjects("""
							     - subject: french
							       ucas_weight: 39
							       regression: { slope: 0.85, intercept: -1.85 }
							       exclusions:
							         - { other: german, severity: red }
							   """, "french");

		var act = () => Catalogue.Load(yaml);

		act.Should().Throw<InvalidDataException>().WithMessage("*not declared symmetrically*");
	}

	[Fact]
	public void schema_validation_rejects_an_unknown_severity()
	{
		var dir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "catalogue-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		File.Copy(Path.Combine(DataDir, CatalogueStore.SchemaFileName), Path.Combine(dir, CatalogueStore.SchemaFileName));
		File.WriteAllText(Path.Combine(dir, CatalogueStore.CatalogueFileName), """
																			   subjects:
																			     - subject: maths
																			       ucas_weight: 50
																			       regression: { slope: 0.80, intercept: -1.00 }
																			     - subject: french
																			       ucas_weight: 39
																			       regression: { slope: 0.85, intercept: -1.85 }
																			       exclusions:
																			         - { other: german, severity: purple }
																			   """);

		var act = () => CatalogueStore.LoadAndValidate(dir);

		act.Should().Throw<CatalogueException>().WithMessage("*failed schema validation*");
	}

	[Fact]
	public void an_entry_equivalent_must_resolve_its_minimum_grade_in_the_scale()
	{
		const string yaml = """
							subjects:
							  - subject: biology
							    ucas_weight: 44
							    regression: { slope: 0.90, intercept: -2.30 }
							    entry_equivalents:
							      - { subject: applied_science, type: btec_diploma, min_grade: platinum }
							""";

		var act = () => Catalogue.Load(yaml);

		act.Should().Throw<InvalidDataException>().WithMessage("*unknown qualification*platinum*");
	}

	[Fact]
	public void schema_validation_allows_an_open_subject_name()
	{
		const string yaml = """
							subjects:
							  - subject: maths
							    ucas_weight: 50
							    regression: { slope: 0.80, intercept: -1.00 }
							  - subject: drama
							    ucas_weight: 42
							    regression: { slope: 0.75, intercept: -0.50 }
							""";

		var data = Catalogue.Load(yaml);

		data.Subjects.Should().Equal(Subject.Maths, new Subject("drama"));
	}

	// Build a full 14-subject catalogue (every subject weight-only) with one subject replaced by a custom
	// block, so a single-constraint fixture still satisfies the coverage invariant.
	internal static string AllSubjects(string extraFor, string? omit)
	{
		var lines = Catalogue.Subjects
			.Where(subject => !string.Equals(EnumNames.NameOf(subject), omit, StringComparison.Ordinal))
			.Select(static (subject, index) =>
				$"  - subject: {EnumNames.NameOf(subject)}\n    ucas_weight: {index + 30}\n    regression: {{ slope: 0.80, intercept: -1.00 }}");
		return "subjects:\n" + string.Join('\n', lines) + '\n' + extraFor;
	}

	internal static string AllSubjectsFixtureDirectory(string? omit)
	{
		var dir = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", "catalogue-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		File.Copy(Path.Combine(DataDir, CatalogueStore.SchemaFileName), Path.Combine(dir, CatalogueStore.SchemaFileName));
		File.WriteAllText(Path.Combine(dir, CatalogueStore.CatalogueFileName), AllSubjects(string.Empty, omit));
		return dir;
	}

	[Fact]
	public void validation_rejects_unknown_chosen_subjects_against_the_bound_catalogue()
	{
		var student = new StudentInput("S", new Dictionary<string, int> { ["maths"] = 6 }, []) {
			ChosenALevels = [new("philosophy")],
			DateOfBirth = new(2009, 9, 1),
		};

		StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale)
			.Should().ContainSingle()
			.Which.Should().Contain("philosophy");
	}
}
