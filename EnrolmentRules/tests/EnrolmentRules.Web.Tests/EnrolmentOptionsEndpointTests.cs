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
	public async Task Contains_qualification_type_labels()
	{
		using var client = factory.CreateClient();

		var body = await client.GetFromJsonAsync("/api/enrolment/options", EnrolmentApiJsonContext.Default.EnrolmentOptionsResponse);

		body.Should().NotBeNull();
		body!.QualificationTypes.Should().NotBeEmpty();
		body.QualificationTypes.Should().Contain(item => item.Value == "BtecDiploma" && item.Label == "BTEC Diploma");
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
}
