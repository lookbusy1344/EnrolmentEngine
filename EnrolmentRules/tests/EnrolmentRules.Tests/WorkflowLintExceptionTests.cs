namespace EnrolmentRules.Tests;

using AwesomeAssertions;
using Domain;

/// <summary>
///     <see cref="WorkflowLintException" /> carries structured lint findings, but must still honour the
///     standard exception constructor triad (FDG ch. 7) so it composes like the rest of the hierarchy. The
///     findings-carrying constructor stays the production path; the triad constructors leave
///     <see cref="WorkflowLintException.Findings" /> empty rather than null.
/// </summary>
public sealed class WorkflowLintExceptionTests
{
	[Fact]
	public void parameterless_constructor_yields_no_findings()
	{
		var exception = new WorkflowLintException();

		exception.Findings.Should().BeEmpty();
		exception.InnerException.Should().BeNull();
	}

	[Fact]
	public void message_constructor_preserves_the_message()
	{
		var exception = new WorkflowLintException("lint failed");

		exception.Message.Should().Be("lint failed");
		exception.Findings.Should().BeEmpty();
	}

	[Fact]
	public void message_and_inner_constructor_preserve_both()
	{
		var inner = new InvalidOperationException("root cause");

		var exception = new WorkflowLintException("lint failed", inner);

		exception.Message.Should().Be("lint failed");
		exception.InnerException.Should().BeSameAs(inner);
		exception.Findings.Should().BeEmpty();
	}

	[Fact]
	public void findings_constructor_populates_findings_and_composes_the_message()
	{
		var findings = new[] {
			new LintFinding("eligibility", "gate", LintSeverity.Error, "bad expression"),
		};

		var exception = new WorkflowLintException(findings);

		exception.Findings.Should().BeEquivalentTo(findings);
		exception.Message.Should().Contain("eligibility/gate: bad expression");
	}
}
