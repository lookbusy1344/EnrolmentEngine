namespace EnrolmentRules.Domain;

/// <summary>
///     A constructed catalogue's runtime invariants were violated: the subject list and metadata table do
///     not describe the same catalogue, or a caller asked for metadata the bound catalogue does not hold.
/// </summary>
public sealed class CatalogueDataException : EnrolmentDataException
{
	public CatalogueDataException() { }

	public CatalogueDataException(string message) : base(message) { }

	public CatalogueDataException(string message, Exception innerException) : base(message, innerException) { }
}
