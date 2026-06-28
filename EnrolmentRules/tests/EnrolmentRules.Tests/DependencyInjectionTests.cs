namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Phase 3 host ergonomics: the DI package should register a singleton engine that resolves once and
///     can be reused across the container lifetime.
/// </summary>
public sealed class DependencyInjectionTests
{
	private static StudentInput ArtAgeGatedStudent() =>
		new("S-AGE",
			new Dictionary<string, int> {
				["english_language"] = 9,
				["maths"] = 9,
				["physics"] = 9,
				["chemistry"] = 9,
				["biology"] = 9,
				["art"] = 6,
			},
			[]) { DateOfBirth = new(2007, 9, 1) };

	private static Rating ArtRating(EnrolmentResult result) =>
		result.Recommendations.Single(r => r.Subject == Subject.Art).Rating;

	[Fact]
	public async Task add_enrolment_engine_registers_a_singleton()
	{
		var services = new ServiceCollection();
		services.AddEnrolmentEngine(options => {
			options.WorkflowsDirectory = Harness.WorkflowsDir;
			options.DataDirectory = Harness.DataDir;
			options.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();

		var first = provider.GetRequiredService<EnrolmentEngine>();
		var second = provider.GetRequiredService<EnrolmentEngine>();

		first.Should().BeSameAs(second);
	}

	[Fact]
	public async Task registers_the_interface_against_the_same_singleton()
	{
		var services = new ServiceCollection();
		services.AddEnrolmentEngine(options => {
			options.WorkflowsDirectory = Harness.WorkflowsDir;
			options.DataDirectory = Harness.DataDir;
			options.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();

		var asInterface = provider.GetRequiredService<IEnrolmentEngine>();
		var asConcrete = provider.GetRequiredService<EnrolmentEngine>();

		asInterface.Should().BeSameAs(asConcrete);
	}

	[Fact]
	public async Task interface_can_be_substituted_by_a_consumer_fake()
	{
		// The seam exists so consumer code can depend on IEnrolmentEngine and inject a stub in their own tests.
		IEnrolmentEngine fake = new StubEngine(Harness.Catalogue);

		var result = await fake.EvaluateAsync(ArtAgeGatedStudent());

		result.Eligible.Should().BeFalse();
		result.Recommendations.Should().BeEmpty();
	}

	[Fact]
	public async Task resolved_engine_evaluates_a_real_student()
	{
		var services = new ServiceCollection();
		services.AddEnrolmentEngine(options => {
			options.WorkflowsDirectory = Harness.WorkflowsDir;
			options.DataDirectory = Harness.DataDir;
			options.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();
		var engine = provider.GetRequiredService<EnrolmentEngine>();

		var result = await engine.EvaluateAsync(ArtAgeGatedStudent());

		result.Eligible.Should().BeTrue();
		result.Recommendations.Should().HaveCount(Harness.Catalogue.Subjects.Count);
	}

	[Fact]
	public async Task time_provider_registration_tracks_the_clock_per_request_not_at_startup()
	{
		var clock = new MutableClock(new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero)); // student is 18
		var services = new ServiceCollection();
		services.AddEnrolmentEngine(options => {
			options.WorkflowsDirectory = Harness.WorkflowsDir;
			options.DataDirectory = Harness.DataDir;
			options.UseTimeProvider(clock);
		});

		await using var provider = services.BuildServiceProvider();
		var engine = provider.GetRequiredService<EnrolmentEngine>();
		var student = ArtAgeGatedStudent();

		var beforeBirthday = await engine.EvaluateAsync(student);
		clock.Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero); // crossed the birthday → 19
		var afterBirthday = await engine.EvaluateAsync(student);

		// A frozen-at-startup date would give the same Art rating both times; a live clock downgrades it.
		((int)ArtRating(beforeBirthday)).Should().BeLessThan((int)ArtRating(afterBirthday));
	}

	// A movable UTC clock so a single registered engine can be observed across a simulated day boundary.
	private sealed class MutableClock(DateTimeOffset now) : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = now;

		public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

		public override DateTimeOffset GetUtcNow() => Now;
	}

	// A trivial hand-written double, proving IEnrolmentEngine is implementable without the concrete engine.
	private sealed class StubEngine(CatalogueData catalogue) : IEnrolmentEngine
	{
		public CatalogueData Catalogue => catalogue;

		public QualificationScale Scale => QualificationScale.Current;

		public Task<EnrolmentResult> EvaluateAsync(StudentInput student) => EvaluateAsync(student, default);

		public Task<EnrolmentResult> EvaluateAsync(StudentInput student, DateOnly asOf) =>
			Task.FromResult(new EnrolmentResult(false, [], [], new(0, 0, 0.0), []));

		public Task<ExplainedResult> ExplainAsync(StudentInput student) => ExplainAsync(student, default);

		public Task<ExplainedResult> ExplainAsync(StudentInput student, DateOnly asOf) =>
			Task.FromResult(new ExplainedResult(false, [], [], new(0, 0, 0.0)));

		public Task<AdviceResult> AdviseAsync(StudentInput student) => AdviseAsync(student, default(DateOnly));

		public Task<AdviceResult> AdviseAsync(StudentInput student, DateOnly asOf) =>
			Task.FromResult(new AdviceResult(false, [], [], null));

		public Task<AdviceResult> AdviseAsync(StudentInput student, bool considerUnsatGcses) =>
			AdviseAsync(student, default(DateOnly));

		public Task<AdviceResult> AdviseAsync(StudentInput student, DateOnly asOf, bool considerUnsatGcses) =>
			AdviseAsync(student, asOf);
	}
}
