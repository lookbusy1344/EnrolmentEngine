namespace EnrolmentRules.Tests;

using System.CodeDom.Compiler;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Domain;

/// <summary>
///     Architecture guard for the .NET performance-guideline size ceiling on value types (Framework
///     Design Guidelines: prefer a class once a struct's instance size passes roughly 16-24 bytes,
///     because every pass-by-value copies the whole thing). Uses reflection over the compiled
///     production assemblies rather than Roslyn: instance layout — decimal is 16 bytes, a reference
///     field is always 8 — is a runtime fact a syntax tree cannot see. Scans every non-generic
///     struct/record struct in the production assemblies referenced by this test project; a generic
///     struct is closed over <see cref="object" /> for measurement, since none of this repository's
///     generic value types vary in size by their type argument (they wrap a single reference-typed
///     field, e.g. <c>EquatableArray&lt;T&gt;</c>).
///     <para>
///         Three exclusions. A <c>ref struct</c> (a stack-only view like <see cref="Span{T}" />) and a
///         source-generator-emitted struct (<c>[GeneratedCode]</c>) are never copied around like an
///         ordinary value the way the guideline means. A struct authored on purpose above the ceiling
///         carries <see cref="LargeStructAttribute" /> (<c>EnrolmentRules.Domain</c>) with its own
///         reviewed justification.
///     </para>
/// </summary>
public sealed class CodeStyle_StructSize
{
	[Fact]
	public void Repository_value_types_do_not_exceed_the_24_byte_size_guideline()
	{
		var violations = ValueTypeSizeGuard.FindViolations(ValueTypeSizeGuard.ProductionAssemblyNames).ToArray();

		violations.Should().BeEmpty(
			"a value type over {0} bytes is copied wholesale on every pass-by-value -- shrink the fields, wrap " +
			"the bulk behind a reference, or convert to a class:{1}{2}",
			ValueTypeSizeGuard.MaxSizeInBytes,
			Environment.NewLine,
			string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void Struct_at_or_under_the_ceiling_is_not_a_violation() =>
		ValueTypeSizeGuard.Measure(typeof(EightByteStruct)).Should().Be(8);

	[Fact]
	public void Struct_over_the_ceiling_is_a_violation()
	{
		var size = ValueTypeSizeGuard.Measure(typeof(ThirtyTwoByteStruct));

		size.Should().BeGreaterThan(ValueTypeSizeGuard.MaxSizeInBytes);
	}

	[Fact]
	public void Oversized_record_struct_is_measured_the_same_as_an_oversized_struct()
	{
		var size = ValueTypeSizeGuard.Measure(typeof(ThirtyTwoByteRecordStruct));

		size.Should().BeGreaterThan(ValueTypeSizeGuard.MaxSizeInBytes);
	}

	[Fact]
	public void Enum_is_not_treated_as_a_value_type_to_measure() =>
		ValueTypeSizeGuard.DeclaredValueTypes([typeof(ExampleEnum).Assembly.GetName().Name!])
						  .Should().NotContain(typeof(ExampleEnum));

	[Fact]
	public void Open_generic_struct_is_closed_over_object_for_measurement()
	{
		// Backed by a single reference-typed field (`T[]?`), so its size does not depend on T -- the
		// same shape as this repository's EquatableArray<T>/EquatableDictionary<TKey, TValue>.
		var size = ValueTypeSizeGuard.Measure(typeof(GenericReferenceWrapper<>));

		size.Should().Be(IntPtr.Size);
	}

	[Fact]
	public void Ref_struct_is_excluded_from_the_scan()
	{
		// A ref struct is a stack-only view (Span<T>'s own shape) that can never be boxed, stored in a
		// field of a non-ref-struct type, or captured by a lambda -- it is never copied onto the heap or
		// into a long-lived collection, so the "pass-by-value is expensive" rationale for the 24-byte
		// ceiling does not apply to it.
		var assemblyName = typeof(CodeStyle_StructSize).Assembly.GetName().Name!;

		ValueTypeSizeGuard.DeclaredValueTypes([assemblyName]).Should().NotContain(typeof(LargeRefStruct));
	}

	[Fact]
	public void Struct_carrying_LargeStructAttribute_is_excluded_from_the_scan()
	{
		var assemblyName = typeof(CodeStyle_StructSize).Assembly.GetName().Name!;

		ValueTypeSizeGuard.FindViolations([assemblyName])
						  .Should().NotContain(violation => violation.Contains(nameof(ReviewedLargeStruct), StringComparison.Ordinal));
	}

	[Fact]
	public void Source_generator_emitted_struct_is_excluded_from_the_scan()
	{
		// Mirrors the shape of a source-generated parameter-carrier struct: the generator, not an
		// author, decides its field list and size.
		var assemblyName = typeof(CodeStyle_StructSize).Assembly.GetName().Name!;

		ValueTypeSizeGuard.DeclaredValueTypes([assemblyName]).Should().NotContain(typeof(SourceGeneratedLargeStruct));
	}

	[Fact]
	public void Compiler_generated_struct_is_excluded_from_the_scan()
	{
		// A real async-method state machine struct would demonstrate this too, but this test suite
		// bans `async`/`await` outside approved test infrastructure (SynchronousTestSuiteTests), so the
		// [CompilerGenerated] marker is applied by hand here to the same effect: the compiler, not an
		// author, decides such a type's field list and size, so the guard must keep it out of the scan
		// entirely -- reporting it would be a false positive no author can fix.
		var assemblyName = typeof(CodeStyle_StructSize).Assembly.GetName().Name!;

		ValueTypeSizeGuard.DeclaredValueTypes([assemblyName]).Should().NotContain(typeof(CompilerGeneratedLargeStruct));
	}

	[CompilerGenerated]
	private struct CompilerGeneratedLargeStruct
	{
		public long A;
		public long B;
		public long C;
		public long D;

		public CompilerGeneratedLargeStruct(long a, long b, long c, long d)
		{
			A = a;
			B = b;
			C = c;
			D = d;
		}
	}

	private struct EightByteStruct
	{
		public long Value;

		public EightByteStruct(long value) => Value = value;
	}

	private struct ThirtyTwoByteStruct
	{
		public long A;
		public long B;
		public long C;
		public long D;

		public ThirtyTwoByteStruct(long a, long b, long c, long d)
		{
			A = a;
			B = b;
			C = c;
			D = d;
		}
	}

	private record struct ThirtyTwoByteRecordStruct(long A, long B, long C, long D);

	private enum ExampleEnum
	{
		None,
	}

	private readonly struct GenericReferenceWrapper<T>(T[]? items)
	{
		private readonly T[]? items = items;

		public bool IsEmpty => items is null or [];
	}

	private readonly ref struct LargeRefStruct
	{
		private readonly long a;
		private readonly long b;
		private readonly long c;
		private readonly long d;

		public LargeRefStruct(long a, long b, long c, long d)
		{
			this.a = a;
			this.b = b;
			this.c = c;
			this.d = d;
		}

		public long Sum => a + b + c + d;
	}

	[LargeStruct("Fixture proving FindViolations respects a reviewed exception.")]
	private readonly struct ReviewedLargeStruct
	{
		private readonly long a;
		private readonly long b;
		private readonly long c;
		private readonly long d;

		public ReviewedLargeStruct(long a, long b, long c, long d)
		{
			this.a = a;
			this.b = b;
			this.c = c;
			this.d = d;
		}

		public long Sum => a + b + c + d;
	}

	[GeneratedCode("EnrolmentRules.Tests.CodeStyle_StructSize", "1.0")]
	private readonly struct SourceGeneratedLargeStruct
	{
		private readonly long a;
		private readonly long b;
		private readonly long c;
		private readonly long d;

		public SourceGeneratedLargeStruct(long a, long b, long c, long d)
		{
			this.a = a;
			this.b = b;
			this.c = c;
			this.d = d;
		}

		public long Sum => a + b + c + d;
	}
}

internal static class ValueTypeSizeGuard
{
	public const int MaxSizeInBytes = 24;

	public static readonly FrozenSet<string> ProductionAssemblyNames = FrozenSet.ToFrozenSet(
		[
			"EnrolmentRules.Domain",
			"EnrolmentRules.Prediction",
			"EnrolmentRules.Engine",
			"EnrolmentRules.Extensions.DependencyInjection",
			"EnrolmentRules.Cli",
		],
		StringComparer.Ordinal);

	private static readonly MethodInfo SizeOfMethod =
		typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf), BindingFlags.Public | BindingFlags.Static)!;

	public static IEnumerable<string> FindViolations(IEnumerable<string> assemblyNames) =>
		DeclaredValueTypes(assemblyNames)
			.Where(static type => !Attribute.IsDefined(type, typeof(LargeStructAttribute)))
			.Select(type => (Type: type, Size: Measure(type)))
			.Where(measurement => measurement.Size > MaxSizeInBytes)
			.Select(measurement => Describe(measurement.Type, measurement.Size))
			.Order(StringComparer.Ordinal);

	public static IEnumerable<Type> DeclaredValueTypes(IEnumerable<string> assemblyNames) =>
		assemblyNames
			.Select(Assembly.Load)
			.SelectMany(static assembly => assembly.GetTypes())
			.Where(static type => type.IsValueType && !type.IsEnum && !type.IsByRefLike)
			.Where(static type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
			.Where(static type => !Attribute.IsDefined(type, typeof(GeneratedCodeAttribute)));

	/// <summary>
	///     The type's runtime instance size, in bytes, via <see cref="Unsafe.SizeOf{T}" /> — unlike
	///     <c>Marshal.SizeOf</c>, this is the true managed layout size (a <see langword="bool" /> is 1
	///     byte, not the 4-byte marshaled default) and, unlike C#'s <c>sizeof</c> operator, it carries no
	///     <c>unmanaged</c> constraint, so a struct holding a reference-typed field measures correctly too.
	///     An open generic type definition is closed over <see cref="object" /> first.
	/// </summary>
	public static int Measure(Type type)
	{
		var closed = type.IsGenericTypeDefinition
			? type.MakeGenericType([.. type.GetGenericArguments().Select(static _ => typeof(object))])
			: type;

		return (int)SizeOfMethod.MakeGenericMethod(closed).Invoke(null, null)!;
	}

	private static string Describe(Type type, int size) =>
		$"{type.FullName ?? type.Name}: {size} bytes (> {MaxSizeInBytes})";
}
