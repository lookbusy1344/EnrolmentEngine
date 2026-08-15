namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using Models;

public sealed class BasketEntryTests
{
	[Fact]
	public void A_not_offered_choice_uses_the_red_basket_treatment()
	{
		var entry = new BasketEntry(Subject.Sociology, ChoiceStatus.NotOffered, null, "Not offered under Elite.");

		entry.CssClass.Should().Be("text-bg-danger");
	}

	[Fact]
	public void From_lists_valid_choices_before_invalid_ones_alphabetical_within_each_group()
	{
		var explanation = new ExplainedResult(true, [], [], new(0, 0, 0));
		var comparison = new PolicyComparisonResult(
			new(new("standard"), "Standard"),
			explanation,
			[
				new(Subject.Music, ChoiceStatus.Available, null),
				new(Subject.Art, ChoiceStatus.Unavailable, "barred"),
				new(Subject.Biology, ChoiceStatus.Available, null),
				new(Subject.Sociology, ChoiceStatus.NotOffered, "not offered under this policy"),
			],
			3,
			4);

		var basket = BasketEntry.From(comparison);

		// Valid (Biology, Music) alphabetical, then invalid (Art, Sociology) alphabetical — not choice order.
		basket.Select(static entry => entry.Subject).Should().Equal(
			Subject.Biology, Subject.Music, Subject.Art, Subject.Sociology);
	}
}
