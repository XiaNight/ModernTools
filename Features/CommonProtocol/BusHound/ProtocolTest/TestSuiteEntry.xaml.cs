namespace CommonProtocol.BusHound.ProtocolTest;

using Base.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

public partial class TestSuiteEntry : UserControl
{
	private TestSuite suite;
	private bool expanded;
	private readonly List<TestStepEntry> stepEntries = new();

	public TestSuiteEntry()
	{
		InitializeComponent();

		SuiteHeader.MouseLeftButtonUp += (_, _) => ToggleExpand();
		RunButton.Click += (_, e) => { e.Handled = true; RunRequested?.Invoke(this, EventArgs.Empty); };
		EditButton.Click += (_, e) => { e.Handled = true; EditRequested?.Invoke(this, EventArgs.Empty); };
		DeleteButton.Click += (_, e) => { e.Handled = true; DeleteRequested?.Invoke(this, EventArgs.Empty); };
		AddStepBtn.Click += (_, _) => AddStepRequested?.Invoke(this, EventArgs.Empty);
	}

	public TestSuite Suite => suite;

	public bool IsExpanded => expanded;

	public IReadOnlyList<TestStepEntry> StepEntries => stepEntries;

	public event EventHandler RunRequested;
	public event EventHandler EditRequested;
	public event EventHandler DeleteRequested;
	public event EventHandler AddStepRequested;
	public event EventHandler<StepEventArgs> StepEditRequested;
	public event EventHandler<StepEventArgs> StepDeleteRequested;
	public event EventHandler<StepEventArgs> StepViewRequested;
	public TestVerdict? LastVerdict { get; private set; }
	public double? LastElapsedMs { get; private set; }
	public string LastMessage { get; private set; }

	public void Bind(TestSuite boundSuite)
	{
		suite = boundSuite;
		NameBlock.Text = suite?.Name ?? string.Empty;
		UpdateStepCount();
		ResetVerdict();
		RebuildStepRows();
	}

	public void UpdateStepCount()
	{
		int count = suite?.Steps?.Count ?? 0;
		StepCountText.Text = $"{count} step(s)";
	}

	public void RebuildStepRows()
	{
		StepsList.Children.Clear();
		stepEntries.Clear();

		if (suite?.Steps == null) return;

		foreach (TestStep step in suite.Steps)
		{
			TestStepEntry entry = new();
			entry.Bind(step);
			entry.EditRequested += (sender, _) => StepEditRequested?.Invoke(this, new StepEventArgs(sender as TestStepEntry));
			entry.DeleteRequested += (sender, _) => StepDeleteRequested?.Invoke(this, new StepEventArgs(sender as TestStepEntry));
			entry.ViewRequested += (sender, _) => StepViewRequested?.Invoke(this, new StepEventArgs(sender as TestStepEntry));

			stepEntries.Add(entry);
			StepsList.Children.Add(entry);
		}

		UpdateStepCount();
	}

	public void SetExpanded(bool expand)
	{
		expanded = expand;
		StepsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
		ExpandIcon.Glyph = expanded ? "" : "";
	}

	public void ToggleExpand()
	{
		SetExpanded(!expanded);
	}

	public void ResetVerdict()
	{
		LastVerdict = null;
		LastElapsedMs = null;
		LastMessage = null;
		SetBadge("--", "SkipBg", null);
		SetResponseTime(null);
	}

	public void ShowRunning()
	{
		SetBadge("…", "SkipBg", null);
		SetResponseTime(null);
	}

	public void ShowResult(TestVerdict verdict, string message, TimeSpan elapsed)
	{
		LastVerdict = verdict;
		LastMessage = message;
		LastElapsedMs = elapsed > TimeSpan.Zero ? elapsed.TotalMilliseconds : null;

		switch (verdict)
		{
			case TestVerdict.Pass:
				SetBadge("PASS", "PassBg", message, "PassFg");
				break;
			case TestVerdict.Timeout:
				SetBadge("TIMEOUT", "FailBg", message, "FailFg");
				break;
			case TestVerdict.Mismatch:
				SetBadge("MISMATCH", "FailBg", message, "FailFg");
				break;
			default:
				SetBadge("ERROR", "FailBg", message, "FailFg");
				break;
		}

		SetResponseTime(elapsed > TimeSpan.Zero ? elapsed : null);
	}

	public void ShowSummary(SuiteRunResult suiteResult)
	{
		if (suiteResult == null) return;

		int passed = 0;
		int total = suiteResult.StepResults.Count;
		foreach (StepRunResult sr in suiteResult.StepResults)
		{
			if (sr.Verdict == TestVerdict.Pass) passed++;
		}

		string summary = $"{passed}/{total} passed";
		ShowResult(suiteResult.Verdict, summary, TimeSpan.FromMilliseconds(suiteResult.TotalElapsedMs));
	}

	private void SetBadge(string text, string backgroundKey, string tooltip, string foregroundKey = null)
	{
		BadgeText.Text = text;

		if (TryFindResource(backgroundKey) is Brush background)
			Badge.Background = background;

		BadgeText.Foreground = foregroundKey != null && TryFindResource(foregroundKey) is Brush foreground
			? foreground
			: (Brush)FindResource("SystemControlForegroundBaseHighBrush");

		Badge.ToolTip = string.IsNullOrWhiteSpace(tooltip) ? null : tooltip;
	}

	private void SetResponseTime(TimeSpan? elapsed)
	{
		if (elapsed is null)
		{
			ResponseTimeText.Text = "--";
			return;
		}

		ResponseTimeText.Text = Utilities.FormatInterval(elapsed.Value);
	}
}

public sealed class StepEventArgs : EventArgs
{
	public StepEventArgs(TestStepEntry entry)
	{
		Entry = entry;
	}

	public TestStepEntry Entry { get; }
}
