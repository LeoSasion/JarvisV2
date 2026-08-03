using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Jarvis.ControlCenter;

public sealed class AttentionTargetBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new AttentionTargetAutomationPeer(this);

    private sealed class AttentionTargetAutomationPeer(
        AttentionTargetBorder owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType
            GetAutomationControlTypeCore() =>
                AutomationControlType.Pane;

        protected override string GetClassNameCore() =>
            nameof(AttentionTargetBorder);

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;

        protected override bool IsKeyboardFocusableCore() => true;
    }
}
