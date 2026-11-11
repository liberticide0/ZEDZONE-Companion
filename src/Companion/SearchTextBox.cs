namespace Companion;

internal sealed class SearchTextBox : TextBox
{
    private const int ImeStartComposition = 0x010D;
    private const int ImeEndComposition = 0x010E;

    internal bool IsComposing { get; private set; }
    internal event EventHandler? CompositionEnded;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == ImeStartComposition)
            IsComposing = true;

        base.WndProc(ref message);

        if (message.Msg == ImeEndComposition)
        {
            IsComposing = false;
            CompositionEnded?.Invoke(this, EventArgs.Empty);
        }
    }
}
