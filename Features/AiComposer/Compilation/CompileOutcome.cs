namespace AiComposer.Compilation;

/// <summary>
/// Result of acquiring a compiled module from the manager's cache: either a ready logic type or a
/// list of guardrail / compiler errors to surface in the page's error panel.
/// </summary>
internal sealed class CompileOutcome
{
	public bool Success { get; init; }
	public IReadOnlyList<string> Errors { get; init; } = [];
	public Type LogicType { get; init; }

	public static CompileOutcome Ok(Type logicType) => new() { Success = true, LogicType = logicType };
	public static CompileOutcome Failed(IReadOnlyList<string> errors) => new() { Success = false, Errors = errors };
}