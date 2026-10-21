using Microsoft.Win32;

namespace SteamVRLibrary;

public interface IStartupRegistration
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Virtual Desktop Unlock";
    private readonly string keyPath;
    private readonly string executable;

    public WindowsStartupRegistration(string? keyPath = null, string? executable = null)
    {
        this.keyPath = keyPath ?? RunKey;
        this.executable = executable ?? Environment.ProcessPath!;
    }

    public bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string Command => "\"" + executable + "\"";

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

public sealed class SettingsForm : Form
{
    public SettingsForm(IStartupRegistration startup, bool showErrors = true)
    {
        Text = "Settings — Virtual Desktop Unlock";
        BackColor = Color.FromArgb(14, 20, 31);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(540, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 };
        foreach (int height in new[] { 40, 32, 44, 48, 48, 44 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "Settings", AutoSize = true, Font = new Font(Font.FontFamily, 19, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "Language · Coming in a future update", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(159, 176, 199) }, 0, 1);
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false, Dock = DockStyle.Fill, AccessibleName = "Language" };
        language.Items.Add("English"); language.SelectedIndex = 0;
        root.Controls.Add(language, 0, 2);
        var autoStart = new CheckBox { Text = "Launch automatically when Windows starts", AutoSize = true, Anchor = AnchorStyles.Left, AccessibleName = "Launch at Windows startup" };
        try { autoStart.Checked = startup.Enabled; }
        catch (Exception e)
        {
            autoStart.Enabled = false;
            if (showErrors) MessageBox.Show(this, e.Message, "Could not read Windows startup setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        bool updating = false;
        autoStart.CheckedChanged += (_, _) =>
        {
            if (updating) return;
            try { startup.SetEnabled(autoStart.Checked); }
            catch (Exception e)
            {
                updating = true; autoStart.Checked = !autoStart.Checked; updating = false;
                if (showErrors) MessageBox.Show(this, e.Message, "Could not save Windows startup setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        root.Controls.Add(autoStart, 0, 3);
        root.Controls.Add(new FutureOptionCheckBox { Text = "Remove streaming bitrate limits · Coming later", Enabled = false, AutoSize = true, Anchor = AnchorStyles.Left, AccessibleName = "Remove streaming bitrate limits" }, 0, 4);
        var close = new Button { Text = "Done", DialogResult = DialogResult.OK, Size = new Size(100, 34), Anchor = AnchorStyles.Right, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(108, 218, 188), ForeColor = BackColor };
        root.Controls.Add(close, 0, 5);
        AcceptButton = CancelButton = close;
    }
}

internal sealed class FutureOptionCheckBox : CheckBox
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        ControlPaint.DrawCheckBox(e.Graphics, new Rectangle(0, (Height - 14) / 2, 14, 14), ButtonState.Inactive);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(22, 0, Width - 22, Height),
            Color.FromArgb(159, 176, 199), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
