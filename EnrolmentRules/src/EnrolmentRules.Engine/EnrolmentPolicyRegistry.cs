namespace EnrolmentRules.Engine;

using Domain;
using Prediction;

/// <summary>
///     Immutable, thread-safe implementation of <see cref="IEnrolmentPolicyRegistry" />. Every definition's
///     engine is built eagerly at construction (never lazily on first request), so a broken auxiliary
///     policy fails startup rather than appearing in the selector and failing on its first student.
///     Construction validates the definition set itself before building anything, so a configuration
///     mistake (duplicate id, blank name, unknown default) fails before the — potentially expensive —
///     engine builds run.
/// </summary>
public sealed class EnrolmentPolicyRegistry : IEnrolmentPolicyRegistry
{
	private readonly IReadOnlyDictionary<EnrolmentPolicyId, EnrolmentPolicy> byId;

	public EnrolmentPolicyRegistry(
		IReadOnlyList<EnrolmentPolicyDefinition> definitions,
		EnrolmentPolicyId defaultPolicyId,
		Func<DateOnly> asOf,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(definitions);
		ArgumentNullException.ThrowIfNull(asOf);
		ValidateDefinitions(definitions, defaultPolicyId);

		var built = new Dictionary<EnrolmentPolicyId, EnrolmentPolicy>();
		var descriptors = new List<EnrolmentPolicyDescriptor>();
		foreach (var definition in definitions) {
			cancellationToken.ThrowIfCancellationRequested();
			var engine = Build(definition, asOf, cancellationToken);
			var descriptor = new EnrolmentPolicyDescriptor(definition.Id, definition.DisplayName);
			descriptors.Add(descriptor);
			built[definition.Id] = new(descriptor, engine);
		}

		Descriptors = Array.AsReadOnly(descriptors.ToArray());
		DefaultPolicyId = defaultPolicyId;
		byId = built;
	}

	/// <inheritdoc />
	public IReadOnlyList<EnrolmentPolicyDescriptor> Descriptors { get; }

	/// <inheritdoc />
	public EnrolmentPolicyId DefaultPolicyId { get; }

	/// <inheritdoc />
	public EnrolmentPolicy GetPolicy(EnrolmentPolicyId id) =>
		byId.TryGetValue(id, out var policy)
			? policy
			: throw new UnknownEnrolmentPolicyException(id, [.. byId.Keys.OrderBy(static known => known)]);

	/// <inheritdoc />
	public bool TryGetPolicy(EnrolmentPolicyId id, out EnrolmentPolicy policy) => byId.TryGetValue(id, out policy!);

	/// <inheritdoc />
	public ValidatedEvaluation<PolicyComparisonResult> Compare(
		EnrolmentPolicyId id, StudentInput student, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(student);
		var policy = GetPolicy(id);

		IEnrolmentEvaluator evaluator = policy.Engine;
		var factsErrors = StudentValidator.ValidateFacts(student, evaluator.Scale);
		if (factsErrors.Count > 0) {
			return new(new([.. factsErrors]), null);
		}

		// Partition NotOffered choices before calling the engine: the unchecked Explain path evaluates
		// every chosen subject it is given, and a subject absent from this policy's catalogue has no
		// rating to explain at all — it must never reach the engine.
		var offeredCatalogue = evaluator.Catalogue.Subjects;
		var notOffered = student.ChosenALevels.Where(subject => !offeredCatalogue.Contains(subject)).ToHashSet();
		cancellationToken.ThrowIfCancellationRequested();

		var comparisonSubject = notOffered.Count == 0
			? student
			: student with {
				ChosenALevels = EquatableArray.CopyOf([.. student.ChosenALevels.Where(subject => !notOffered.Contains(subject))]),
			};

		var explanation = policy.Engine.Explain(comparisonSubject, cancellationToken);
		var ratingBySubject = explanation.Explanations.ToDictionary(static e => e.Subject, static e => e);

		var statuses = student.ChosenALevels.Select(subject => ChosenStatus(subject, notOffered, ratingBySubject));

		var averageGcseScore = GradePredictor.AverageGcseScore(student.ToGcseResults());
		var result = new PolicyComparisonResult(
			policy.Descriptor,
			explanation,
			[.. statuses],
			evaluator.Thresholds.MinChosenALevels,
			Aggregator.EffectiveMaxChosenALevels(averageGcseScore, evaluator.Thresholds));

		return new(ValidationOutcome.Valid, result);
	}

	private static ChosenSubjectStatus ChosenStatus(
		Subject subject, HashSet<Subject> notOffered, Dictionary<Subject, Explanation> ratingBySubject)
	{
		if (notOffered.Contains(subject)) {
			return new(subject, ChoiceStatus.NotOffered, null);
		}

		var explanation = ratingBySubject[subject];
		return explanation.Rating == Rating.Red
			? new(subject, ChoiceStatus.Unavailable, explanation.Reason)
			: new(subject, ChoiceStatus.Available, null);
	}

	private static EnrolmentEngine Build(EnrolmentPolicyDefinition definition, Func<DateOnly> asOf, CancellationToken cancellationToken)
	{
		try {
			return EnrolmentEngine.Create(definition.Source, asOf, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			// Deliberately broad: a missing asset (IOException), a malformed one (WorkflowException,
			// EnrolmentDataException) and anything else the bootstrap recipe can throw must all be
			// attributed to the policy that was building when they happened, not surface as a bare,
			// policy-anonymous exception.
			throw new EnrolmentPolicyBuildException(definition.Id, ex);
		}
	}

	private static void ValidateDefinitions(IReadOnlyList<EnrolmentPolicyDefinition> definitions, EnrolmentPolicyId defaultPolicyId)
	{
		if (definitions.Count == 0) {
			throw new EnrolmentPolicyConfigurationException("At least one policy definition is required.");
		}

		var ids = new HashSet<EnrolmentPolicyId>();
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var definition in definitions) {
			if (!ids.Add(definition.Id)) {
				throw new EnrolmentPolicyConfigurationException($"Duplicate policy identifier '{definition.Id}'.");
			}

			if (string.IsNullOrWhiteSpace(definition.DisplayName)) {
				throw new EnrolmentPolicyConfigurationException($"Policy '{definition.Id}' has a blank display name.");
			}

			if (!names.Add(definition.DisplayName)) {
				throw new EnrolmentPolicyConfigurationException($"Duplicate policy display name '{definition.DisplayName}'.");
			}

			ArgumentNullException.ThrowIfNull(definition.Source);
		}

		if (!ids.Contains(defaultPolicyId)) {
			throw new EnrolmentPolicyConfigurationException(
				$"Default policy '{defaultPolicyId}' is not among the registered definitions.");
		}
	}
}
