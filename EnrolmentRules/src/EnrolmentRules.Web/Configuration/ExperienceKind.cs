namespace EnrolmentRules.Web.Configuration;

/// <summary>The workflow experience served at <c>/</c> until a caller asks for one explicitly via <c>/razor</c> or <c>/app</c>.</summary>
public enum ExperienceKind
{
	Razor,
	Vue,
}
