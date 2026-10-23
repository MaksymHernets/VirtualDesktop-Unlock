using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SteamVRLibrary;

public static class Localizer
{
    public sealed record Language(string Code, string Name);
    public static readonly Language[] Languages = [new("en", "English"), new("ru", "Русский"), new("fr", "Français"), new("de", "Deutsch"), new("es", "Español"), new("zh", "中文（简体）"), new("ja", "日本語"), new("ko", "한국어"), new("hi", "हिन्दी"), new("ar", "العربية")];
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = Languages.ToDictionary(x => x.Code, x => Load(x.Code));
    private static readonly ConditionalWeakTable<Control, Binding> Bindings = new();
    private static readonly ConditionalWeakTable<ToolStripItem, MenuBinding> MenuBindings = new();
    private static readonly ConditionalWeakTable<ColumnHeader, MenuBinding> ColumnBindings = new();
    private static string? preferencesPath;
    public static string Code { get; private set; } = "en";
    public static bool IsRtl => Code == "ar";
    public static event EventHandler? Changed;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual Desktop Unlock", "Language.json");
    public static string Detect(CultureInfo culture) => Languages.Any(x => x.Code == culture.TwoLetterISOLanguageName) ? culture.TwoLetterISOLanguageName : "en";
    public static void Initialize(string? path = null, CultureInfo? windowsLanguage = null)
    {
        preferencesPath = path ?? DefaultPath;
        string selected = Detect(windowsLanguage ?? CultureInfo.CurrentUICulture);
        bool needsSave=true;
        try
        {
            if (File.Exists(preferencesPath))
            {
                var saved = JsonSerializer.Deserialize<Preference>(File.ReadAllText(preferencesPath));
                if (Languages.Any(x => x.Code == saved?.Language)) { selected = saved!.Language;needsSave=false; }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        try { if(needsSave)Save(selected); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        SetCulture(selected);
    }
    public static void Select(string code, bool persist = true)
    {
        if (!Catalogs.ContainsKey(code)) throw new ArgumentException("Unsupported application language.", nameof(code));
        if (persist) Save(code);
        SetCulture(code);
        Changed?.Invoke(null, EventArgs.Empty);
    }
    private static void SetCulture(string code)
    {
        Code = code;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(code);
    }
    private static void Save(string code)
    {
        if (preferencesPath == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        string temp = preferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Preference { Language = code }));
            if (File.Exists(preferencesPath)) File.Replace(temp, preferencesPath, preferencesPath + ".bak");
            else File.Move(temp, preferencesPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private sealed class Preference { public string Language { get; set; } = ""; }
    private static Dictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Localization." + code + ".json") ?? throw new InvalidDataException("Missing language catalog: " + code);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static IReadOnlyDictionary<string,string> Catalog(string code) => Catalogs[code];
    public static string T(string text, params object[] args)
    {
        string translated = Catalogs[Code].GetValueOrDefault(text, text);
        return args.Length == 0 ? translated : string.Format(CultureInfo.CurrentCulture, translated, args);
    }
    public static void SetText(Control control, string key, params object[] args)
    {
        var binding = Bindings.GetValue(control, c => new Binding(c));
        binding.Text = key; binding.Args = args;
        control.Text = T(key, args);
    }
    public static void SetToolTip(ToolTip tip, Control control, string key)
    {
        var binding = Bindings.GetValue(control, c => new Binding(c));
        binding.Tip = tip; binding.TipText = key; tip.SetToolTip(control, T(key));
    }
    public static void Apply(Control control)
    {
        var b = Bindings.GetValue(control, c => new Binding(c));
        if (control is not TextBoxBase and not ComboBox and not NumericUpDown) control.Text = T(b.Text, b.Args);
        if (control is TextBox box) box.PlaceholderText = T(b.Placeholder);
        if (b.Name != null) control.AccessibleName = T(b.Name);
        if (b.Tip != null) b.Tip.SetToolTip(control, T(b.TipText));
        if (control is Form form) { form.RightToLeft = IsRtl ? RightToLeft.Yes : RightToLeft.No; form.RightToLeftLayout = IsRtl; }
        // WinForms mirrors alignment with the form; flipping it again would left-align Arabic.
        if (control is Label label) label.TextAlign = b.Alignment;
        if (control is ListView list)
        {
            list.RightToLeft = RightToLeft.No; list.RightToLeftLayout = false;
            foreach(ColumnHeader column in list.Columns)column.Text=T(ColumnBindings.GetValue(column,c=>new MenuBinding(c.Text)).Text);
        }
        if (control is NumericUpDown) control.RightToLeft = RightToLeft.No;
        foreach (Control child in control.Controls) Apply(child);
    }
    public static void ApplyMenu(ToolStrip strip)
    {
        foreach (ToolStripItem item in strip.Items)
        {
            var binding = MenuBindings.GetValue(item, i => new MenuBinding(i.Text ?? ""));
            item.Text = T(binding.Text);
        }
        strip.RightToLeft = IsRtl ? RightToLeft.Yes : RightToLeft.No;
    }
    private sealed record MenuBinding(string Text);
    private sealed class Binding(Control c)
    {
        public string Text = c.Text;
        public object[] Args = [];
        public string? Name = c.AccessibleName;
        public string Placeholder = (c as TextBox)?.PlaceholderText ?? "";
        public ContentAlignment Alignment = (c as Label)?.TextAlign ?? ContentAlignment.MiddleLeft;
        public ToolTip? Tip;
        public string TipText = "";
    }
}

internal static class LocalizedMessageBox
{
    public static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon)
        => MessageBox.Show(owner,Localizer.T(text),Localizer.T(caption),buttons,icon,MessageBoxDefaultButton.Button1,Localizer.IsRtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
    public static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon)
        => MessageBox.Show(Localizer.T(text),Localizer.T(caption),buttons,icon,MessageBoxDefaultButton.Button1,Localizer.IsRtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
}
