using VirtualDesktop.Unlock;

namespace SteamVRLibrary;

public sealed class BitrateProfilesForm : Form
{
    public BitrateProfilesForm(BitratePreferences preferences)
    {
        Text = "Bitrate profiles — Virtual Desktop Unlock";
        BackColor = Color.FromArgb(14, 20, 31); ForeColor = Color.White; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(570, 400); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        var config = preferences.Configuration.Copy();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 8 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        foreach (int height in new[] { 36, 42, 42, 45, 45, 45, 50, 44 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(root);
        var title = new Label { Text = "Limits by headset and codec", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold) };
        root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        var headsets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Headset bitrate profile", DisplayMember = "Value", ValueMember = "Key", DataSource = BitrateConfiguration.Headsets.ToArray() };
        root.Controls.Add(headsets, 0, 1); root.SetColumnSpan(headsets, 2);
        var custom = new CheckBox { Text = "Use custom limits for this headset", AutoSize = true, Anchor = AnchorStyles.Left, AccessibleName = "Custom headset limits" };
        root.Controls.Add(custom, 0, 2); root.SetColumnSpan(custom, 2);
        NumericUpDown Limit(string name, int row)
        {
            root.Controls.Add(new Label { Text = name + " (Mbps)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var value = new NumericUpDown { Minimum = 10, Maximum = 2000, Increment = 25, ThousandsSeparator = true, Dock = DockStyle.Fill, AccessibleName = name };
            root.Controls.Add(value, 1, row); return value;
        }
        var h264 = Limit("H.264 / H.264+", 3); var hevc = Limit("H.265 / HEVC 10-bit", 4); var av1 = Limit("AV1 / AV1 10-bit", 5);
        var note = new Label { Text = "Unconfigured headsets keep stock limits. The bitrate slider and automatic network limiting still apply.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(159, 176, 199) };
        root.Controls.Add(note, 0, 6); root.SetColumnSpan(note, 2);
        int previous = -1; bool loading = false;
        void Commit()
        {
            if (previous < 0) return;
            if (custom.Checked) config.Profiles[previous] = new BitrateProfile((int)h264.Value, (int)hevc.Value, (int)av1.Value);
            else config.Profiles.Remove(previous);
        }
        void LoadHeadset()
        {
            if (loading || headsets.SelectedItem is not KeyValuePair<int, string> selected) return;
            Commit(); previous = selected.Key; loading = true;
            var exists = config.Profiles.TryGetValue(previous, out var p); p ??= new BitrateProfile(1000, 300, 250);
            custom.Checked = exists; h264.Value = p.H264Mbps; hevc.Value = p.HevcMbps; av1.Value = p.Av1Mbps;
            h264.Enabled = hevc.Enabled = av1.Enabled = exists; loading = false;
        }
        custom.CheckedChanged += (_, _) => { if (!loading) h264.Enabled = hevc.Enabled = av1.Enabled = custom.Checked; };
        headsets.SelectedIndexChanged += (_, _) => LoadHeadset();
        headsets.BindingContext = new BindingContext();
        headsets.SelectedValue = 1000; LoadHeadset();
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill };
        var save = new Button { Text = "Save profiles", Dock = DockStyle.Fill, BackColor = Color.FromArgb(108, 218, 188), ForeColor = BackColor, FlatStyle = FlatStyle.Flat, AccessibleName = "Save bitrate profiles" };
        save.Click += (_, _) =>
        {
            try { Commit(); preferences.Save(config); DialogResult = DialogResult.OK; Close(); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Could not save bitrate profiles", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        root.Controls.Add(cancel, 0, 7); root.Controls.Add(save, 1, 7);
        AcceptButton = save; CancelButton = cancel;
    }
}
