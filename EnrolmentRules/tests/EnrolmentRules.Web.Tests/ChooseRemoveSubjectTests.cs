namespace EnrolmentRules.Web.Tests;

using System.Globalization;
using AwesomeAssertions;

public sealed class ChooseRemoveSubjectTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory factory;

	public ChooseRemoveSubjectTests(WebAppFactory factory) => this.factory = factory;

	[Fact]
	public async Task Choosing_german_excludes_french_and_removing_it_restores_the_prior_rating()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);

		var form = EligibleStudentForm(token);

		using var saveContent = new FormUrlEncodedContent(form);
		using var saveResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), saveContent);
		using var afterSave = await client.GetAsync(saveResponse.Headers.Location);
		var htmlAfterSave = await afterSave.Content.ReadAsStringAsync();
		htmlAfterSave.Should().Contain("french").And.Contain("Green");

		var chooseToken = await ExtractAntiForgeryTokenAsync(afterSave);
		using var chooseContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = chooseToken,
			["subject"] = "german",
		});
		using var chooseResponse = await client.PostAsync(new Uri("/razor?handler=ChooseSubject", UriKind.Relative), chooseContent);
		using var afterChoose = await client.GetAsync(chooseResponse.Headers.Location);
		var htmlAfterChoose = await afterChoose.Content.ReadAsStringAsync();
		htmlAfterChoose.Should().Contain("Cannot be combined with chosen german");
		htmlAfterChoose.Should().NotContain("name=\"subject\" value=\"french\"");

		var removeToken = await ExtractAntiForgeryTokenAsync(afterChoose);
		using var removeContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = removeToken,
			["subject"] = "german",
		});
		using var removeResponse = await client.PostAsync(new Uri("/razor?handler=RemoveSubject", UriKind.Relative), removeContent);
		using var afterRemove = await client.GetAsync(removeResponse.Headers.Location);
		var htmlAfterRemove = await afterRemove.Content.ReadAsStringAsync();
		htmlAfterRemove.Should().NotContain("Cannot be combined with chosen german");
		htmlAfterRemove.Should().Contain("french").And.Contain("Green");
	}

	[Fact]
	public async Task Red_unchosen_subjects_are_not_selectable_by_markup_or_forged_post()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);
		using var saveContent = new FormUrlEncodedContent(EligibleStudentForm(token));
		using var saveResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), saveContent);
		using var afterSave = await client.GetAsync(saveResponse.Headers.Location);
		var htmlAfterSave = await afterSave.Content.ReadAsStringAsync();

		htmlAfterSave.Should().Contain("further_maths").And.Contain("Red");
		htmlAfterSave.Should().NotContain("name=\"subject\" value=\"further_maths\"");

		var chooseToken = await ExtractAntiForgeryTokenAsync(afterSave);
		using var forgedChooseContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = chooseToken,
			["subject"] = "further_maths",
		});
		using var forgedChooseResponse = await client.PostAsync(new Uri("/razor?handler=ChooseSubject", UriKind.Relative), forgedChooseContent);
		using var afterForgedChoose = await client.GetAsync(forgedChooseResponse.Headers.Location);
		var htmlAfterForgedChoose = await afterForgedChoose.Content.ReadAsStringAsync();

		htmlAfterForgedChoose.Should().Contain("None chosen yet.");
		htmlAfterForgedChoose.Should().NotContain("list-inline-item badge text-bg-primary rounded-pill\">Further Maths");
	}

	[Fact]
	public async Task Lowering_gcses_ejects_a_chosen_subject_that_is_no_longer_green()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);
		using var saveContent = new FormUrlEncodedContent(EligibleStudentForm(token));
		using var saveResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), saveContent);
		using var afterSave = await client.GetAsync(saveResponse.Headers.Location);

		// French is green on the strong grades, so it can be chosen.
		var chooseToken = await ExtractAntiForgeryTokenAsync(afterSave);
		using var chooseContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = chooseToken,
			["subject"] = "french",
		});
		using var chooseResponse = await client.PostAsync(new Uri("/razor?handler=ChooseSubject", UriKind.Relative), chooseContent);
		using var afterChoose = await client.GetAsync(chooseResponse.Headers.Location);
		var htmlAfterChoose = await afterChoose.Content.ReadAsStringAsync();
		htmlAfterChoose.Should().Contain("list-inline-item badge text-bg-primary rounded-pill\">French");

		// Now collapse every grade to a 1: the student fails the eligibility gate, so French goes red.
		var lowerToken = await ExtractAntiForgeryTokenAsync(afterChoose);
		using var lowerContent = new FormUrlEncodedContent(LoweredStudentForm(lowerToken));
		using var lowerResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), lowerContent);
		using var afterLower = await client.GetAsync(lowerResponse.Headers.Location);
		var htmlAfterLower = await afterLower.Content.ReadAsStringAsync();

		htmlAfterLower.Should().NotContain("list-inline-item badge text-bg-primary rounded-pill\">French");
		htmlAfterLower.Should().Contain("None chosen yet.");
		htmlAfterLower.Should().Contain("no longer available with your current grades");
		// The page still renders a verdict rather than the engine's refusal: the basket was pruned first.
		htmlAfterLower.Should().NotContain("chosen_a_levels");
	}

	[Fact]
	public async Task Normal_attainment_student_cannot_forge_a_fourth_choice_once_three_are_chosen()
	{
		using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

		using var getResponse = await client.GetAsync(new Uri("/razor", UriKind.Relative));
		var token = await ExtractAntiForgeryTokenAsync(getResponse);
		using var saveContent = new FormUrlEncodedContent(NormalAttainmentStudentForm(token));
		using var saveResponse = await client.PostAsync(new Uri("/razor?handler=SaveFacts", UriKind.Relative), saveContent);
		var currentLocation = saveResponse.Headers.Location;
		foreach (var subject in new[] { "chemistry", "biology", "history" }) {
			using var currentPage = await client.GetAsync(currentLocation);
			var chooseToken = await ExtractAntiForgeryTokenAsync(currentPage);
			using var chooseContent = new FormUrlEncodedContent(new Dictionary<string, string> {
				["__RequestVerificationToken"] = chooseToken,
				["subject"] = subject,
			});
			using var chooseResponse = await client.PostAsync(new Uri("/razor?handler=ChooseSubject", UriKind.Relative), chooseContent);
			currentLocation = chooseResponse.Headers.Location;
		}

		using var afterSave = await client.GetAsync(currentLocation);
		var htmlAfterThreeChoices = await afterSave.Content.ReadAsStringAsync();
		htmlAfterThreeChoices.Should().Contain("exceeds chosen subject cap");
		htmlAfterThreeChoices.Should().NotContain("name=\"subject\" value=\"french\"");

		// The remaining subjects are blocked by the choice count alone, so the page has to say so once,
		// up front — not only inside each blocked subject's card.
		htmlAfterThreeChoices.Should().Contain("choice-limit-notice");
		htmlAfterThreeChoices.Should().Contain("3 of 3 permitted A-level choices already made");

		var forgedToken = await ExtractAntiForgeryTokenAsync(afterSave);
		using var forgedChooseContent = new FormUrlEncodedContent(new Dictionary<string, string> {
			["__RequestVerificationToken"] = forgedToken,
			["subject"] = "french",
		});
		using var forgedChooseResponse = await client.PostAsync(new Uri("/razor?handler=ChooseSubject", UriKind.Relative), forgedChooseContent);
		using var afterForgedChoose = await client.GetAsync(forgedChooseResponse.Headers.Location);
		var htmlAfterForgedChoose = await afterForgedChoose.Content.ReadAsStringAsync();

		htmlAfterForgedChoose.Should().Contain("list-inline-item badge text-bg-primary rounded-pill\">Chemistry");
		htmlAfterForgedChoose.Should().Contain("list-inline-item badge text-bg-primary rounded-pill\">Biology");
		htmlAfterForgedChoose.Should().Contain("list-inline-item badge text-bg-primary rounded-pill\">History");
		htmlAfterForgedChoose.Should().NotContain("list-inline-item badge text-bg-primary rounded-pill\">French");
	}

	/// <summary>The same facts as <see cref="EligibleStudentForm" /> with every grade dropped to a 1.</summary>
	private static Dictionary<string, string> LoweredStudentForm(string token)
	{
		var form = EligibleStudentForm(token);
		foreach (var key in form.Keys.Where(static key => key.EndsWith(".Grade", StringComparison.Ordinal)).ToList()) {
			form[key] = "1";
		}

		return form;
	}

	private static Dictionary<string, string> EligibleStudentForm(string token)
	{
		// examples/golden/strong-constraints.json's GCSEs: with no chosen A-levels, French and German are green.
		var form = new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
			["Hobbies[0]"] = "chess_club",
		};
		var gcses = new (string Subject, int Grade)[] {
			("maths", 8), ("english_language", 8), ("english_literature", 8), ("physics", 8), ("chemistry", 8), ("biology", 8), ("french", 8),
			("german", 8), ("physical_education", 8), ("computer_studies", 8), ("history", 8), ("music", 8), ("art", 8),
		};
		for (var i = 0; i < gcses.Length; i++) {
			form[$"Gcses[{i}].Subject"] = gcses[i].Subject;
			form[$"Gcses[{i}].Grade"] = gcses[i].Grade.ToString(CultureInfo.InvariantCulture);
		}

		return form;
	}

	private static Dictionary<string, string> NormalAttainmentStudentForm(string token)
	{
		var form = new Dictionary<string, string> {
			["__RequestVerificationToken"] = token,
			["DateOfBirth"] = "2009-09-01",
			["Hobbies[0]"] = "chess_club",
		};
		var gcses = new (string Subject, int Grade)[] {
			("maths", 6), ("english_language", 6), ("english_literature", 6), ("physics", 6), ("chemistry", 6), ("biology", 6), ("french", 6),
			("german", 6), ("physical_education", 6), ("computer_studies", 6), ("history", 6), ("music", 6), ("art", 6),
		};
		for (var i = 0; i < gcses.Length; i++) {
			form[$"Gcses[{i}].Subject"] = gcses[i].Subject;
			form[$"Gcses[{i}].Grade"] = gcses[i].Grade.ToString(CultureInfo.InvariantCulture);
		}

		return form;
	}

	private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
	{
		var html = await response.Content.ReadAsStringAsync();
		const string marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
		var start = html.IndexOf(marker, StringComparison.Ordinal);
		start.Should().BeGreaterThan(-1, "the page must render the anti-forgery token");
		var valueStart = start + marker.Length;
		var valueEnd = html.IndexOf('"', valueStart);
		return html[valueStart..valueEnd];
	}
}
