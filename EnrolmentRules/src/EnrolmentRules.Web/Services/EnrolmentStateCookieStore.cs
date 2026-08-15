namespace EnrolmentRules.Web.Services;

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Models;

/// <summary>Loads and saves the <see cref="EnrolmentSession" /> snapshot held in a plain, self-contained cookie.</summary>
public interface IEnrolmentStateStore
{
	/// <summary>The current snapshot, or a fresh empty one if the cookie is absent or unreadable.</summary>
	Task<EnrolmentSession> LoadAsync(HttpContext context, CancellationToken cancellationToken = default);

	/// <summary>Persist <paramref name="snapshot" /> as the current facts.</summary>
	Task SaveAsync(HttpContext context, EnrolmentSession snapshot, CancellationToken cancellationToken = default);

	/// <summary>Clear the stored snapshot.</summary>
	Task ResetAsync(HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
///     <inheritdoc cref="IEnrolmentStateStore" /> Carries the snapshot's own bytes directly in the cookie
///     rather than a server-side-store lookup key, so any <c>EnrolmentRules.Web</c> instance can decode a
///     request without a shared cache or sticky sessions. Not encrypted: the payload is exactly what the
///     page already renders as HTML, so there is nothing here worth protecting. Realistic snapshots (a
///     handful of GCSE/qualification/hobby rows plus a basket) stay well under the ~4KB per-cookie budget
///     browsers enforce; there is no explicit cap beyond that browser limit.
/// </summary>
public sealed class EnrolmentStateCookieStore : IEnrolmentStateStore
{
	private const string CookieName = "enrolment.state";
	private const string EmptySnapshotId = "razor-request";

	private static readonly CookieOptions Options = new() {
		Path = "/razor",
		SameSite = SameSiteMode.Lax,
		HttpOnly = true,
	};

	public Task<EnrolmentSession> LoadAsync(HttpContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!context.Request.Cookies.TryGetValue(CookieName, out var cookieValue) || cookieValue is null) {
			return Task.FromResult(EnrolmentSession.Empty(EmptySnapshotId));
		}

		try {
			var bytes = Convert.FromBase64String(cookieValue);
			var snapshot = JsonSerializer.Deserialize(bytes, WebJsonContext.Default.EnrolmentSession);
			return Task.FromResult(snapshot ?? EnrolmentSession.Empty(EmptySnapshotId));
		}
		catch (Exception ex) when (ex is FormatException or JsonException) {
			context.Response.Cookies.Delete(CookieName, DeleteOptions(context));
			return Task.FromResult(EnrolmentSession.Empty(EmptySnapshotId));
		}
	}

	public Task SaveAsync(HttpContext context, EnrolmentSession snapshot, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(snapshot);

		var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, WebJsonContext.Default.EnrolmentSession);
		context.Response.Cookies.Append(CookieName, Convert.ToBase64String(bytes), CookieOptionsFor(context));
		return Task.CompletedTask;
	}

	public Task ResetAsync(HttpContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		context.Response.Cookies.Delete(CookieName, DeleteOptions(context));
		return Task.CompletedTask;
	}

	private static CookieOptions CookieOptionsFor(HttpContext context) => new() {
		Path = Options.Path,
		SameSite = Options.SameSite,
		HttpOnly = Options.HttpOnly,
		Secure = context.Request.IsHttps,
	};

	private static CookieOptions DeleteOptions(HttpContext context) => new() {
		Path = Options.Path,
		Secure = context.Request.IsHttps,
	};
}
