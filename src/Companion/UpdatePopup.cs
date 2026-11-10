namespace Companion;

internal sealed class UpdatePopup : TableLayoutPanel
{
    private readonly Label heading = new() { AutoSize = true };
    private readonly Label body = new() { AutoSize = true };
    private readonly Button primary;
    private readonly Button dismiss;
    private readonly Font headingFont;
    private Action<bool>? complete;

    internal UpdatePopup(Font font, Color surface, Color ink, Color accent, Button primary, Button dismiss)
    {
        this.primary = primary;
        this.dismiss = dismiss;
        Font = font;
        headingFont = new Font(font.FontFamily, 14, FontStyle.Bold);
        AccessibleName = "Companion 업데이트";
        BackColor = surface;
        ForeColor = ink;
        BorderStyle = BorderStyle.FixedSingle;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        Visible = false;
        heading.Font = headingFont;
        heading.ForeColor = accent;
        primary.FlatAppearance.BorderColor = accent;
        primary.Click += (_, _) => Close(true);
        dismiss.Click += (_, _) => Close(false);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Anchor = AnchorStyles.Right, Margin = Padding.Empty
        };
        actions.Controls.Add(primary);
        actions.Controls.Add(dismiss);
        Controls.Add(heading, 0, 0);
        Controls.Add(body, 0, 1);
        Controls.Add(actions, 0, 2);
    }

    internal void Open(string title, string message, string? accept, string cancel, Action<bool> completed)
    {
        heading.Text = title;
        body.Text = message;
        primary.Text = accept ?? "";
        primary.Visible = accept is not null;
        dismiss.Text = cancel;
        complete = completed;
        Visible = true;
        BringToFront();
        Recenter();
        FocusAction();
    }

    internal void Recenter()
    {
        if (!Visible || Parent is null)
            return;
        int edge = LogicalToDeviceUnits(18);
        int width = Math.Min(LogicalToDeviceUnits(520), Math.Max(1, Parent.ClientSize.Width - 2 * edge));
        Padding = new Padding(edge);
        heading.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(10));
        body.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(16));
        heading.MaximumSize = body.MaximumSize = new Size(Math.Max(1, width - 2 * edge - 4), 0);
        MinimumSize = new Size(width, 0);
        MaximumSize = new Size(width, 0);
        PerformLayout();
        Location = new Point(Math.Max(0, (Parent.ClientSize.Width - Width) / 2),
            Math.Max(0, (Parent.ClientSize.Height - Height) / 2));
    }

    internal void FocusAction() => dismiss.Focus();

    internal void HandleKey(Keys key)
    {
        if (key == Keys.Escape)
            Close(false);
        else if (key is Keys.Tab or (Keys.Tab | Keys.Shift))
        {
            if (primary.Visible && !primary.Focused)
                primary.Focus();
            else
                dismiss.Focus();
        }
        else if (key is Keys.Enter or Keys.Space)
        {
            if (primary.Visible && primary.Focused)
                primary.PerformClick();
            else
                dismiss.PerformClick();
        }
    }

    internal void Close(bool accepted)
    {
        var callback = complete;
        complete = null;
        Hide();
        callback?.Invoke(accepted);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            headingFont.Dispose();
        base.Dispose(disposing);
    }
}
