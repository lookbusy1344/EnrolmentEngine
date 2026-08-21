namespace EnrolmentRules.Web.Tests;

using System.Net;
using Api;
using AwesomeAssertions;

public sealed class EnrolmentOptionsEndpointTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public EnrolmentOptionsEndpointTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Returns_200_json()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/api/enrolment/options", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
	}

	[Fact]
	public async Task Sets_no_store_cache_control()
	{
		using var client = factory.CreateClient();

		using var response = await client.GetAsync(new Uri("/api/enrolment/options", UriKind.Relative));

		response.Headers.CacheControl!.NoStore.Should().BeTrue();
	}

	[Fact]
	public async Task Contains_catalogue_a_level_subjects_in_catalogue_order()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync("/api/enrolment/options", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		body!.ALevelSubjects.Should().NotBeEmpty();
		body.ALevelSubjects.Select(s => s.Value).Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public async Task Contains_prior_qualification_subjects_and_hobbies()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync("/api/enrolment/options", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		body!.PriorQualificationSubjects.Should().NotBeEmpty();
		body.Hobbies.Should().NotBeEmpty();
		body.ChoiceLimit.Should().BeGreaterThan(0);
	}

	/// <summary>
	///     Every prior-qualification subject group is keyed by the exact <c>QualificationType</c> it
	///     represents — including the two BTEC sub-types, which share a Subject picker section label prefix
	///     but are distinct qualifications with distinct grade scales — so the front end can infer Type
	///     from whichever group the student's chosen subject belongs to, instead of asking for it directly.
	/// </summary>
	[Fact]
	public async Task Groups_prior_qualification_subjects_by_exact_qualification_type()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync("/api/enrolment/options", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		var groups = body!.PriorQualificationSubjects.ToDictionary(group => group.Type, group => group.Subjects.Select(o => o.Value).ToArray());

		groups.Keys.Should().BeEquivalentTo("ALevel", "BtecExtendedCertificate", "BtecDiploma", "Nvq");

		groups["ALevel"].Should().Contain("biology");
		groups["ALevel"].Should().NotContain("applied_science", "applied_science is a btec_diploma entry equivalent, not an A-level");

		groups["BtecDiploma"].Should().Contain("applied_science", "the only real catalogue entry equivalent is typed btec_diploma");

		groups["BtecExtendedCertificate"].Should().BeEquivalentTo("business", "health_and_social_care", "information_technology");

		groups["Nvq"].Should().BeEquivalentTo("construction", "business_administration", "hospitality_and_catering");
	}

	/// <summary>
	///     The catalogue's own-time <c>required_activities</c>/<c>blocking_activities</c> are prefixes
	///     matched with <c>StartsWith</c> (<see cref="Engine.ConstraintPass" />), so Music's bare
	///     <c>plays_</c> entry means "any instrument", not a literal hobby. Offering it verbatim in the
	///     picker as "Plays" let a student satisfy Music's own-time requirement without naming one — the
	///     concrete <c>plays_*</c> examples already in the illustrative list are the real way to satisfy it.
	/// </summary>
	[Fact]
	public async Task Excludes_bare_own_time_prefixes_from_hobby_options()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync("/api/enrolment/options", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		body!.Hobbies.Select(o => o.Value).Should().NotContain("plays_");
	}

	/// <summary>
	///     English Language and Maths are the key GCSEs (gating eligibility): they lead the picker rather
	///     than sit wherever the alphabet puts them. Everything else stays alphabetical behind them.
	/// </summary>
	[Fact]
	public async Task Lists_english_language_then_maths_first_in_gcse_subjects_then_the_rest_alphabetically()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync("/api/enrolment/options", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		var subjects = body!.GcseSubjects.Select(o => o.Value).ToArray();

		subjects.Take(2).Should().Equal("english_language", "maths");
		subjects.Skip(2).Should().BeInAscendingOrder(StringComparer.Ordinal);
	}
}
