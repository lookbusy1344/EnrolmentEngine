namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Engine;
using Services;

/// <summary>
///     <see cref="EnrolmentOptionsService" /> must read the same reference date the policy's engine
///     evaluates against, not a second, independently-configured clock (F13) — the endpoint used to pass a
///     <c>TimeProvider</c> that could disagree with the engine's own <c>AsOfSource</c> around a local/UTC
///     midnight boundary.
/// </summary>
public sealed class EnrolmentOptionsServiceTests
{
	[Fact]
	public void Today_and_default_date_of_birth_track_the_engines_own_reference_date()
	{
		var fixedToday = new DateOnly(2026, 3, 5);
		var engine = EnrolmentEngine.Create(
			Path.Combine(WebAppFactory.RepoRoot, "workflows"),
			Path.Combine(WebAppFactory.RepoRoot, "data"),
			() => fixedToday);
		var policy = new EnrolmentPolicy(new(new("standard"), "Standard"), engine);

		var options = new EnrolmentOptionsService(policy);

		options.Today().Should().Be(fixedToday);
		options.DefaultDateOfBirth().Should().Be(fixedToday.AddYears(-16));
		options.DefaultAge().Should().Be(16);
	}

	[Fact]
	public void Cache_is_scoped_to_one_application_and_policy_instance()
	{
		var engine = EnrolmentEngine.Create(
			Path.Combine(WebAppFactory.RepoRoot, "workflows"),
			Path.Combine(WebAppFactory.RepoRoot, "data"),
			static () => new(2026, 3, 5));
		var firstPolicy = new EnrolmentPolicy(new(new("standard"), "Standard"), engine);
		var replacementPolicy = new EnrolmentPolicy(new(new("standard"), "Replacement"), engine);
		var firstApplication = new EnrolmentOptionsServiceCache();
		var secondApplication = new EnrolmentOptionsServiceCache();

		firstApplication.Get(firstPolicy).Should().BeSameAs(firstApplication.Get(firstPolicy));
		firstApplication.Get(replacementPolicy).Should().NotBeSameAs(firstApplication.Get(firstPolicy));
		secondApplication.Get(firstPolicy).Should().NotBeSameAs(firstApplication.Get(firstPolicy));
	}
}
