namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>Pins the JSON contract for the domain enums and the severity ordering of <see cref="Rating" />.</summary>
public sealed class DomainTests
{
	public static TheoryData<string, string> SerialisedSubjects { get; } = new() {
		{
			"maths", "\"maths\""
		}, {
			"further_maths", "\"further_maths\""
		}, {
			"english_language", "\"english_language\""
		}, {
			"english_literature", "\"english_literature\""
		}, {
			"french", "\"french\""
		}, {
			"german", "\"german\""
		}, {
			"physical_education", "\"physical_education\""
		}, {
			"computer_studies", "\"computer_studies\""
		},
	};

	[Theory]
	[InlineData(Rating.Green, "\"green\"")]
	[InlineData(Rating.Amber, "\"amber\"")]
	[InlineData(Rating.Red, "\"red\"")]
	public void rating_serialises_to_lowercase(Rating rating, string expected) =>
		JsonSerializer.Serialize(rating).Should().Be(expected);

	[Fact]
	public void a_level_grade_names_an_exact_point() => ALevelGrade.Name(ALevelGrade.A).Should().Be("A");

	[Fact]
	public void a_level_grade_name_throws_off_grid()
	{
		var act = () => ALevelGrade.Name(4.5);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Theory]
	[InlineData(4.9, "A")]
	[InlineData(4.2, "B")]
	[InlineData(4.5, "A")] // tie broken towards the higher grade
	public void a_level_grade_rounds_a_continuous_prediction_to_the_nearest_band(double points, string expected) =>
		ALevelGrade.NearestBand(points).Name.Should().Be(expected);

	[Fact]
	public void shipped_layout_finds_a_file_beside_the_base_directory()
	{
		var name = $"shipped-layout-{Guid.NewGuid():N}.marker";
		var path = Path.Combine(AppContext.BaseDirectory, name);
		File.WriteAllText(path, "x");
		try {
			ShippedLayout.Locate(name).Should().Be(path);
		}
		finally {
			File.Delete(path);
		}
	}

	[Fact]
	public void shipped_layout_throws_when_the_path_is_absent()
	{
		// A marker no directory carries pins the walk to nothing, so nothing is found. (The successful marker
		// walk is exercised by every CLI test, which resolves workflows/data/policies through it.)
		var act = () => ShippedLayout.Locate($"missing-{Guid.NewGuid():N}.yaml", $"no-such-marker-{Guid.NewGuid():N}");

		act.Should().Throw<FileNotFoundException>();
	}

	[Theory]
	[MemberData(nameof(SerialisedSubjects))]
	public void subject_serialises_to_snake_case(string subjectName, string expected)
	{
		Subject.TryParse(subjectName, out var subject).Should().BeTrue();
		JsonSerializer.Serialize(subject).Should().Be(expected);
	}

	[Fact]
	public void subject_round_trips_an_open_name()
	{
		var json = JsonSerializer.Serialize(new Subject("drama"));

		json.Should().Be("\"drama\"");
		JsonSerializer.Deserialize<Subject>(json).Should().Be(new("drama"));
	}

	[Fact]
	public void subject_parse_returns_an_open_name() => Subject.Parse("drama").Should().Be(new("drama"));

	[Fact]
	public void subject_parse_throws_for_an_invalid_name()
	{
		var act = () => Subject.Parse("Drama");

		act.Should().Throw<FormatException>().WithMessage("*not a valid subject name*");
	}

	[Theory]
	[InlineData("")]
	[InlineData("Drama")]
	[InlineData("drama_")]
	public void subject_constructor_rejects_an_invalid_name(string value)
	{
		var act = () => new Subject(value);

		act.Should().Throw<ArgumentException>().WithParameterName(nameof(value));
	}

	[Fact]
	public void default_subject_stringifies_to_empty_not_null() =>
		// FDG §8: ToString must never return null; the strongly-typed-string zero state is the empty string.
		default(Subject).ToString().Should().BeEmpty();
}
