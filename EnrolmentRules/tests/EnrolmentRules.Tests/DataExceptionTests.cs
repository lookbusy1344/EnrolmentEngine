namespace EnrolmentRules.Tests;

using Domain;
using Engine;
using FluentAssertions;
using Prediction;

/// <summary>Locks the shared startup-data exception hierarchy exposed by the packable surface.</summary>
public sealed class DataExceptionTests
{
	[Fact]
	public void startup_data_exceptions_share_a_common_base()
	{
		typeof(CatalogueException).BaseType.Should().Be<EnrolmentDataException>();
		typeof(QualificationScaleException).BaseType.Should().Be<EnrolmentDataException>();
		typeof(PolicyThresholdsException).BaseType.Should().Be<EnrolmentDataException>();
		typeof(TransitionMatrixException).BaseType.Should().Be<EnrolmentDataException>();
	}
}
