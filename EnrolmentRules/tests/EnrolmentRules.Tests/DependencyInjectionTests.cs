namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using Extensions.DependencyInjection;
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
		_ = services.AddEnrolmentEngine(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(Harness.DataDir)
				.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();

		var first = provider.GetRequiredService<EnrolmentEngine>();
		var second = provider.GetRequiredService<EnrolmentEngine>();

		first.Should().BeSameAs(second);
	}

	[Fact]
	public void add_enrolment_engine_rejects_an_empty_workflows_directory()
	{
		var services = new ServiceCollection();

		var act = () => services.AddEnrolmentEngine(options => {
			options.UseWorkflowsDirectory(string.Empty)
				.UseDataDirectory(Harness.DataDir);
		});

		act.Should().Throw<ArgumentException>().WithParameterName("workflowsDirectory");
	}

	[Fact]
	public void add_enrolment_engine_rejects_an_empty_data_directory()
	{
		var services = new ServiceCollection();

		var act = () => services.AddEnrolmentEngine(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(string.Empty);
		});

		act.Should().Throw<ArgumentException>().WithParameterName("dataDirectory");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	public void options_reject_an_invalid_workflows_directory(string? workflowsDirectory)
	{
		var act = () => new EnrolmentEngineOptions().UseWorkflowsDirectory(workflowsDirectory!);

		act.Should().Throw<ArgumentException>().WithParameterName(nameof(workflowsDirectory));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	public void options_reject_an_invalid_data_directory(string? dataDirectory)
	{
		var act = () => new EnrolmentEngineOptions().UseDataDirectory(dataDirectory!);

		act.Should().Throw<ArgumentException>().WithParameterName(nameof(dataDirectory));
	}

	[Fact]
	public async Task registers_the_interface_against_the_same_singleton()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentEngine(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(Harness.DataDir)
				.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();

		var asInterface = provider.GetRequiredService<IEnrolmentEngine>();
		var asConcrete = provider.GetRequiredService<EnrolmentEngine>();

		asInterface.Should().BeSameAs(asConcrete);
	}

	[Fact]
	public void interface_can_be_substituted_by_a_consumer_fake()
	{
		// The seam exists so consumer code can depend on IEnrolmentEngine and inject a stub in their own tests.
		IEnrolmentEngine fake = new StubEngine(Harness.Catalogue);

		var result = fake.Evaluate(ArtAgeGatedStudent());

		result.Eligible.Should().BeFalse();
		result.Recommendations.Should().BeEmpty();
	}

	[Fact]
	public async Task add_enrolment_engine_accepts_a_pre_built_engine()
	{
		var engine = EnrolmentEngine.Create(Harness.WorkflowsDir, Harness.DataDir, Harness.AsOf);
		var services = new ServiceCollection();
		services.AddEnrolmentEngine(engine);

		await using var provider = services.BuildServiceProvider();

		provider.GetRequiredService<EnrolmentEngine>().Should().BeSameAs(engine);
		provider.GetRequiredService<IEnrolmentEngine>().Should().BeSameAs(engine);
	}

	[Fact]
	public void add_enrolment_engine_has_no_disposable_concrete_engine_lifetime_to_own() =>
		typeof(EnrolmentEngine).Should().NotBeAssignableTo<IDisposable>();

	[Fact]
	public async Task resolved_engine_evaluates_a_real_student()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentEngine(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(Harness.DataDir)
				.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();
		var engine = provider.GetRequiredService<EnrolmentEngine>();

		var result = engine.Evaluate(ArtAgeGatedStudent());

		result.Eligible.Should().BeTrue();
		result.Recommendations.Should().HaveCount(Harness.Catalogue.Subjects.Count);
	}

	[Fact]
	public async Task time_provider_registration_tracks_the_clock_per_request_not_at_startup()
	{
		var clock = new MutableClock(new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero)); // student is 18
		var services = new ServiceCollection();
		_ = services.AddEnrolmentEngine(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(Harness.DataDir)
				.UseTimeProvider(clock);
		});

		await using var provider = services.BuildServiceProvider();
		var engine = provider.GetRequiredService<EnrolmentEngine>();
		var student = ArtAgeGatedStudent();

		var beforeBirthday = engine.Evaluate(student);
		clock.Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero); // crossed the birthday → 19
		var afterBirthday = engine.Evaluate(student);

		// A frozen-at-startup date would give the same Art rating both times; a live clock downgrades it.
		((int)ArtRating(beforeBirthday)).Should().BeLessThan((int)ArtRating(afterBirthday));
	}

	[Fact]
	public async Task add_enrolment_engine_factory_registers_a_live_interface_proxy()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentEngineFactory(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(Harness.DataDir)
				.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();
		var factory = provider.GetRequiredService<IEnrolmentEngineFactory>();
		var resolved = provider.GetRequiredService<IEnrolmentEngine>();
		var evaluator = (IEnrolmentEvaluator)resolved;
		var currentEvaluator = (IEnrolmentEvaluator)factory.Current;

		resolved.Should().NotBeSameAs(factory.Current);
		evaluator.Catalogue.Should().BeSameAs(currentEvaluator.Catalogue);
		evaluator.Scale.Should().BeSameAs(currentEvaluator.Scale);
	}

	[Fact]
	public async Task add_enrolment_engine_factory_keeps_interface_resolution_live_across_reload()
	{
		var fixture = CopyShippedLayout();
		try {
			var services = new ServiceCollection();
			_ = services.AddEnrolmentEngineFactory(options => {
				options.UseWorkflowsDirectory(Path.Combine(fixture, "workflows"))
					.UseDataDirectory(Path.Combine(fixture, "data"))
					.UseFixedAsOf(Harness.AsOf);
			});

			await using var provider = services.BuildServiceProvider();
			var engine = provider.GetRequiredService<IEnrolmentEngine>();
			var factory = provider.GetRequiredService<IEnrolmentEngineFactory>();
			var student = ReloadEligibleStudent();

			engine.TryEvaluate(student).Value!.Eligible.Should().BeTrue();

			RaisePassGrade(Path.Combine(fixture, "data", "thresholds.yaml"), 7);
			factory.Reload();

			engine.TryEvaluate(student).Value!.Eligible.Should().BeFalse();
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public async Task disposing_the_service_provider_disposes_the_registered_enrolment_engine_factory()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentEngineFactory(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(Harness.DataDir)
				.UseFixedAsOf(Harness.AsOf);
		});

		var provider = services.BuildServiceProvider();
		var factory = provider.GetRequiredService<EnrolmentEngineFactory>();

		await provider.DisposeAsync();

		var act = () => factory.Reload();

		act.Should().Throw<ObjectDisposedException>();
	}

	[Fact]
	public async Task resolved_engine_try_evaluate_rejects_invalid_input()
	{
		var services = new ServiceCollection();
		_ = services.AddEnrolmentEngine(options => {
			options.UseWorkflowsDirectory(Harness.WorkflowsDir)
				.UseDataDirectory(Harness.DataDir)
				.UseFixedAsOf(Harness.AsOf);
		});

		await using var provider = services.BuildServiceProvider();
		var engine = provider.GetRequiredService<IEnrolmentEngine>();
		var student = new StudentInput("S-BAD", new Dictionary<string, int> { ["maths"] = 99 }, []) { DateOfBirth = new(2009, 9, 1) };

		var outcome = engine.TryEvaluate(student);

		outcome.Validation.IsValid.Should().BeFalse();
		outcome.Value.Should().BeNull();
		outcome.Validation.Errors.Should().ContainSingle()
			.Which.Should().Contain("maths").And.Contain("out of range");
	}

	private static StudentInput ReloadEligibleStudent() =>
		new(
			"S-RELOAD",
			new Dictionary<string, int> {
				["english_language"] = 6,
				["maths"] = 6,
				["physics"] = 6,
				["chemistry"] = 6,
				["biology"] = 6,
			},
			[]) { DateOfBirth = new(2009, 9, 1) };

	private static string CopyShippedLayout()
	{
		var fixture = Path.Combine(Path.GetTempPath(), "enrolmentrules-tests", Guid.NewGuid().ToString("N"));
		CopyTree(Harness.WorkflowsDir, Path.Combine(fixture, "workflows"));
		CopyTree(Harness.DataDir, Path.Combine(fixture, "data"));
		return fixture;
	}

	private static void CopyTree(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			var relative = Path.GetRelativePath(source, file);
			var target = Path.Combine(destination, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, true);
		}
	}

	private static void RaisePassGrade(string thresholdsPath, int passGrade)
	{
		var lines = File.ReadAllLines(thresholdsPath);
		for (var i = 0; i < lines.Length; i++) {
			if (lines[i].StartsWith("pass_grade:", StringComparison.Ordinal)) {
				lines[i] = $"pass_grade: {passGrade}";
			}
		}

		File.WriteAllLines(thresholdsPath, lines);
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

		public QualificationScale Scale => QualificationScale.Default;

		public EnrolmentResult Evaluate(StudentInput student, CancellationToken cancellationToken = default) =>
			Evaluate(student, default, CancellationToken.None);

		public EnrolmentResult Evaluate(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			new(false, [], [], new(0, 0, 0.0), []);

		public ExplainedResult Explain(StudentInput student, CancellationToken cancellationToken = default) =>
			Explain(student, default, CancellationToken.None);

		public ExplainedResult Explain(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			new(false, [], [], new(0, 0, 0.0));

		public AdviceResult Advise(StudentInput student, CancellationToken cancellationToken = default) =>
			Advise(student, default(DateOnly), CancellationToken.None);

		public AdviceResult Advise(StudentInput student, DateOnly asOf, CancellationToken cancellationToken = default) =>
			new(false, [], [], null);

		public AdviceResult Advise(StudentInput student, bool considerUnsatGcses, CancellationToken cancellationToken = default) =>
			Advise(student, default(DateOnly), CancellationToken.None);

		public AdviceResult Advise(StudentInput student, DateOnly asOf, bool considerUnsatGcses,
			CancellationToken cancellationToken = default) =>
			Advise(student, asOf, CancellationToken.None);

		public ValidatedEvaluation<EnrolmentResult> TryEvaluate(StudentInput student, CancellationToken cancellationToken = default) =>
			TryEvaluate(student, default, cancellationToken);

		public ValidatedEvaluation<EnrolmentResult> TryEvaluate(StudentInput student, DateOnly asOf,
			CancellationToken cancellationToken = default) =>
			new(ValidationOutcome.Valid, null);

		public ValidatedEvaluation<ExplainedResult> TryExplain(StudentInput student, CancellationToken cancellationToken = default) =>
			TryExplain(student, default, cancellationToken);

		public ValidatedEvaluation<ExplainedResult> TryExplain(StudentInput student, DateOnly asOf,
			CancellationToken cancellationToken = default) =>
			new(ValidationOutcome.Valid, null);

		public ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, CancellationToken cancellationToken = default) =>
			TryAdvise(student, default(DateOnly), cancellationToken);

		public ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, DateOnly asOf,
			CancellationToken cancellationToken = default) =>
			TryAdvise(student, asOf, false, cancellationToken);

		public ValidatedEvaluation<AdviceResult> TryAdvise(StudentInput student, bool considerUnsatGcses,
			CancellationToken cancellationToken = default) =>
			TryAdvise(student, default, considerUnsatGcses, cancellationToken);

		public ValidatedEvaluation<AdviceResult> TryAdvise(
			StudentInput student,
			DateOnly asOf,
			bool considerUnsatGcses,
			CancellationToken cancellationToken = default) =>
			new(ValidationOutcome.Valid, null);
	}
}
