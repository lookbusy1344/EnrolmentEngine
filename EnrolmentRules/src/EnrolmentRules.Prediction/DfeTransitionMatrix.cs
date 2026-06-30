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
	private const int ExpectedFieldCount = 12;
	private const double ProbabilityTotalTolerance = 1e-9;
	private const string ExpectedHeader =
		"subject,dfe_qualification_number,dfe_subject_number,dfe_subject_name,prior_attainment_band,probability_u,probability_e,probability_d,probability_c,probability_b,probability_a,probability_a_star";

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
	public static DfeTransitionMatrix Load(string path)
	{
		using var reader = File.OpenText(path);
		return Load(reader);
	}

	/// <summary>Load a normalized DfE transition-matrix CSV from an arbitrary text reader.</summary>
	public static DfeTransitionMatrix Load(TextReader reader)
	{
		ArgumentNullException.ThrowIfNull(reader);

		var header = reader.ReadLine()
			?? throw new TransitionMatrixException("DfE transition matrix is missing the required header row.");
		ValidateHeader(header);

		var evidence = new List<TransitionEvidence>();
		var seen = new HashSet<(Subject Subject, string Band)>();
		var dataRowCount = 0;

		while (reader.ReadLine() is { } line) {
			if (string.IsNullOrWhiteSpace(line)) {
				continue;
			}

			dataRowCount++;
			var rowNumber = dataRowCount + 1;
			var parsed = Parse(line, rowNumber);
			if (!seen.Add((parsed.Subject, parsed.PriorAttainmentBand))) {
				throw new TransitionMatrixException(
					$"DfE transition matrix row {rowNumber} duplicates subject '{EnumNames.NameOf(parsed.Subject)}' and prior attainment band '{parsed.PriorAttainmentBand}'.");
			}

			evidence.Add(parsed);
		}

		if (dataRowCount == 0) {
			throw new TransitionMatrixException("DfE transition matrix must contain at least one data row.");
		}

		return new(evidence);
	}

	/// <summary>Load a normalized DfE transition-matrix CSV from an arbitrary stream.</summary>
	public static DfeTransitionMatrix Load(Stream stream)
	{
		using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
		return Load(reader);
	}

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

	private static void ValidateHeader(string header)
	{
		if (!string.Equals(header, ExpectedHeader, StringComparison.Ordinal)) {
			throw new TransitionMatrixException("DfE transition matrix header does not match the normalized CSV contract.");
		}
	}

	private static TransitionEvidence Parse(string line, int rowNumber)
	{
		var fields = line.Split(',');
		if (fields.Length != ExpectedFieldCount) {
			throw new TransitionMatrixException(
				$"DfE transition matrix row {rowNumber} must contain exactly {ExpectedFieldCount} fields.");
		}

		if (!Subject.TryParse(fields[0], out var subject)) {
			throw new TransitionMatrixException($"DfE transition matrix row {rowNumber} has an unknown subject '{fields[0]}'.");
		}

		var band = fields[4];
		if (!Bands.Contains(band, StringComparer.Ordinal)) {
			throw new TransitionMatrixException(
				$"DfE transition matrix row {rowNumber} has an unknown prior_attainment_band '{band}'.");
		}

		var probabilities = new[] {
			Probability(fields[5], rowNumber, "probability_u"),
			Probability(fields[6], rowNumber, "probability_e"),
			Probability(fields[7], rowNumber, "probability_d"),
			Probability(fields[8], rowNumber, "probability_c"),
			Probability(fields[9], rowNumber, "probability_b"),
			Probability(fields[10], rowNumber, "probability_a"),
			Probability(fields[11], rowNumber, "probability_a_star"),
		};

		var total = probabilities.Sum();
		if (Math.Abs(total - 1.0) > ProbabilityTotalTolerance) {
			throw new TransitionMatrixException(
				$"DfE transition matrix row {rowNumber} probabilities must sum to 1 within tolerance {ProbabilityTotalTolerance}.");
		}

		return new(
			subject,
			Source,
			band,
			probabilities[0],
			probabilities[1],
			probabilities[2],
			probabilities[3],
			probabilities[4],
			probabilities[5],
			probabilities[6]);
	}

	private static double Probability(string field, int rowNumber, string columnName)
	{
		if (string.IsNullOrWhiteSpace(field)) {
			return 0.0;
		}

		try {
			if (!double.TryParse(field, CultureInfo.InvariantCulture, out var probability)) {
				throw new FormatException($"Could not parse '{field}' as a floating-point value.");
			}

			if (double.IsNaN(probability) || double.IsInfinity(probability)) {
				throw new TransitionMatrixException(
					$"DfE transition matrix row {rowNumber} column '{columnName}' must be finite.");
			}

			if (probability is < 0.0 or > 1.0) {
				throw new TransitionMatrixException(
					$"DfE transition matrix row {rowNumber} column '{columnName}' must be within [0, 1].");
			}

			return probability;
		}
		catch (TransitionMatrixException) {
			throw;
		}
		catch (FormatException ex) {
			throw new TransitionMatrixException(
				$"DfE transition matrix row {rowNumber} column '{columnName}' contains an invalid probability value.",
				ex);
		}
	}

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

/// <summary>
///     Startup-data failure while loading the DfE transition matrix. Hosts can catch the shared
///     <see cref="EnrolmentDataException" /> category across all runtime-loaded policy inputs.
/// </summary>
public sealed class TransitionMatrixException : EnrolmentDataException
{
	public TransitionMatrixException(string message) : base(message) { }

	public TransitionMatrixException(string message, Exception innerException) : base(message, innerException) { }
}
