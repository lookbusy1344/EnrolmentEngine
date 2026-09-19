namespace EnrolmentRules.Tests;

using System.Text.Json.Nodes;
using AwesomeAssertions;

/// <summary>
///     The shared JSON-Schema validation seam the four data loaders delegate to: it evaluates a document,
///     surfaces the instance-located error detail through a caller-supplied exception, and compiles each
///     distinct schema text once.
/// </summary>
public sealed class SchemaValidatorTests
{
	private const string Schema = """
								  {"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}
								  """;

	[Fact]
	public void a_valid_document_passes()
	{
		var act = () => SchemaValidator.Validate(
			JsonNode.Parse("""{"name":"ok"}""")!, Schema, static errors => new FixtureException(errors));

		act.Should().NotThrow();
	}

	[Fact]
	public void an_invalid_document_throws_the_supplied_exception_with_the_instance_location()
	{
		var act = () => SchemaValidator.Validate(
			JsonNode.Parse("""{"name":42}""")!, Schema, static errors => new FixtureException(errors));

		act.Should().Throw<FixtureException>().WithMessage("*/name*");
	}

	[Fact]
	public void the_same_schema_text_compiles_once()
	{
		var first = SchemaValidator.Compile(Schema);
		var second = SchemaValidator.Compile(Schema);

		second.Should().BeSameAs(first);
	}

	private sealed class FixtureException(string message) : Exception(message);
}
