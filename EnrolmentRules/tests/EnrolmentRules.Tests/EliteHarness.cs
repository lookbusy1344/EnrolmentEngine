namespace EnrolmentRules.Tests;

/// <summary>
///     Builds the Elite auxiliary policy engine over the <em>real</em> shipped
///     <c>policies/elite/</c> assets layered via <see cref="OverlayEnrolmentDataSource" /> onto the shared
///     Standard base source, mirroring <see cref="Harness" /> for the Standard policy. Memoised for the
///     whole suite for the same reason: startup (schema validation + probe compilation) is identical on
///     every call and the engine is read-only after build.
/// </summary>
internal static class EliteHarness
{
	private static readonly Lazy<EnrolmentEngine> Engine = new(BuildEngine, LazyThreadSafetyMode.ExecutionAndPublication);

	public static string WorkflowsDir => Path.Combine(Harness.RepoRoot, "policies", "elite", "workflows");

	public static string DataDir => Path.Combine(Harness.RepoRoot, "policies", "elite", "data");

	/// <summary>The fully bootstrapped Elite engine (schema-validated, probe-compiled, lint-clean).</summary>
	public static EnrolmentEngine ShippedEngine() => Engine.Value;

	/// <summary>
	///     A GCSE grade set that clears every Elite eligibility rule and every offered subject's amber
	///     tier — one cognate GCSE per offered subject, except Further Mathematics (shares "maths") and
	///     Religious Studies (shares "history", its configured related discipline). Eleven distinct keys:
	///     unsuitable for eligibility count/total/average <em>boundary</em> tests, which need an exact,
	///     controlled cardinality — see <see cref="EightGcsesAtGrade" /> for those.
	/// </summary>
	public static Dictionary<string, int> AllOfferedGcsesAtGrade(int grade) => new(StringComparer.Ordinal) {
		["english_language"] = grade,
		["english_literature"] = grade,
		["maths"] = grade,
		["biology"] = grade,
		["chemistry"] = grade,
		["history"] = grade,
		["physics"] = grade,
		["psychology"] = grade,
		["french"] = grade,
		["geography"] = grade,
		["politics"] = grade,
	};

	/// <summary>
	///     Exactly eight GCSEs at one uniform grade — the minimum "at least eight GCSEs" cardinality, so
	///     eligibility count/best-eight-total/top-seven-average boundary tests can reason about an exact
	///     sum instead of "the best 8 of however many were submitted".
	/// </summary>
	public static Dictionary<string, int> EightGcsesAtGrade(int grade) => new(StringComparer.Ordinal) {
		["english_language"] = grade,
		["maths"] = grade,
		["biology"] = grade,
		["chemistry"] = grade,
		["history"] = grade,
		["physics"] = grade,
		["psychology"] = grade,
		["french"] = grade,
	};

	private static EnrolmentEngine BuildEngine()
	{
		var source = new OverlayEnrolmentDataSource(
			new DirectoryDataSource(WorkflowsDir, DataDir),
			new DirectoryDataSource(Harness.WorkflowsDir, Harness.DataDir));
		return EnrolmentEngine.Create(source, Harness.AsOf);
	}
}
