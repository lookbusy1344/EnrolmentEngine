namespace EnrolmentRules.Web.Tests;

using AwesomeAssertions;
using Domain;
using Microsoft.Net.Http.Headers;
using Models;
using Services;

public sealed class EnrolmentStateCookieStoreTests
{
	private readonly EnrolmentStateCookieStore store = new();

	[Fact]
	public async Task Load_with_no_cookie_returns_an_empty_snapshot()
	{
		var snapshot = await store.LoadAsync(new DefaultHttpContext());

		snapshot.DateOfBirth.Should().BeNull();
		snapshot.Gcses.Should().BeEmpty();
		snapshot.PriorQualifications.Should().BeEmpty();
		snapshot.Hobbies.Should().BeEmpty();
		snapshot.ChosenALevels.Should().BeEmpty();
	}

	[Fact]
	public async Task Save_then_load_round_trips_every_field()
	{
		var original = new EnrolmentSession(
			"student-2",
			new DateOnly(2009, 3, 14),
			[new("maths", 8), new("english_language", 6)],
			[new("Maths", QualificationType.ALevel, "a")],
			["chess_club", "coding"],
			[new("maths"), new("physics")]);

		var writeContext = new DefaultHttpContext();
		await store.SaveAsync(writeContext, original);

		var loaded = await store.LoadAsync(ContextCarryingCookieFrom(writeContext));

		loaded.Should().Be(original);
	}

	[Fact]
	public async Task Reset_clears_a_previously_saved_snapshot()
	{
		var saveContext = new DefaultHttpContext();
		await store.SaveAsync(saveContext, new("student-3", new DateOnly(2008, 1, 1), [], [], [], []));
		var savedContext = ContextCarryingCookieFrom(saveContext);

		var resetContext = new DefaultHttpContext {
			Request = {
				Cookies = savedContext.Request.Cookies,
			},
		};
		await store.ResetAsync(resetContext);

		var deleteCookie = SingleSetCookie(resetContext);
		deleteCookie.Expires.Should().NotBeNull();
		deleteCookie.Expires!.Value.Should().BeBefore(DateTimeOffset.UtcNow);
	}

	[Fact]
	public async Task Load_with_a_malformed_cookie_value_returns_an_empty_snapshot_and_clears_it()
	{
		var context = new DefaultHttpContext();
		context.Request.Headers.Cookie = "enrolment.state=not-valid-base64-or-json!!!";

		var snapshot = await store.LoadAsync(context);

		snapshot.Gcses.Should().BeEmpty();
		var deleteCookie = SingleSetCookie(context);
		deleteCookie.Expires.Should().NotBeNull();
		deleteCookie.Expires!.Value.Should().BeBefore(DateTimeOffset.UtcNow);
	}

	/// <summary>Simulates the browser round trip: copies the <c>Set-Cookie</c> response header into a fresh request's <c>Cookie</c> header.</summary>
	private static DefaultHttpContext ContextCarryingCookieFrom(HttpContext previousResponse)
	{
		var setCookie = SingleSetCookie(previousResponse);
		var context = new DefaultHttpContext();
		context.Request.Headers.Cookie = new CookieHeaderValue(setCookie.Name, setCookie.Value).ToString();
		return context;
	}

	private static SetCookieHeaderValue SingleSetCookie(HttpContext context) =>
		SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.ToArray()!).Single();
}
