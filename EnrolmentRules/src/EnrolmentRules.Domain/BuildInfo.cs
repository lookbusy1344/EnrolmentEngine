namespace EnrolmentRules.Domain;

using System.Reflection;

/// <summary>
///     The build provenance of the running binaries — the assembly version and the git commit it was
///     built from — so a CLI user or a web visitor can say which build produced a decision. The commit
///     is stamped at build time by the <c>StampGitCommit</c> target in <c>Directory.Build.props</c>, which
///     sets <c>SourceRevisionId</c>; the SDK folds that into
///     <see cref="AssemblyInformationalVersionAttribute" /> as the <c>+metadata</c> suffix. Every project
///     in the solution builds from one checkout, so this assembly's stamp speaks for all of them.
/// </summary>
public static class BuildInfo
{
	/// <summary>Stands in for the commit when the build had no git checkout to read (e.g. a source drop).</summary>
	public const string UnknownCommit = "unknown";

	private const char MetadataSeparator = '+';

	/// <summary>The <c>SourceRevisionId</c> suffix marking a build from a working tree with uncommitted changes.</summary>
	private const string DirtySuffix = "-dirty";

	private static readonly string InformationalVersion =
		typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? typeof(BuildInfo).Assembly.GetName().Version?.ToString()
		?? "0.0.0";

	/// <summary>The assembly version, with any build metadata stripped — e.g. <c>0.1.0</c>.</summary>
	public static string Version { get; } = InformationalVersion.Split(MetadataSeparator)[0];

	private static string Revision { get; } = InformationalVersion.Split(MetadataSeparator) is [_, var revision, ..]
											  && !string.IsNullOrWhiteSpace(revision)
												  ? revision
												  : UnknownCommit;

	/// <summary>
	///     The abbreviated git commit the build came from — bare, so it is usable in a commit URL — or
	///     <see cref="UnknownCommit" /> when no commit was stamped.
	/// </summary>
	public static string Commit { get; } =
		Revision.EndsWith(DirtySuffix, StringComparison.Ordinal) ? Revision[..^DirtySuffix.Length] : Revision;

	/// <summary>Whether the build came from a working tree with uncommitted tracked changes.</summary>
	public static bool IsDirty { get; } = Revision.EndsWith(DirtySuffix, StringComparison.Ordinal);

	/// <summary>
	///     The full stamp — <c>{Version}+{Commit}</c>, plus <c>-dirty</c> for an uncommitted build — as shown
	///     by the CLI and the web footer.
	/// </summary>
	public static string VersionWithCommit { get; } = $"{Version}{MetadataSeparator}{Revision}";
}
