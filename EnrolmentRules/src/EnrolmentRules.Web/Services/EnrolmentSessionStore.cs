namespace EnrolmentRules.Web.Services;

using System.Text.Json;
using Models;

/// <summary>Loads and saves the <see cref="EnrolmentSession" /> snapshot held in the ASP.NET session.</summary>
public interface IEnrolmentSessionStore
{
	/// <summary>The current snapshot, or a fresh empty one keyed to <paramref name="session" />'s id if none is stored yet.</summary>
	Task<EnrolmentSession> LoadAsync(ISession session, CancellationToken cancellationToken = default);

	/// <summary>Persist <paramref name="snapshot" /> as the session's current facts.</summary>
	Task SaveAsync(ISession session, EnrolmentSession snapshot, CancellationToken cancellationToken = default);

	/// <summary>Clear this site's stored snapshot, leaving the rest of the session untouched.</summary>
	Task ResetAsync(ISession session, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEnrolmentSessionStore" />
public sealed class EnrolmentSessionStore : IEnrolmentSessionStore
{
	private const string SessionKey = "enrolment.session";

	public async Task<EnrolmentSession> LoadAsync(ISession session, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);

		await session.LoadAsync(cancellationToken);
		if (!session.TryGetValue(SessionKey, out var bytes)) {
			return EnrolmentSession.Empty(session.Id);
		}

		try {
			return JsonSerializer.Deserialize(bytes, WebJsonContext.Default.EnrolmentSession) ?? EnrolmentSession.Empty(session.Id);
		}
		catch (JsonException) {
			session.Remove(SessionKey);
			await session.CommitAsync(cancellationToken);
			return EnrolmentSession.Empty(session.Id);
		}
	}

	public async Task SaveAsync(ISession session, EnrolmentSession snapshot, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(snapshot);

		session.Set(SessionKey, JsonSerializer.SerializeToUtf8Bytes(snapshot, WebJsonContext.Default.EnrolmentSession));
		await session.CommitAsync(cancellationToken);
	}

	public async Task ResetAsync(ISession session, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);

		session.Remove(SessionKey);
		await session.CommitAsync(cancellationToken);
	}
}
