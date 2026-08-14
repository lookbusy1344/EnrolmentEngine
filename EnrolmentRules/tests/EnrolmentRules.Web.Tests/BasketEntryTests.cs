namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Domain;
using Models;

public sealed class BasketEntryTests
{
	[Fact]
	public void A_not_offered_choice_uses_the_red_basket_treatment()
	{
		var entry = new BasketEntry(Subject.Sociology, ChoiceStatus.NotOffered, null, "Not offered under Elite.");

		entry.CssClass.Should().Be("text-bg-danger");
	}
}
