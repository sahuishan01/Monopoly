using BoardEmpire.Core;
using Godot;

namespace BoardEmpire.UI;

/// <summary>A full-window page on the navigation stack.</summary>
public abstract partial class Screen : Control
{
    protected App App => App.I;
    protected Settings Settings => App.I.Settings;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
    }

    protected abstract void Build();

    /// <summary>Return true to consume the back action (for example to close a dialog).</summary>
    public virtual bool HandleBack() => false;

    /// <summary>Called when the screen above this one is closed.</summary>
    public virtual void OnResumed() { }

    /// <summary>A new instance showing the same thing, used after theme or language changes.</summary>
    public virtual Screen? Recreate() => null;

    /// <summary>Standard page chrome: back button, title and a centred content column.</summary>
    protected Control Page(string title, Control content, int maxWidth = 1100, Control? trailing = null)
    {
        var header = Ui.HBox(14,
            Ui.Button("‹ " + Loc.T("Back"), App.Back, ButtonKind.Ghost),
            Ui.Label(Loc.T(title).ToUpperInvariant(), 22, Tokens.Text, mono: true, bold: true),
            Ui.Spacer());
        if (trailing != null) header.AddChild(trailing);

        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = Ui.VBox(18, header, content);
        column.CustomMinimumSize = new Vector2(0, 0);
        var holder = new MarginContainer().Full();
        int side = Mathf.Max(20, (int)((GetViewportRect().Size.X - maxWidth) / 2));
        holder.AddThemeConstantOverride("margin_left", side);
        holder.AddThemeConstantOverride("margin_right", side);
        holder.AddThemeConstantOverride("margin_top", 22);
        holder.AddThemeConstantOverride("margin_bottom", 22);
        holder.AddChild(column);
        AddChild(holder);
        return holder;
    }

    /// <summary>A titled group of controls.</summary>
    protected static PanelContainer Section(string caption, params Control[] children)
    {
        var box = Ui.VBox(10);
        box.AddChild(Ui.Caption(caption, Tokens.Accent));
        foreach (var c in children) box.AddChild(c);
        return Ui.Panel(box);
    }

    protected static HBoxContainer Row(string label, Control control)
    {
        var name = Ui.Label(Loc.T(label), 16, Tokens.Text);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return Ui.HBox(12, name, control);
    }
}

/// <summary>Dimmed overlay with a centred card; tapping outside closes it.</summary>
public partial class Modal : Control
{
    private readonly Control _content;
    private readonly Action? _onClose;
    private readonly bool _dismissable;

    public Modal(Control content, Action? onClose = null, bool dismissable = true, int width = 560)
    {
        _content = content;
        _onClose = onClose;
        _dismissable = dismissable;
        content.CustomMinimumSize = new Vector2(Ui.Px(width), 0);
    }

    public override void _Ready()
    {
        this.Full();
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) }.Full();
        dim.GuiInput += e =>
        {
            if (_dismissable && e is InputEventMouseButton { Pressed: true }) Close();
        };
        AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }.Full();
        var panel = Ui.Panel(_content, Tokens.Panel, 22, 18, Tokens.Line);
        center.AddChild(panel);
        AddChild(center);
        if (!App.I.Settings.ReduceMotion)
        {
            panel.PivotOffset = panel.Size / 2;
            Modulate = Modulate with { A = 0 };
            CreateTween().TweenProperty(this, "modulate:a", 1f, 0.14);
        }
    }

    public void Close()
    {
        _onClose?.Invoke();
        QueueFree();
    }
}
