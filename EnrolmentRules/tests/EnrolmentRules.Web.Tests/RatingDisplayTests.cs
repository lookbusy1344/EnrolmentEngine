namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Domain;
using Models;

public sealed class RatingDisplayTests
{
	[Fact]
	public void OrderForCards_sorts_by_colour_then_alphabetically_by_label()
	{
		Explanation[] explanations = [
			MakeExplanation(Subject.Physics, Rating.Green),
			MakeExplanation(Subject.Art, Rating.Amber),
			MakeExplanation(Subject.Biology, Rating.Green),
			MakeExplanation(Subject.FurtherMaths, Rating.Red),
			MakeExplanation(Subject.Chemistry, Rating.Amber),
		];

		var ordered = RatingDisplay.OrderForCards(explanations).Select(static e => e.Subject).ToArray();

		ordered.Should().Equal(Subject.Biology, Subject.Physics, Subject.Art, Subject.Chemistry, Subject.FurtherMaths);
	}

	[Fact]
	public void OrderForCards_keeps_original_order_for_two_subjects_already_sharing_a_colour_and_first_letter()
	{
		Explanation[] explanations = [MakeExplanation(Subject.Drama, Rating.Red), MakeExplanation(Subject.DesignTechnology, Rating.Red)];

		var ordered = RatingDisplay.OrderForCards(explanations).Select(static e => e.Subject).ToArray();

		// "Design Technology" < "Drama" — the prettified label decides the tie, not catalogue/enum order.
		ordered.Should().Equal(Subject.DesignTechnology, Subject.Drama);
	}

	private static Explanation MakeExplanation(Subject subject, Rating rating) =>
		new(subject, rating, "reason", rating, "rule", "base reason", 0.0, []);
}
