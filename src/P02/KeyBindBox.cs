using System.ComponentModel;

namespace P02;

/// <summary>Click, then press a key. Shows the key it will send.</summary>
public sealed class KeyBindBox : Button
{
    private bool _capturing;
    private string _key = "1";

    public event Action<string>? KeyBound;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public string Key
    {
        get => _key;
        set { _key = value; Refresh_(); }
    }

    public KeyBindBox()
    {
        FlatStyle = FlatStyle.System;
        Refresh_();
        Click += (_, _) => BeginCapture();
        LostFocus += (_, _) => EndCapture();
    }

    private void Refresh_() => Text = _capturing ? "press a key…" : _key.ToUpperInvariant();

    private void BeginCapture()
    {
        _capturing = true;
        Refresh_();
    }

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        Refresh_();
    }

    // Buttons never see F-keys or Tab through OnKeyDown, so intercept earlier.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);

        Keys code = keyData & Keys.KeyCode;
        if (code == Keys.Escape) { EndCapture(); return true; }

        // Numpad is deliberately unsupported. Swallowing the press silently
        // would just look broken, so say why.
        if (code is (>= Keys.NumPad0 and <= Keys.NumPad9)
                 or Keys.Decimal or Keys.Multiply or Keys.Subtract
                 or Keys.Add or Keys.Divide)
        {
            Text = "use the number row";
            return true;
        }

        string? name = KeySender.FromKeys(keyData);
        if (name is not null)
        {
            _key = name;
            _capturing = false;
            Refresh_();
            KeyBound?.Invoke(name);
        }
        return true;
    }

    protected override bool IsInputKey(Keys keyData) => _capturing || base.IsInputKey(keyData);
}
