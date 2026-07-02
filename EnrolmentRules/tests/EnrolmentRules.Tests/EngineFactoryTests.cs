namespace EnrolmentRules.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using AwesomeAssertions;
using Domain;
using Engine;
using Prediction;

/// <summary>Reloadable engine factory for policy edits without process restart.</summary>
public sealed class EngineFactoryTests
{
	private const int ThreadJoinTimeoutMilliseconds = 5_000;
	private const int SignalWaitTimeoutMilliseconds = 5_000;

	private static StudentInput EligibleStudent() =>
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

	[Fact]
	public void reload_picks_up_threshold_changes_from_disk()
	{
		var fixture = CopyShippedLayout();
		try {
			using var factory = EnrolmentEngineFactory.Create(
				Path.Combine(fixture, "workflows"),
				Path.Combine(fixture, "data"),
				Harness.AsOf);
			var student = EligibleStudent();

			factory.Current.TryEvaluate(student).Value!.Eligible.Should().BeTrue();

			RaisePassGrade(Path.Combine(fixture, "data", "thresholds.yaml"), 7);
			factory.Reload();

			var afterPassGradeReload = factory.Current.TryEvaluate(student).Value!;
			afterPassGradeReload.Eligible.Should().BeFalse();
			afterPassGradeReload.EligibilityReasons.Should().Equal(
				"GCSE English Language below the pass grade (7)",
				"GCSE Maths below the pass grade (7)",
				"Fewer than the required number of GCSE passes (5 at grade 7 or above)");

			RaiseMinPasses(Path.Combine(fixture, "data", "thresholds.yaml"), 6);
			factory.Reload();

			var afterMinPassesReload = factory.Current.TryEvaluate(StudentForPassGradeBoundary()).Value!;
			afterMinPassesReload.Eligible.Should().BeFalse();
			afterMinPassesReload.EligibilityReasons.Should().ContainSingle()
				.Which.Should().Be("Fewer than the required number of GCSE passes (6 at grade 7 or above)");
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public void reload_leaves_current_unchanged_when_bootstrap_fails()
	{
		var fixture = CopyShippedLayout();
		try {
			using var factory = EnrolmentEngineFactory.Create(
				Path.Combine(fixture, "workflows"),
				Path.Combine(fixture, "data"),
				Harness.AsOf);
			var before = factory.Current;

			File.WriteAllText(Path.Combine(fixture, "workflows", "eligibility.yaml"), "not: valid");

			var act = () => factory.Reload();
			act.Should().Throw<WorkflowException>();
			factory.Current.Should().BeSameAs(before);
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public void concurrent_evaluations_during_reload_do_not_throw()
	{
		using var source = ControlledReloadDataSource.WithReloadThresholds(
			Harness.WorkflowsDir,
			Harness.DataDir,
			true,
			ThresholdsBytes("min_passes", "4"));
		using var factory = EnrolmentEngineFactory.Create(source, Harness.AsOf);
		var student = EligibleStudent();
		var errors = new ConcurrentQueue<Exception>();
		var validationResults = new ConcurrentQueue<bool>();
		using var startGate = new ManualResetEventSlim(false);

		var reloadThread = new Thread(() => {
			try {
				WaitForSignal(startGate, "reload start gate");
				factory.Reload();
			}
			catch (Exception exception) {
				errors.Enqueue(exception);
			}
		});

		var evaluationThreads = Enumerable.Range(0, 50)
			.Select(_ => new Thread(() => {
				try {
					WaitForSignal(startGate, "evaluation start gate");
					source.WaitForFirstReloadBlocked();
					validationResults.Enqueue(factory.Current.TryEvaluate(student).Validation.IsValid);
				}
				catch (Exception exception) {
					errors.Enqueue(exception);
				}
			}))
			.ToArray();

		reloadThread.Start();
		foreach (var thread in evaluationThreads) {
			thread.Start();
		}

		startGate.Set();
		try {
			source.WaitForFirstReloadBlocked();
			JoinThreads(evaluationThreads, "evaluation thread");
		}
		finally {
			source.ReleaseFirstReload();
			JoinThread(reloadThread, "reload thread");
		}

		errors.Should().BeEmpty();
		validationResults.Should().HaveCount(evaluationThreads.Length)
			.And.OnlyContain(static isValid => isValid);
	}

	[Fact]
	public void concurrent_reload_calls_are_serialized_and_publish_in_call_order()
	{
		using var source = ControlledReloadDataSource.WithReloadThresholds(
			Harness.WorkflowsDir,
			Harness.DataDir,
			true,
			ThresholdsBytes("min_passes", "4"),
			ThresholdsBytes("min_passes", "6"));
		using var factory = EnrolmentEngineFactory.Create(source, Harness.AsOf);
		var student = StudentForPassGradeBoundary();
		var errors = new ConcurrentQueue<Exception>();
		using var startGate = new ManualResetEventSlim(false);

		var firstReload = new Thread(() => {
			try {
				WaitForSignal(startGate, "first reload start gate");
				factory.Reload();
			}
			catch (Exception exception) {
				errors.Enqueue(exception);
			}
		});

		var secondReload = new Thread(() => {
			try {
				WaitForSignal(startGate, "second reload start gate");
				source.WaitForFirstReloadBlocked();
				factory.Reload();
			}
			catch (Exception exception) {
				errors.Enqueue(exception);
			}
		});

		firstReload.Start();
		secondReload.Start();
		startGate.Set();
		try {
			source.WaitForFirstReloadBlocked();
			source.HasSecondReloadEnteredBuild.Should().BeFalse();
			source.MaxConcurrentReloadBuilds.Should().Be(1);
		}
		finally {
			source.ReleaseFirstReload();
			JoinThreads([firstReload, secondReload], "reload thread");
		}

		errors.Should().BeEmpty();
		source.MaxConcurrentReloadBuilds.Should().Be(1);
		factory.Current.TryEvaluate(student).Value!.Eligible.Should().BeFalse();
	}

	[Fact]
	public void joining_threads_applies_one_timeout_to_the_group()
	{
		const int groupTimeoutMilliseconds = 100;
		using var release = new ManualResetEventSlim(false);
		var threads = Enumerable.Range(0, 3)
			.Select(_ => new Thread(release.Wait))
			.ToArray();
		foreach (var thread in threads) {
			thread.Start();
		}

		var stopwatch = Stopwatch.StartNew();
		try {
			var act = () => JoinThreads(threads, "blocked thread", groupTimeoutMilliseconds);

			act.Should().Throw<TimeoutException>();
			stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(groupTimeoutMilliseconds * 2));
		}
		finally {
			release.Set();
			JoinThreads(threads, "cleanup thread", ThreadJoinTimeoutMilliseconds);
		}
	}

	[Fact]
	public void failed_reload_releases_the_gate_and_keeps_current_until_a_later_success()
	{
		using var source = ControlledReloadDataSource.WithReloadThresholds(
			Harness.WorkflowsDir,
			Harness.DataDir,
			false,
			InvalidThresholdsBytes(),
			ThresholdsBytes("min_passes", "4"));
		using var factory = EnrolmentEngineFactory.Create(source, Harness.AsOf);
		var before = factory.Current;
		var student = StudentForPassGradeBoundary();

		var act = () => factory.Reload();
		act.Should().Throw<PolicyThresholdsException>();
		factory.Current.Should().BeSameAs(before);
		factory.Current.TryEvaluate(student).Value!.Eligible.Should().BeTrue();

		factory.Reload();

		factory.Current.Should().NotBeSameAs(before);
		factory.Current.TryEvaluate(student).Value!.Eligible.Should().BeTrue();
		source.MaxConcurrentReloadBuilds.Should().Be(1);
	}

	private static StudentInput StudentForPassGradeBoundary() =>
		new(
			"S-RELOAD-BOUNDARY",
			new Dictionary<string, int> {
				["english_language"] = 7,
				["maths"] = 7,
				["physics"] = 7,
				["chemistry"] = 7,
				["biology"] = 7,
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

	private static void RaiseMinPasses(string thresholdsPath, int minPasses)
	{
		var lines = File.ReadAllLines(thresholdsPath);
		for (var i = 0; i < lines.Length; i++) {
			if (lines[i].StartsWith("min_passes:", StringComparison.Ordinal)) {
				lines[i] = $"min_passes: {minPasses}";
			}
		}

		File.WriteAllLines(thresholdsPath, lines);
	}

	private static byte[] ThresholdsBytes(string key, string value)
	{
		var lines = File.ReadAllLines(Path.Combine(Harness.DataDir, PolicyThresholdsStore.ThresholdsFileName));
		for (var i = 0; i < lines.Length; i++) {
			if (lines[i].StartsWith($"{key}:", StringComparison.Ordinal)) {
				lines[i] = $"{key}: {value}";
			}
		}

		return Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines));
	}

	private static byte[] InvalidThresholdsBytes() =>
		Encoding.UTF8.GetBytes("pass_grade: nope");

	private static void JoinThread(Thread thread, string description)
	{
		if (!thread.Join(ThreadJoinTimeoutMilliseconds)) {
			throw new TimeoutException($"{description} did not finish within {ThreadJoinTimeoutMilliseconds}ms.");
		}
	}

	private static void JoinThreads(
		IEnumerable<Thread> threads,
		string description,
		int timeoutMilliseconds = ThreadJoinTimeoutMilliseconds)
	{
		var stopwatch = Stopwatch.StartNew();
		var timedOut = threads.Count(thread => {
			var remaining = TimeSpan.FromMilliseconds(timeoutMilliseconds) - stopwatch.Elapsed;
			return !thread.Join(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
		});
		if (timedOut > 0) {
			throw new TimeoutException($"{timedOut} {description}(s) did not finish within {timeoutMilliseconds}ms.");
		}
	}

	private static void WaitForSignal(ManualResetEventSlim signal, string description)
	{
		if (!signal.Wait(SignalWaitTimeoutMilliseconds)) {
			throw new TimeoutException($"{description} was not signalled within {SignalWaitTimeoutMilliseconds}ms.");
		}
	}

	private sealed class ControlledReloadDataSource : IEnrolmentDataSource, IDisposable
	{
		private readonly bool blockFirstReload;
		private readonly byte[] catalogue;
		private readonly byte[] catalogueSchema;
		private readonly byte[] qualifications;
		private readonly byte[] qualificationsSchema;
		private readonly ManualResetEventSlim releaseFirstReloadSignal = new(false);
		private readonly Queue<byte[]> reloadThresholds;
		private readonly byte[] shippedThresholds;
		private readonly byte[] thresholdsSchema;
		private readonly byte[] transitionMatrix;
		private readonly IReadOnlyList<(string FileName, byte[] Bytes)> workflows;
		private readonly byte[] workflowSchema;
		private readonly ManualResetEventSlim firstReloadBlockedSignal = new(false);
		private int activeReloadBuilds;
		private int thresholdsOpenCount;

		private ControlledReloadDataSource(
			IReadOnlyList<(string FileName, byte[] Bytes)> workflows,
			byte[] workflowSchema,
			byte[] catalogue,
			byte[] catalogueSchema,
			byte[] qualifications,
			byte[] qualificationsSchema,
			byte[] shippedThresholds,
			Queue<byte[]> reloadThresholds,
			byte[] thresholdsSchema,
			byte[] transitionMatrix,
			bool blockFirstReload)
		{
			this.workflows = workflows;
			this.workflowSchema = workflowSchema;
			this.catalogue = catalogue;
			this.catalogueSchema = catalogueSchema;
			this.qualifications = qualifications;
			this.qualificationsSchema = qualificationsSchema;
			this.shippedThresholds = shippedThresholds;
			this.reloadThresholds = reloadThresholds;
			this.thresholdsSchema = thresholdsSchema;
			this.transitionMatrix = transitionMatrix;
			this.blockFirstReload = blockFirstReload;
		}

		public bool IsFirstReloadBlocked { get; private set; }
		public bool HasSecondReloadEnteredBuild { get; private set; }
		public int MaxConcurrentReloadBuilds { get; private set; }

		public void Dispose()
		{
			firstReloadBlockedSignal.Dispose();
			releaseFirstReloadSignal.Dispose();
		}

		public IReadOnlyList<WorkflowContent> OpenWorkflows() =>
			[.. workflows.Select(static workflow => new WorkflowContent(workflow.FileName, new MemoryStream(workflow.Bytes, false)))];

		public Stream OpenWorkflowSchema() => new MemoryStream(workflowSchema, false);

		public Stream OpenCatalogue() => new MemoryStream(catalogue, false);

		public Stream OpenCatalogueSchema() => new MemoryStream(catalogueSchema, false);

		public Stream OpenQualifications() => new MemoryStream(qualifications, false);

		public Stream OpenQualificationsSchema() => new MemoryStream(qualificationsSchema, false);

		public Stream OpenThresholds()
		{
			var openNumber = Interlocked.Increment(ref thresholdsOpenCount);
			if (openNumber == 1) {
				return new MemoryStream(shippedThresholds, false);
			}

			var active = Interlocked.Increment(ref activeReloadBuilds);
			MaxConcurrentReloadBuilds = Math.Max(MaxConcurrentReloadBuilds, active);
			try {
				if (openNumber == 2 && blockFirstReload) {
					IsFirstReloadBlocked = true;
					firstReloadBlockedSignal.Set();
					WaitForSignal(releaseFirstReloadSignal, "first reload release signal");
				} else if (openNumber == 3) {
					HasSecondReloadEnteredBuild = true;
				}

				return new MemoryStream(reloadThresholds.Dequeue(), false);
			}
			finally {
				_ = Interlocked.Decrement(ref activeReloadBuilds);
			}
		}

		public Stream OpenThresholdsSchema() => new MemoryStream(thresholdsSchema, false);

		public Stream OpenTransitionMatrix() => new MemoryStream(transitionMatrix, false);

		public void ReleaseFirstReload() => releaseFirstReloadSignal.Set();

		public void WaitForFirstReloadBlocked() => WaitForSignal(firstReloadBlockedSignal, "first reload blocked signal");

		public static ControlledReloadDataSource WithReloadThresholds(
			string workflowsDirectory,
			string dataDirectory,
			bool blockFirstReload,
			params byte[][] reloadThresholds)
		{
			var workflows = Directory.EnumerateFiles(workflowsDirectory)
				.Where(file => file != Path.Combine(workflowsDirectory, WorkflowStore.SchemaFileName))
				.OrderBy(static file => file, StringComparer.Ordinal)
				.Select(static file => (Path.GetFileName(file), File.ReadAllBytes(file)))
				.ToArray();

			return new(
				workflows,
				File.ReadAllBytes(Path.Combine(workflowsDirectory, WorkflowStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, CatalogueStore.CatalogueFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, CatalogueStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, QualificationScaleStore.QualificationsFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, QualificationScaleStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, PolicyThresholdsStore.ThresholdsFileName)),
				new(reloadThresholds),
				File.ReadAllBytes(Path.Combine(dataDirectory, PolicyThresholdsStore.SchemaFileName)),
				File.ReadAllBytes(Path.Combine(dataDirectory, DfeTransitionMatrix.DataDirectoryRelativePath)),
				blockFirstReload);
		}
	}
}
