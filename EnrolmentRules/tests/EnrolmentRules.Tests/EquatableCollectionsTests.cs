namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Pins the value-semantics fix the RecordValueAnalyser (JSV01) demands: records carrying collection
///     members must compare and hash by element, and must still round-trip through the source-generated
///     JSON contract unchanged.
/// </summary>
public sealed class EquatableCollectionsTests
{
	[Fact]
	public void explicit_copy_factories_reject_null()
	{
		IEnumerable<int>? items = null;
		IReadOnlyDictionary<string, int>? entries = null;

		var copyArray = () => EquatableArray.CopyOf(items!);
		var copyDictionary = () => EquatableDictionaryFactory.CopyOf(entries!);

		copyArray.Should().Throw<ArgumentNullException>().WithParameterName("items");
		copyDictionary.Should().Throw<ArgumentNullException>().WithParameterName("entries");
	}

	[Fact]
	public void raw_student_constructor_preserves_absent_collections_for_validation()
	{
		IReadOnlyDictionary<string, int>? gcses = null;
		IReadOnlyList<string>? hobbies = null;
		var student = new StudentInput("S1", gcses, hobbies);

		var errors = StudentValidator.Validate(student, Harness.Catalogue, Harness.Scale);

		errors.Should().Contain("gcses is required").And.Contain("hobbies is required");
	}

	[Fact]
	public void records_with_equal_but_distinct_lists_are_equal()
	{
		// Distinct backing instances, equal contents — reference equality would call these unequal.
		var a = new StudentProfile("S1", 7.0, [new(Subject.Maths, 5.0)], [], ["plays_piano"]);
		var b = new StudentProfile("S1", 7.0, [new(Subject.Maths, 5.0)], [], ["plays_piano"]);

		a.Should().Be(b);
		a.GetHashCode().Should().Be(b.GetHashCode());
	}

	[Fact]
	public void records_with_differing_lists_are_not_equal()
	{
		var a = new StudentProfile("S1", 7.0, [new(Subject.Maths, 5.0)], [], ["plays_piano"]);
		var b = new StudentProfile("S1", 7.0, [new(Subject.Maths, 4.0)], [], ["plays_piano"]);

		a.Should().NotBe(b);
	}

	[Fact]
	public void records_with_equal_but_distinct_dictionaries_are_equal()
	{
		var a = new StudentInput("S1", new Dictionary<string, int> { ["maths"] = 7, ["physics"] = 6 }, []);
		// Same entries, different insertion order — value equality must ignore order.
		var b = new StudentInput("S1", new Dictionary<string, int> { ["physics"] = 6, ["maths"] = 7 }, []);

		a.Should().Be(b);
		a.GetHashCode().Should().Be(b.GetHashCode());
	}

	[Fact]
	public void student_profile_round_trips_through_source_generated_json()
	{
		var profile = new StudentProfile(
			"S1",
			7.5,
			[new(Subject.Maths, 5.0), new(Subject.Physics, 4.5)],
			[],
			["plays_piano", "chess"]) { ChosenALevels = [Subject.Maths] };

		var json = JsonSerializer.Serialize(profile, EnrolmentJsonContext.Default.StudentProfile);
		var back = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentProfile);

		back.Should().Be(profile);
	}

	[Fact]
	public void student_document_dictionary_round_trips_through_source_generated_json()
	{
		var document = new StudentDocument(
			new StudentInput("S1", new Dictionary<string, int> { ["maths"] = 7, ["physics"] = 6 }, ["chess"]) with { ChosenALevels = [Subject.Physics] });

		var json = JsonSerializer.Serialize(document, EnrolmentJsonContext.Default.StudentDocument);
		var back = JsonSerializer.Deserialize(json, EnrolmentJsonContext.Default.StudentDocument);

		back.Should().Be(document);
	}
}
