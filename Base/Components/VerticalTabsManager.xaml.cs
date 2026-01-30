using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using static System.Net.Mime.MediaTypeNames;

namespace Base.Components
{
    /// <summary>
    /// Interaction logic for TabsManager.xaml
    /// </summary>
    public partial class VerticalTabsManager : UserControl
    {
        public ObservableCollection<NavigationButton> TopButtons { get; } = new();
        public ObservableCollection<NavigationButton> BottomButtons { get; } = new();

        public event Action<NavigationButton> OnTabChanged;


        public VerticalTabsManager()
        {
            InitializeComponent();
        }

        public void Expand()
        {
            foreach (NavigationButton button in TopButtons) button.Expand();
            foreach (NavigationButton button in BottomButtons) button.Expand();
        }

        public void Collapse()
        {
            foreach (NavigationButton button in TopButtons) button.Collapse();
            foreach (NavigationButton button in BottomButtons) button.Collapse();
        }

        private void NavButtonClicked(NavigationButton button)
        {
            OnTabChanged?.Invoke(button);
        }

        public NavigationButton AddTop(string text, string glyph = "\uE7EF", string secondaryGlyph = "", int order = int.MaxValue)
        {
            NavigationButton newButton = new NavigationButton
            {
                Text = text,
                Glyph = glyph,
                SecondaryGlyph = secondaryGlyph,
                OrderIndex = order
            };
            newButton.OnClick += () => NavButtonClicked(newButton);
            TopButtons.Add(newButton);
            // Sort buttons by OrderIndex
            TopButtons.ToArray().OrderBy(b => b.OrderIndex).ToList().ForEach(b =>
            {
                TopButtons.Remove(b);
                TopButtons.Add(b);
            });
            return newButton;
        }

        public NavigationButton AddBottom(string text, string glyph = "\uE7EF", string secondaryGlyph = "", int order = int.MaxValue)
        {
            NavigationButton newButton = new NavigationButton
            {
                Text = text,
                Glyph = glyph,
                SecondaryGlyph = secondaryGlyph,
                OrderIndex = order
            };
            newButton.OnClick += () => NavButtonClicked(newButton);
            BottomButtons.Add(newButton);
            // Sort buttons by OrderIndex
            BottomButtons.ToArray().OrderBy(b => b.OrderIndex).ToList().ForEach(b =>
            {
                BottomButtons.Remove(b);
                BottomButtons.Add(b);
            });
            return newButton;
        }
    }
}
