using System.Windows;
using System.Windows.Controls;
using AiComposer.Compilation;
using AiComposer.Model;
using AiComposer.Runtime;
using Base.Core;
using Base.Pages;

namespace AiComposer.UI;

/// <summary>
/// The composer: a static, attributed <see cref="PageBase"/> that authors generated page
/// definitions and drives the full local loop — persist, register, navigate, edit
/// (destroy + recreate), and reload-on-restart — without an external AI. It also doubles as a
/// verification harness via "Validate", which compiles the C# and parses the XAML without saving.
/// </summary>
[PageInfo("AI Composer",
	Glyph = "",
	ShortName = "Composer",
	Description = "Author, compile, and register AI-generated pages at runtime.",
	ShowDeviceSelection = false)]
public partial class AiComposerPage : PageBase
{
	private string editingId;
	private bool suppressSelection;

	public AiComposerPage()
	{
		InitializeComponent();
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		GeneratedPageManager.Instance.PagesChanged += RefreshList;
		RefreshList();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		GeneratedPageManager.Instance.PagesChanged -= RefreshList;
	}

	// ---- list ----

	private void RefreshList()
	{
		suppressSelection = true;
		PagesList.ItemsSource = GeneratedPageManager.Instance.GetDefinitions();
		if (editingId != null)
			PagesList.SelectedItem = ((IEnumerable<GeneratedPageDefinition>)PagesList.ItemsSource)
				.FirstOrDefault(d => d.Id == editingId);
		suppressSelection = false;
	}

	private void PagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (suppressSelection) return;
		if (PagesList.SelectedItem is not GeneratedPageDefinition selected) return;

		GeneratedPageDefinition full = GeneratedPageManager.Instance.LoadDefinition(selected.Id);
		if (full == null)
		{
			SetStatus($"Could not load definition for '{selected.Title}'.");
			return;
		}

		editingId = full.Id;
		TitleBox.Text = full.Title;
		GlyphBox.Text = full.Glyph;
		GroupBox.Text = full.Group;
		OrderBox.Text = full.Order == int.MaxValue ? "" : full.Order.ToString();
		XamlEditor.Text = full.Xaml;
		CsharpEditor.Text = full.Csharp;
		SetStatus($"Editing '{full.Title}' (id {full.Id}). Modified {full.ModifiedUtc:u}.");
	}

	// ---- actions ----

	private void New_Click(object sender, RoutedEventArgs e)
	{
		editingId = null;
		PagesList.SelectedItem = null;
		TitleBox.Text = "";
		GlyphBox.Text = "";
		GroupBox.Text = "Generated";
		OrderBox.Text = "";
		XamlEditor.Text = "";
		CsharpEditor.Text = "";
		SetStatus("New page. Fill in the fields and source, then Create page.");
	}

	private void Create_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateFields()) return;

		GeneratedPageDefinition def = BuildFromFields(id: null);
		GeneratedPageDefinition created = GeneratedPageManager.Instance.Create(def);
		editingId = created.Id;
		RefreshList();
		SetStatus($"Created '{created.Title}'. It now appears in the navigation" +
			(string.IsNullOrWhiteSpace(created.Group) ? "." : $" under '{created.Group}'.") +
			" Click its tab to open it.");
	}

	private void Update_Click(object sender, RoutedEventArgs e)
	{
		if (editingId == null)
		{
			SetStatus("Select a page in the list to update, or use Create page for a new one.");
			return;
		}
		if (!ValidateFields()) return;

		GeneratedPageDefinition def = BuildFromFields(editingId);
		GeneratedPageManager.Instance.Update(def);
		RefreshList();
		SetStatus($"Updated '{def.Title}'. Its old instance and assembly were destroyed and will be " +
			"recreated on next open; the navigation slot stayed in place.");
	}

	private void Delete_Click(object sender, RoutedEventArgs e)
	{
		if (editingId == null)
		{
			SetStatus("Select a page in the list to delete.");
			return;
		}

		string title = TitleBox.Text;
		MessageBoxResult confirm = MessageBox.Show(
			$"Delete the generated page '{title}'? This removes its stored files.",
			"Delete generated page", MessageBoxButton.YesNo, MessageBoxImage.Warning);
		if (confirm != MessageBoxResult.Yes) return;

		string id = editingId;
		GeneratedPageManager.Instance.Delete(id);
		editingId = null;
		New_Click(sender, e);
		SetStatus($"Deleted '{title}'.");
	}

	private async void Validate_Click(object sender, RoutedEventArgs e)
	{
		SetStatus("Validating…");
		string csharp = CsharpEditor.Text;
		string xaml = XamlEditor.Text;

		CompileResult result = await Task.Run(() => RoslynCompiler.Compile(csharp, "AiComposer.Validate"));
		result.LoadContext?.Unload(); // validation only — don't keep the assembly loaded

		if (!result.Success)
		{
			SetStatus("C# did not compile:" + Environment.NewLine + string.Join(Environment.NewLine, result.Errors));
			return;
		}

		try
		{
			XamlMaterializer.Parse(xaml);
		}
		catch (Exception ex)
		{
			SetStatus("C# compiles, but XAML failed to parse:" + Environment.NewLine + ex.Message);
			return;
		}

		SetStatus("Validation passed: C# compiles and XAML parses. Safe to create/update.");
	}

	private void Sample_Click(object sender, RoutedEventArgs e)
	{
		GeneratedPageDefinition sample = BuiltInSample.Create();
		editingId = null;
		PagesList.SelectedItem = null;
		TitleBox.Text = "My Sample Copy";
		GlyphBox.Text = sample.Glyph;
		GroupBox.Text = sample.Group;
		OrderBox.Text = "";
		XamlEditor.Text = sample.Xaml;
		CsharpEditor.Text = sample.Csharp;
		SetStatus("Loaded the sample template into the editor as a new page. Create page to add it.");
	}

	// ---- helpers ----

	private bool ValidateFields()
	{
		if (string.IsNullOrWhiteSpace(TitleBox.Text))
		{
			SetStatus("A title is required.");
			return false;
		}
		if (string.IsNullOrWhiteSpace(XamlEditor.Text) || string.IsNullOrWhiteSpace(CsharpEditor.Text))
		{
			SetStatus("Both XAML and C# are required.");
			return false;
		}
		return true;
	}

	private GeneratedPageDefinition BuildFromFields(string id)
	{
		bool hasOrder = int.TryParse(OrderBox.Text, out int order);
		return new GeneratedPageDefinition
		{
			Id = id ?? "",
			Title = TitleBox.Text.Trim(),
			Glyph = string.IsNullOrWhiteSpace(GlyphBox.Text) ? "" : GlyphBox.Text,
			Group = GroupBox.Text?.Trim() ?? "",
			Order = hasOrder ? order : int.MaxValue,
			Xaml = XamlEditor.Text,
			Csharp = CsharpEditor.Text,
		};
	}

	private void SetStatus(string message)
	{
		StatusText.Text = message;
	}
}
