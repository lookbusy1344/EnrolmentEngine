namespace EnrolmentRules.Web.Pages;

using Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

/// <summary>Redirects <c>/</c> to whichever experience <see cref="EnrolmentWebOptions.DefaultExperience" /> configures.</summary>
public sealed class IndexModel(EnrolmentWebOptions webOptions) : PageModel
{
	public IActionResult OnGet() => webOptions.DefaultExperience switch {
		ExperienceKind.Vue => Redirect("/app"),
		_ => Redirect("/razor"),
	};
}
