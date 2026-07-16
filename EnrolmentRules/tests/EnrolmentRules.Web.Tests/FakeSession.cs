namespace EnrolmentRules.Web.Tests;

/// <summary>
///     A minimal in-memory <see cref="ISession" /> double: <see cref="EnrolmentSessionStore" /> tests only
///     need session load/commit plus <see cref="TryGetValue" />/<see cref="Set" />/<see cref="Remove" /> and a
///     stable <see cref="Id" />, so this avoids depending on the distributed-cache-backed
///     <c>DistributedSession</c> constructor shape.
/// </summary>
internal sealed class FakeSession : ISession
{
	private readonly Dictionary<string, byte[]> store = [];

	public FakeSession(string? id = null) => Id = id ?? Guid.NewGuid().ToString("N");

	public int LoadAsyncCallCount { get; private set; }

	public int CommitAsyncCallCount { get; private set; }

	public bool IsAvailable => true;

	public string Id { get; }

	public IEnumerable<string> Keys => store.Keys;

	public Task LoadAsync(CancellationToken cancellationToken = default)
	{
		LoadAsyncCallCount++;
		return Task.CompletedTask;
	}

	public Task CommitAsync(CancellationToken cancellationToken = default)
	{
		CommitAsyncCallCount++;
		return Task.CompletedTask;
	}

	public bool TryGetValue(string key, out byte[] value) => store.TryGetValue(key, out value!);

	public void Set(string key, byte[] value) => store[key] = value;

	public void Remove(string key) => store.Remove(key);

	public void Clear() => store.Clear();
}
