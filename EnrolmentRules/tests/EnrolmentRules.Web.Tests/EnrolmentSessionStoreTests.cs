namespace EnrolmentRules.Web.Tests;

using System.Text;
using AwesomeAssertions;
using Domain;
using Models;
using Services;

public sealed class EnrolmentSessionStoreTests
{
	private readonly EnrolmentSessionStore store = new();

	[Fact]
	public async Task Load_with_no_stored_snapshot_returns_empty_snapshot_keyed_to_session_id()
	{
		var session = new FakeSession("student-1");

		var snapshot = await store.LoadAsync(session);

		snapshot.Should().Be(EnrolmentSession.Empty("student-1"));
		session.LoadAsyncCallCount.Should().Be(1);
		session.CommitAsyncCallCount.Should().Be(0);
	}

	[Fact]
	public async Task Save_then_load_round_trips_every_field()
	{
		var session = new FakeSession("student-2");
		var original = new EnrolmentSession(
			"student-2",
			new DateOnly(2009, 3, 14),
			[new("maths", 8), new("english_language", 6)],
			[new("Maths", QualificationType.Gcse, "8")],
			["chess_club", "coding"],
			[new("maths"), new("physics")]);

		await store.SaveAsync(session, original);
		var loaded = await store.LoadAsync(session);

		loaded.Should().Be(original);
		session.LoadAsyncCallCount.Should().Be(1);
		session.CommitAsyncCallCount.Should().Be(1);
	}

	[Fact]
	public async Task Reset_clears_only_the_enrolment_session_key()
	{
		var session = new FakeSession("student-3");
		session.Set("other.key", [1, 2, 3]);
		await store.SaveAsync(session, new("student-3", new DateOnly(2008, 1, 1), [], [], [], []));

		await store.ResetAsync(session);

		(await store.LoadAsync(session)).Should().Be(EnrolmentSession.Empty("student-3"));
		session.TryGetValue("other.key", out var otherValue).Should().BeTrue();
		otherValue.Should().Equal(1, 2, 3);
		session.CommitAsyncCallCount.Should().Be(2);
	}

	[Fact]
	public async Task Load_with_malformed_snapshot_resets_the_enrolment_session_key()
	{
		var session = new FakeSession("student-4");
		session.Set("enrolment.session", Encoding.UTF8.GetBytes("{ definitely not a session snapshot }"));
		session.Set("other.key", [4, 5, 6]);

		var loaded = await store.LoadAsync(session);

		loaded.Should().Be(EnrolmentSession.Empty("student-4"));
		session.TryGetValue("enrolment.session", out _).Should().BeFalse();
		session.TryGetValue("other.key", out var otherValue).Should().BeTrue();
		otherValue.Should().Equal(4, 5, 6);
		session.CommitAsyncCallCount.Should().Be(1);
	}
}
