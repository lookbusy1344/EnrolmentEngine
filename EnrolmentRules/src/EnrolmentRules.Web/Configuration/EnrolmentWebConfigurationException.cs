namespace EnrolmentRules.Web.Configuration;

/// <summary>Thrown at startup when <c>EnrolmentWeb</c> configuration cannot be loaded into a valid <see cref="EnrolmentWebOptions" />.</summary>
public sealed class EnrolmentWebConfigurationException : Exception
{
	public EnrolmentWebConfigurationException() { }

	public EnrolmentWebConfigurationException(string message) : base(message) { }

	public EnrolmentWebConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
