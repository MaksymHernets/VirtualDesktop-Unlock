using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamVRLibrary;

internal static class LocalizationTests
{
    public static void Run(string root, Action<bool,string> check)
    {
        var path=Path.Combine(root,"Languages","Language.json");
        var english=Localizer.Catalog("en");
        foreach(var language in Localizer.Languages)
        {
            var catalog=Localizer.Catalog(language.Code);
            check(catalog.Keys.ToHashSet().SetEquals(english.Keys) && catalog.Values.All(x=>!string.IsNullOrWhiteSpace(x)),"Complete language catalog: "+language.Code);
            foreach(var pair in english)
            {
                string Slots(string value)=>string.Join(",",Regex.Matches(value,@"\{\d+\}").Select(x=>x.Value).Order());
                if(Slots(pair.Value)!=Slots(catalog[pair.Key]))throw new Exception("Placeholder mismatch: "+language.Code+" "+pair.Key);
            }
            check(Localizer.Detect(CultureInfo.GetCultureInfo(language.Code))==language.Code,"Windows language detection: "+language.Code);
        }
        check(Localizer.Detect(CultureInfo.GetCultureInfo("zh-TW"))=="zh" && Localizer.Detect(CultureInfo.GetCultureInfo("fr-CA"))=="fr","Regional Windows language variants map to supported catalogs");
        check(Localizer.Detect(CultureInfo.GetCultureInfo("uk-UA"))=="en","Unsupported Windows language falls back to English");
        Localizer.Initialize(path,CultureInfo.GetCultureInfo("ru-RU"));
        check(Localizer.Code=="ru" && File.Exists(path),"First launch detects and persists Windows display language");
        Localizer.Select("ja");
        Localizer.Initialize(path,CultureInfo.GetCultureInfo("de-DE"));
        check(Localizer.Code=="ja","Manual language choice survives restart and Windows language changes");
        File.WriteAllText(path,"{bad json");
        Localizer.Initialize(path,CultureInfo.GetCultureInfo("uk-UA"));
        check(Localizer.Code=="en","Malformed preference recovers without crashing");
        try
        {
            foreach(var language in Localizer.Languages)
            {
                Localizer.Select(language.Code);
                check(StreamerControl.MatchesProcessStart("2026-10-23T15:10:17.8526864+03:00 [1] Installed OpenXR overrides:",new DateTime(2026,10,23,12,10,17,DateTimeKind.Utc))
                    && !StreamerControl.MatchesProcessStart("2026-10-22T15:10:17.8526864+03:00 [1] Installed OpenXR overrides:",new DateTime(2026,10,23,12,10,17,DateTimeKind.Utc)),"Protocol log timestamps validate independently of UI culture: "+language.Code);
                using var form=new LibraryForm(true,Path.Combine(root,"LanguageUI",language.Code,"library.json"),new PreviewStreamer());
                form.LoadPreview(new ScanResult([new Game("952060","Resident Evil 3","example")],[],[]));
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
                IEnumerable<Control> Children(Control c)=>c.Controls.Cast<Control>().SelectMany(x=>new[]{x}.Concat(Children(x)));
                var controls=Children(form).ToArray();
                check(controls.OfType<Button>().Any(x=>x.Text==Localizer.T("Refresh")) && form.RightToLeftLayout==(language.Code=="ar"),"Main UI text and direction: "+language.Code);
                var clipped=controls.OfType<Button>().Where(x=>TextRenderer.MeasureText(x.Text,x.Font,Size.Empty,TextFormatFlags.NoPadding).Width>x.ClientSize.Width).Select(x=>x.Text).ToArray();
                check(clipped.Length==0,"Localized main buttons fit their captions: "+language.Code+" "+string.Join(",",clipped));
                void Render(Form target,string name)
                {
                    using var image=new Bitmap(target.Width,target.Height);target.DrawToBitmap(image,new Rectangle(Point.Empty,target.Size));image.Save(Path.Combine(root,"language-"+language.Code+"-"+name+".png"));
                }
                Render(form,"main");
                form.Size=form.MinimumSize;Application.DoEvents();
                check(controls.OfType<Button>().All(x=>x.Parent!.ClientRectangle.Contains(x.Bounds)),"Main buttons stay in bounds at minimum size: "+language.Code);
                Render(form,"minimum");
                using var settings=new SettingsForm(new PreviewStartup(),false,new TraySettings(Path.Combine(root,"LanguageUI",language.Code,"Tray.json")));
                settings.ShowInTaskbar=false;settings.StartPosition=FormStartPosition.Manual;settings.Location=new Point(-20000,-20000);settings.Show();Application.DoEvents();Render(settings,"settings");
                using var profiles=new BitrateProfilesForm(new BitratePreferences(Path.Combine(root,"LanguageUI",language.Code,"Bitrate.xml")));
                profiles.ShowInTaskbar=false;profiles.StartPosition=FormStartPosition.Manual;profiles.Location=new Point(-20000,-20000);profiles.Show();Application.DoEvents();Render(profiles,"profiles");
                if(language.Code=="ar")
                {
                    Localizer.Select("fr");Application.DoEvents();
                    check(!form.RightToLeftLayout && controls.OfType<Button>().Any(x=>x.Text=="Actualiser"),"Live language switching updates an open main window and restores left-to-right layout");
                }
            }
        }
        finally { Localizer.Select("en",false); }
        using var live=new LibraryForm(true,Path.Combine(root,"PickerUI","library.json"),new PreviewStreamer());
        using var picker=new SettingsForm(new PreviewStartup(),false,new TraySettings(Path.Combine(root,"PickerUI","Tray.json")));
        IEnumerable<Control> All(Control c)=>c.Controls.Cast<Control>().SelectMany(x=>new[]{x}.Concat(All(x)));
        var choice=All(picker).OfType<ComboBox>().Single();choice.SelectedValue="ru";
        check(Localizer.Code=="ru" && picker.Text.StartsWith("Настройки") && All(live).OfType<Button>().Any(x=>x.Text=="Обновить"),"Settings language picker saves and immediately translates both open windows");
        Localizer.Initialize(path,CultureInfo.GetCultureInfo("de-DE"));
        check(Localizer.Code=="ru","Language chosen in Settings persists for the next launch");
        Localizer.Select("en",false);
    }
    private sealed class PreviewStreamer:IStreamerControl { public bool IsRunning=>true;public bool IsApplied(IReadOnlyCollection<uint> ids)=>true;public Task RestartAsync()=>Task.CompletedTask; }
    private sealed class PreviewStartup:IStartupRegistration { public bool Enabled=>false;public void SetEnabled(bool enabled){} }
}
