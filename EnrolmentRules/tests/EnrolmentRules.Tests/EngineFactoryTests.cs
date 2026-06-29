namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;

/// <summary>Reloadable engine factory for policy edits without process restart.</summary>
public sealed class EngineFactoryTests
{
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
	public async Task reload_picks_up_threshold_changes_from_disk()
	{
		var fixture = CopyShippedLayout();
		try {
			var factory = await EnrolmentEngineFactory.CreateAsync(
				Path.Combine(fixture, "workflows"),
				Path.Combine(fixture, "data"),
				Harness.AsOf);
			var student = EligibleStudent();

			(await factory.Current.TryEvaluateAsync(student)).Value!.Eligible.Should().BeTrue();

			RaisePassGrade(Path.Combine(fixture, "data", "thresholds.yaml"), 7);
			await factory.ReloadAsync();

			(await factory.Current.TryEvaluateAsync(student)).Value!.Eligible.Should().BeFalse();
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public async Task reload_leaves_current_unchanged_when_bootstrap_fails()
	{
		var fixture = CopyShippedLayout();
		try {
			var factory = await EnrolmentEngineFactory.CreateAsync(
				Path.Combine(fixture, "workflows"),
				Path.Combine(fixture, "data"),
				Harness.AsOf);
			var before = factory.Current;

			File.WriteAllText(Path.Combine(fixture, "workflows", "eligibility.yaml"), "not: valid");

			var act = () => factory.ReloadAsync();
			await act.Should().ThrowAsync<WorkflowException>();
			factory.Current.Should().BeSameAs(before);
		}
		finally {
			Directory.Delete(fixture, true);
		}
	}

	[Fact]
	public async Task concurrent_evaluations_during_reload_do_not_throw()
	{
		var factory = await EnrolmentEngineFactory.CreateAsync(Harness.WorkflowsDir, Harness.DataDir, Harness.AsOf);
		var student = EligibleStudent();
		var reloads = Task.Run(async () => {
			for (var i = 0; i < 5; i++) {
				await factory.ReloadAsync();
			}
		});
		var evaluations = Enumerable.Range(0, 50)
			.Select(_ => factory.Current.TryEvaluateAsync(student))
			.ToArray();

		await Task.WhenAll(reloads, Task.WhenAll(evaluations));
		evaluations.Select(task => task.Result).Should().OnlyContain(outcome => outcome.Validation.IsValid);
	}

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
}
