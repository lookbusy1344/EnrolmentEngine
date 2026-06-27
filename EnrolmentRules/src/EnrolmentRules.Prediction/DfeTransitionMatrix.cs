namespace EnrolmentRules.Prediction;

using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Domain;

/// <summary>
///     Local projection of the official DfE 16-to-18 transition matrices. The committed CSV is a
///     normalized extract of GCE A-level percentage rows for the subjects modelled by this project.
/// </summary>
public sealed class DfeTransitionMatrix
{
	public const string Source = "DfE 16 to 18 transition matrices 2019";

	/// <summary>The matrix CSV's location relative to a <c>data/</c> directory.</summary>
	public const string DataDirectoryRelativePath = "dfe-transition-matrices/gce-a-level-2019-transition-probabilities.csv";

	private const string DefaultRelativePath = "data/" + DataDirectoryRelativePath;

	private static readonly Lazy<DfeTransitionMatrix> Default = new(static () => Load(FindDefaultCsvPath()));

	private static readonly string[] Bands = [
		"< 1",
		"1 to < 2",
		"2 to < 3",
		"3 to < 4",
		"4 to < 5",
		"5 to < 6",
		"6 to < 7",
		"7 to < 8",
		"8 to < 9",
		">=9",
	];

	private readonly FrozenDictionary<Subject, FrozenSet<string>> populatedBands;

	private readonly FrozenDictionary<(Subject Subject, string Band), TransitionEvidence> rows;

	private DfeTransitionMatrix(IEnumerable<TransitionEvidence> evidence)
	{
		var materialized = evidence.ToArray();
		rows = materialized.ToFrozenDictionary(static e => (e.Subject, e.PriorAttainmentBand));
		populatedBands = materialized
			.GroupBy(static e => e.Subject)
			.ToFrozenDictionary(static grp => grp.Key, static grp => grp.Select(static e => e.PriorAttainmentBand).ToFrozenSet());
	}

	/// <summary>Load the project-local DfE transition-matrix extract (the zero-wiring fallback path).</summary>
	public static DfeTransitionMatrix LoadDefault() => Default.Value;

	/// <summary>Load the matrix from the CSV under an explicit <c>data/</c> directory, mirroring the catalogue and thresholds loaders.</summary>
	public static DfeTransitionMatrix LoadFromDataDirectory(string dataDirectory) =>
		Load(Path.Combine(dataDirectory, DataDirectoryRelativePath));

	/// <summary>Load a normalized DfE transition-matrix CSV.</summary>
	public static DfeTransitionMatrix Load(string path) =>
		new(File.ReadLines(path).Skip(1).Where(static line => !string.IsNullOrWhiteSpace(line)).Select(Parse));

	/// <summary>Load a normalized DfE transition-matrix CSV from an arbitrary text reader.</summary>
	public static DfeTransitionMatrix Load(TextReader reader) =>
		new(ReadLines(reader).Skip(1).Where(static line => !string.IsNullOrWhiteSpace(line)).Select(Parse));

	/// <summary>Load a normalized DfE transition-matrix CSV from an arbitrary stream.</summary>
	public static DfeTransitionMatrix Load(Stream stream)
	{
		using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
		return Load(reader);
	}

	/// <summary>Return one transition-evidence row per modelled subject for <paramref name="averageGcseScore" />.</summary>
	public IReadOnlyList<TransitionEvidence> EvidenceFor(double averageGcseScore)
		=> EvidenceFor(averageGcseScore, Catalogue.Current);

	/// <summary>Return one transition-evidence row per subject in <paramref name="catalogue" />.</summary>
	public IReadOnlyList<TransitionEvidence> EvidenceFor(double averageGcseScore, CatalogueData catalogue)
	{
		var band = PriorAttainmentBand(averageGcseScore);
		return [
			.. catalogue.Subjects.Select(subject =>
				FindEvidence(subject, band)),
		];
	}

	private TransitionEvidence FindEvidence(Subject subject, string band)
	{
		if (rows.TryGetValue((subject, band), out var exact)) {
			return exact;
		}

		if (!populatedBands.TryGetValue(subject, out var subjectBands)) {
			return Empty(subject, band);
		}

		var bandIndex = Array.IndexOf(Bands, band);
		for (var i = bandIndex - 1; i >= 0; i--) {
			if (subjectBands.Contains(Bands[i])) {
				return rows[(subject, Bands[i])];
			}
		}

		return Empty(subject, band);
	}

	private static TransitionEvidence Parse(string line)
	{
		var fields = line.Split(',');
		if (fields.Length != 12 || !Subject.TryParse(fields[0], out var subject)) {
			throw new InvalidDataException("DfE transition matrix row does not match the normalized CSV contract.");
		}

		return new(
			subject,
			Source,
			fields[4],
			Probability(fields[5]),
			Probability(fields[6]),
			Probability(fields[7]),
			Probability(fields[8]),
			Probability(fields[9]),
			Probability(fields[10]),
			Probability(fields[11]));
	}

	private static IEnumerable<string> ReadLines(TextReader reader)
	{
		while (reader.ReadLine() is { } line) {
			yield return line;
		}
	}

	private static double Probability(string field) =>
		string.IsNullOrWhiteSpace(field) ? 0.0 : double.Parse(field, CultureInfo.InvariantCulture);

	private static TransitionEvidence Empty(Subject subject, string band) =>
		new(subject, Source, band, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

	private static string PriorAttainmentBand(double averageGcseScore) => averageGcseScore switch {
		< 1.0 => "< 1",
		< 2.0 => "1 to < 2",
		< 3.0 => "2 to < 3",
		< 4.0 => "3 to < 4",
		< 5.0 => "4 to < 5",
		< 6.0 => "5 to < 6",
		< 7.0 => "6 to < 7",
		< 8.0 => "7 to < 8",
		< 9.0 => "8 to < 9",
		_ => ">=9",
	};

	private static string FindDefaultCsvPath()
	{
		var bundled = Path.Combine(AppContext.BaseDirectory, DefaultRelativePath);
		if (File.Exists(bundled)) {
			return bundled;
		}

		var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
		foreach (var start in starts) {
			for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent) {
				var candidate = Path.Combine(dir.FullName, DefaultRelativePath);
				if (File.Exists(candidate)) {
					return candidate;
				}
			}
		}

		throw new FileNotFoundException($"Could not locate '{DefaultRelativePath}'.");
	}
}
