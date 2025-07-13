using Avalonia;
using Avalonia.Controls;

namespace DiscordPurger.Controls;

public partial class CenterOverlayModal : UserControl
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<CenterOverlayModal, object?>(nameof(Content));

    public static readonly StyledProperty<bool> IsVisibleProperty =
        AvaloniaProperty.Register<CenterOverlayModal, bool>(nameof(IsVisible));

    public static readonly StyledProperty<bool> IsClosingProperty =
        AvaloniaProperty.Register<CenterOverlayModal, bool>(nameof(IsClosing));

    public CenterOverlayModal()
    {
        InitializeComponent();
    }

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public bool IsVisible
    {
        get => GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    public bool IsClosing
    {
        get => GetValue(IsClosingProperty);
        set => SetValue(IsClosingProperty, value);
    }
}