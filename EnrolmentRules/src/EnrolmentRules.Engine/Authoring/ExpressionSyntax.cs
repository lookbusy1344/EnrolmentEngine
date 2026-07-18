namespace EnrolmentRules.Engine.Authoring;

using System.Globalization;
using System.Text;

/// <summary>
///     A parsed rule expression. Deliberately a small tree over the subset the workflows use rather than a
///     general C# parser: the narrator must be able to say "I do not understand this" precisely, which a
///     permissive parser cannot.
/// </summary>
internal abstract class Node
{
	/// <summary>A short rendering of this node for diagnostics, so a narration failure names the construct.</summary>
	public abstract string Describe();
}

internal sealed class AndNode(IReadOnlyList<Node> parts) : Node
{
	public IReadOnlyList<Node> Parts { get; } = parts;

	public override string Describe() => string.Join(" && ", Parts.Select(static part => part.Describe()));
}

internal sealed class OrNode(IReadOnlyList<Node> parts) : Node
{
	public IReadOnlyList<Node> Parts { get; } = parts;

	public override string Describe() => string.Join(" || ", Parts.Select(static part => part.Describe()));
}

internal sealed class ComparisonNode(Node left, string @operator, Node right) : Node
{
	public Node Left { get; } = left;

	public string Operator { get; } = @operator;

	public Node Right { get; } = right;

	public override string Describe() => $"{Left.Describe()} {Operator} {Right.Describe()}";
}

internal sealed class CallNode(string owner, string method, IReadOnlyList<Node> arguments) : Node
{
	public string Owner { get; } = owner;

	public string Method { get; } = method;

	public IReadOnlyList<Node> Arguments { get; } = arguments;

	public override string Describe() =>
		$"{Owner}.{Method}({string.Join(", ", Arguments.Select(static argument => argument.Describe()))})";
}

internal sealed class MemberNode(string owner, string name) : Node
{
	public string Owner { get; } = owner;

	public string Name { get; } = name;

	public override string Describe() => $"{Owner}.{Name}";
}

internal sealed class IdentifierNode(string name) : Node
{
	public string Name { get; } = name;

	public override string Describe() => Name;
}

internal sealed class NumberNode(double value) : Node
{
	public double Value { get; } = value;

	public override string Describe() => Value.ToString("0.##", CultureInfo.InvariantCulture);
}

internal sealed class StringNode(string value) : Node
{
	public string Value { get; } = value;

	public override string Describe() => $"\"{Value}\"";
}

internal sealed class LambdaNode(string parameter, Node body) : Node
{
	public string Parameter { get; } = parameter;

	public Node Body { get; } = body;

	public override string Describe() => $"{Parameter} => {Body.Describe()}";
}

/// <summary>
///     A conditional (<c>cond ? a : b</c>) bound — the shape an age-gated or otherwise varying entry
///     requirement is authored in, where the threshold itself depends on a fact about the student.
/// </summary>
internal sealed class ConditionalNode(Node condition, Node whenTrue, Node whenFalse) : Node
{
	public Node Condition { get; } = condition;

	public Node WhenTrue { get; } = whenTrue;

	public Node WhenFalse { get; } = whenFalse;

	public override string Describe() => $"{Condition.Describe()} ? {WhenTrue.Describe()} : {WhenFalse.Describe()}";
}

internal sealed class BooleanNode(bool value) : Node
{
	public bool Value { get; } = value;

	public override string Describe() => Value ? "true" : "false";
}

/// <summary>
///     Recursive-descent parser over the rule-expression subset, with C# precedence: <c>||</c> lowest,
///     then <c>&amp;&amp;</c>, then the comparison operators. Any token it does not recognise raises
///     <see cref="CriteriaNarrationException" /> rather than being skipped.
/// </summary>
internal static class Parser
{
	private static readonly string[] Operators = [">=", "<=", "==", "!=", ">", "<"];

	public static Node Parse(string expression)
	{
		var tokens = Tokenizer.Tokenize(expression);
		var position = 0;
		var node = ParseExpression(tokens, ref position);

		return position == tokens.Count
			? node
			: throw new CriteriaNarrationException($"unexpected '{tokens[position].Text}' in expression '{expression.Trim()}'");
	}

	/// <summary>The lowest-precedence level: a conditional, or whatever sits below it.</summary>
	private static Node ParseExpression(IReadOnlyList<Token> tokens, ref int position)
	{
		var condition = ParseOr(tokens, ref position);
		if (Peek(tokens, position) is not "?") {
			return condition;
		}

		position++;
		var whenTrue = ParseExpression(tokens, ref position);
		Expect(tokens, ref position, ":");
		var whenFalse = ParseExpression(tokens, ref position);
		return new ConditionalNode(condition, whenTrue, whenFalse);
	}

	private static Node ParseOr(IReadOnlyList<Token> tokens, ref int position)
	{
		var parts = new List<Node> { ParseAnd(tokens, ref position) };
		while (Peek(tokens, position) is "||") {
			position++;
			parts.Add(ParseAnd(tokens, ref position));
		}

		return parts.Count == 1 ? parts[0] : new OrNode(parts);
	}

	private static Node ParseAnd(IReadOnlyList<Token> tokens, ref int position)
	{
		var parts = new List<Node> { ParseComparison(tokens, ref position) };
		while (Peek(tokens, position) is "&&") {
			position++;
			parts.Add(ParseComparison(tokens, ref position));
		}

		return parts.Count == 1 ? parts[0] : new AndNode(parts);
	}

	private static Node ParseComparison(IReadOnlyList<Token> tokens, ref int position)
	{
		var left = ParsePrimary(tokens, ref position);
		if (Peek(tokens, position) is not { } candidate || !Operators.Contains(candidate, StringComparer.Ordinal)) {
			return left;
		}

		position++;
		var right = ParsePrimary(tokens, ref position);
		return new ComparisonNode(left, candidate, right);
	}

	private static Node ParsePrimary(IReadOnlyList<Token> tokens, ref int position)
	{
		if (position >= tokens.Count) {
			throw new CriteriaNarrationException("expression ended unexpectedly");
		}

		var token = tokens[position];
		switch (token.Kind) {
			case TokenKind.Punctuation when token.Text == "(": {
					position++;
					var inner = ParseExpression(tokens, ref position);
					Expect(tokens, ref position, ")");
					return inner;
				}

			case TokenKind.Number:
				position++;
				return new NumberNode(double.Parse(token.Text, CultureInfo.InvariantCulture));

			case TokenKind.String:
				position++;
				return new StringNode(token.Text);

			case TokenKind.Identifier:
				return ParseIdentifier(tokens, ref position, token);

			default:
				throw new CriteriaNarrationException($"unexpected '{token.Text}' in expression");
		}
	}

	private static Node ParseIdentifier(IReadOnlyList<Token> tokens, ref int position, Token token)
	{
		position++;

		if (token.Text is "true" or "false") {
			return new BooleanNode(token.Text == "true");
		}

		// A lambda parameter: `g => g.Grade >= …`, the shape the eligibility pass count is written in.
		if (Peek(tokens, position) is "=>") {
			position++;
			return new LambdaNode(token.Text, ParseExpression(tokens, ref position));
		}

		if (Peek(tokens, position) is not ".") {
			return new IdentifierNode(token.Text);
		}

		position++;
		var member = Expect(tokens, ref position, TokenKind.Identifier);

		if (Peek(tokens, position) is not "(") {
			return new MemberNode(token.Text, member);
		}

		position++;
		var arguments = new List<Node>();
		while (Peek(tokens, position) is not ")") {
			arguments.Add(ParseExpression(tokens, ref position));
			if (Peek(tokens, position) is ",") {
				position++;
			}
		}

		Expect(tokens, ref position, ")");
		return new CallNode(token.Text, member, arguments);
	}

	private static string? Peek(IReadOnlyList<Token> tokens, int position) =>
		position < tokens.Count ? tokens[position].Text : null;

	private static void Expect(IReadOnlyList<Token> tokens, ref int position, string text)
	{
		if (Peek(tokens, position) != text) {
			throw new CriteriaNarrationException($"expected '{text}' but found '{Peek(tokens, position) ?? "end of expression"}'");
		}

		position++;
	}

	private static string Expect(IReadOnlyList<Token> tokens, ref int position, TokenKind kind)
	{
		if (position >= tokens.Count || tokens[position].Kind != kind) {
			throw new CriteriaNarrationException($"expected {kind} but found '{Peek(tokens, position) ?? "end of expression"}'");
		}

		return tokens[position++].Text;
	}
}

internal enum TokenKind
{
	Identifier,
	Number,
	String,
	Operator,
	Punctuation,
}

internal readonly record struct Token(TokenKind Kind, string Text);

internal static class Tokenizer
{
	private static readonly string[] MultiCharacterOperators = ["&&", "||", ">=", "<=", "==", "!=", "=>"];

	public static IReadOnlyList<Token> Tokenize(string expression)
	{
		var tokens = new List<Token>();
		var index = 0;

		while (index < expression.Length) {
			var character = expression[index];

			if (char.IsWhiteSpace(character)) {
				index++;
				continue;
			}

			if (character == '"') {
				tokens.Add(new(TokenKind.String, ReadString(expression, ref index)));
				continue;
			}

			if (char.IsAsciiDigit(character)) {
				tokens.Add(new(TokenKind.Number, ReadWhile(expression, ref index, static c => char.IsAsciiDigit(c) || c == '.')));
				continue;
			}

			if (char.IsLetter(character) || character == '_') {
				tokens.Add(new(TokenKind.Identifier,
					ReadWhile(expression, ref index, static c => char.IsLetterOrDigit(c) || c == '_')));
				continue;
			}

			var multiCharacter = MultiCharacterOperators.FirstOrDefault(op =>
				index + op.Length <= expression.Length && expression.AsSpan(index, op.Length).SequenceEqual(op));

			if (multiCharacter is not null) {
				tokens.Add(new(TokenKind.Operator, multiCharacter));
				index += multiCharacter.Length;
				continue;
			}

			if (character is '(' or ')' or ',' or '.' or '>' or '<' or '?' or ':') {
				tokens.Add(new(character is '>' or '<' ? TokenKind.Operator : TokenKind.Punctuation, character.ToString()));
				index++;
				continue;
			}

			throw new CriteriaNarrationException($"unexpected character '{character}' in expression '{expression.Trim()}'");
		}

		return tokens;
	}

	private static string ReadString(string expression, ref int index)
	{
		var start = ++index;
		while (index < expression.Length && expression[index] != '"') {
			index++;
		}

		if (index >= expression.Length) {
			throw new CriteriaNarrationException($"unterminated string in expression '{expression.Trim()}'");
		}

		var value = expression[start..index];
		index++;
		return value;
	}

	private static string ReadWhile(string expression, ref int index, Func<char, bool> predicate)
	{
		var start = index;
		while (index < expression.Length && predicate(expression[index])) {
			index++;
		}

		return expression[start..index];
	}
}

/// <summary>Snake_case rule vocabulary rendered as the display names a student would recognise.</summary>
internal static class Naming
{
	public static string Display(string snakeCase)
	{
		var builder = new StringBuilder(snakeCase.Length);
		var capitalise = true;

		foreach (var character in snakeCase) {
			if (character == '_') {
				_ = builder.Append(' ');
				capitalise = true;
				continue;
			}

			_ = builder.Append(capitalise ? char.ToUpperInvariant(character) : character);
			capitalise = false;
		}

		return builder.ToString();
	}
}
