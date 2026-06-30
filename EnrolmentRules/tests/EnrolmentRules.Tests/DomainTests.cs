namespace EnrolmentRules.Tests;

using System.Text.Json;
using AwesomeAssertions;
using Domain;

/// <summary>Pins the JSON contract for the domain enums and the severity ordering of <see cref="Rating" />.</summary>
public sealed class DomainTests
{
	public static TheoryData<string, string> SerialisedSubjects { get; } = new() {
		{ "maths", "\"maths\"" },
		{ "further_maths", "\"further_maths\"" },
		{ "english_language", "\"english_language\"" },
		{ "english_literature", "\"english_literature\"" },
		{ "french", "\"french\"" },
		{ "german", "\"german\"" },
		{ "physical_education", "\"physical_education\"" },
		{ "computer_studies", "\"computer_studies\"" },
	};

	[Theory]
	[InlineData(Rating.Green, "\"green\"")]
	[InlineData(Rating.Amber, "\"amber\"")]
	[InlineData(Rating.Red, "\"red\"")]
	public void rating_serialises_to_lowercase(Rating rating, string expected) =>
		JsonSerializer.Serialize(rating).Should().Be(expected);

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

	[Fact]
	public void most_severe_returns_the_worse_rating()
	{
		Rating.Green.MostSevere(Rating.Amber).Should().Be(Rating.Amber);
		Rating.Amber.MostSevere(Rating.Red).Should().Be(Rating.Red);
		Rating.Red.MostSevere(Rating.Green).Should().Be(Rating.Red);
		Rating.Green.MostSevere(Rating.Green).Should().Be(Rating.Green);
	}
}
