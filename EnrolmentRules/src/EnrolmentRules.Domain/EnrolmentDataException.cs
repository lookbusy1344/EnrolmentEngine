namespace EnrolmentRules.Domain;

/// <summary>Base type for startup data failures: the host can catch one category across the load path.</summary>
public abstract class EnrolmentDataException : Exception
{
	protected EnrolmentDataException() { }

	protected EnrolmentDataException(string message) : base(message) { }

	protected EnrolmentDataException(string message, Exception innerException) : base(message, innerException) { }
}
