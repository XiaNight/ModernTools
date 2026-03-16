namespace Base.Components
{
    public interface INavigationItem
    {
        public string Text { get; set; }
        public string Glyph { get; set; }
        public string ShortText { get; set; }
        public string SecondaryGlyph { get; set; }
        public int OrderIndex { get; set; }
        public bool IsChild { get; set; }
        public int Size { get; }
        public int ItemHeight { get; set; }

        public event Action OnClick;
        public void Click();

        public void EnterCompactMode();
        public void ExitCompactMode();
        public void SetHighlightedState(bool state);
        public void UpdateLayoutAnimate() { }
    }
}
