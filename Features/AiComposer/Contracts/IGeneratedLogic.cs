using System.Windows;

namespace AiComposer.Contracts;

/// <summary>
/// The contract every generated page's C# implements. The host compiles the C#, instantiates the
/// single implementing type, then calls <see cref="Initialize"/> and sets the instance as the
/// DataContext of the parsed XAML root — so the logic's public ICommand / bindable properties drive
/// the UI through {Binding}. Generated pages have no code-behind or event handlers; all interaction
/// flows through commands and bindings against this object.
/// </summary>
public interface IGeneratedLogic
{
	/// <summary>
	/// Called once after the page's XAML is parsed, before it is shown. <paramref name="host"/> is
	/// the curated host surface; <paramref name="root"/> is the parsed XAML root (already the
	/// DataContext target) for logic that needs to inspect named elements.
	/// </summary>
	void Initialize(IHostApi host, FrameworkElement root);
}
