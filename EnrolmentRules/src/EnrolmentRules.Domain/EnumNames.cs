namespace EnrolmentRules.Domain;

using System.Reflection;
using System.Text.Json.Serialization;

/// <summary>
///     The JSON ⇄ enum name mapping declared once by the <see cref="JsonStringEnumMemberNameAttribute" />
///     on each member, exposed for host code that has to round-trip names that live in the workflow JSON
///     (e.g. parsing a <c>"{subject}:{rating}"</c> rule name back to typed values). The attribute remains
///     the single source of truth, so there is no second hard-coded list to drift; the map is built once
///     per enum type and cached.
/// </summary>
public static class EnumNames
{
	/// <summary>The JSON name of <paramref name="subject" />.</summary>
	public static string NameOf(Subject subject) => subject.Value;

	/// <summary>Resolve a JSON subject name to its typed value object.</summary>
	public static bool TryParse(string name, out Subject subject) => Subject.TryParse(name, out subject);

	/// <summary>The JSON name of <paramref name="value" />.</summary>
	public static string NameOf<TEnum>(TEnum value) where TEnum : struct, Enum => Cache<TEnum>.ToName[value];

	/// <summary>Resolve a JSON name to its enum value; <c>false</c> if it is not a known member name.</summary>
	public static bool TryParse<TEnum>(string name, out TEnum value) where TEnum : struct, Enum =>
		Cache<TEnum>.ByName.TryGetValue(name, out value);

	private static class Cache<TEnum> where TEnum : struct, Enum
	{
		public static readonly IReadOnlyDictionary<string, TEnum> ByName = Build();

		public static readonly IReadOnlyDictionary<TEnum, string> ToName =
			ByName.ToDictionary(static kv => kv.Value, static kv => kv.Key);

		private static Dictionary<string, TEnum> Build()
		{
			var map = new Dictionary<string, TEnum>(StringComparer.Ordinal);
			foreach (var value in Enum.GetValues<TEnum>()) {
				var field = typeof(TEnum).GetField(value.ToString())!;
				var name = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? value.ToString();
				map[name] = value;
			}

			return map;
		}
	}
}
