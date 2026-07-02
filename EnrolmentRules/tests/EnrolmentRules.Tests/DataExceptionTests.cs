namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;
using Engine;
using Prediction;

/// <summary>Locks the shared startup-data exception hierarchy exposed by the packable surface.</summary>
public sealed class DataExceptionTests
{
	[Fact]
	public void policy_thresholds_store_maps_malformed_yaml_to_its_documented_exception_type()
	{
		var schema = File.ReadAllText(Path.Combine(Harness.DataDir, PolicyThresholdsStore.SchemaFileName));

		var act = () => PolicyThresholdsStore.LoadAndValidate(
			new StringReader("pass_grade: ["),
			new StringReader(schema),
			"malformed-thresholds.yaml");

		act.Should().Throw<PolicyThresholdsException>()
			.WithMessage("*malformed-thresholds.yaml*")
			.Which.InnerException.Should().BeOfType<FormatException>();
	}

	[Fact]
	public void startup_data_exceptions_share_a_common_base()
	{
		typeof(CatalogueException).BaseType.Should().Be<EnrolmentDataException>();
		typeof(QualificationScaleException).BaseType.Should().Be<EnrolmentDataException>();
		typeof(PolicyThresholdsException).BaseType.Should().Be<EnrolmentDataException>();
		typeof(TransitionMatrixException).BaseType.Should().Be<EnrolmentDataException>();
	}
}
