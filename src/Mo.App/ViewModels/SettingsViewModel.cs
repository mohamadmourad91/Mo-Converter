using System.Windows.Input;
using Mo.Engine;

namespace Mo.App.ViewModels;

/// <summary>What the settings window asks the outside world to do (folder picker, restart prompt, closing).</summary>
public interface ISettingsDialogs
{
    string? PickFolder(string? current);
    bool AskRestart();
    void Close();
}

/// <summary>
/// Editable copy of <see cref="AppSettings"/>. Nothing is written until Save.
/// The three wheel keys always stay distinct: choosing a key that another wheel uses swaps the two.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ISettingsDialogs _dialogs;
    private readonly Action<AppSettings, bool> _saved;
    private readonly Action<bool> _setStartWithWindows;
    private readonly bool _startupInitially;

    private ModifierKey _convertKey, _toolsKey, _actionsKey;
    private bool _useFolder;
    private string _outputFolder;
    private bool _playSounds;
    private ThemeChoice _theme;
    private string _language;
    private bool _startWithWindows;
    private string? _error;

    /// <param name="saved">Called after saving with the settings and whether a restart was accepted.</param>
    public SettingsViewModel(AppSettings settings, ISettingsDialogs dialogs, bool startWithWindows, Action<bool> setStartWithWindows, Action<AppSettings, bool> saved)
    {
        _settings = settings;
        _dialogs = dialogs;
        _saved = saved;
        _setStartWithWindows = setStartWithWindows;
        _convertKey = settings.ConvertKey;
        _toolsKey = settings.ToolsKey;
        _actionsKey = settings.ActionsKey;
        _useFolder = !string.IsNullOrWhiteSpace(settings.OutputFolder);
        _outputFolder = settings.OutputFolder ?? "";
        _playSounds = settings.PlaySounds;
        _theme = settings.Theme;
        _language = settings.Language ?? SystemLanguage;
        _startWithWindows = _startupInitially = startWithWindows;
        if (!HasDistinctKeys) (_convertKey, _toolsKey, _actionsKey) = (ModifierKey.Shift, ModifierKey.Ctrl, ModifierKey.Alt);

        var t = Localizer.Current;
        Keys =
        [
            new(ModifierKey.Shift, t.T("key.shift")),
            new(ModifierKey.Ctrl, t.T("key.ctrl")),
            new(ModifierKey.Alt, t.T("key.alt")),
        ];
        Themes =
        [
            new(ThemeChoice.System, t.T("settings.theme.system")),
            new(ThemeChoice.Light, t.T("settings.theme.light")),
            new(ThemeChoice.Dark, t.T("settings.theme.dark")),
        ];
        // Language names are written in their own language so anyone can find theirs.
        Languages =
        [
            new(SystemLanguage, t.T("settings.language.system")),
            new("en", "English"),
            new("de", "Deutsch"),
            new("ar", "\u0627\u0644\u0639\u0631\u0628\u064A\u0629"),
        ];

        BrowseCommand = new RelayCommand(Browse);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(dialogs.Close);
    }

    /// <summary>Marker for "follow Windows" in the language list (stored as null).</summary>
    public const string SystemLanguage = "system";

    public IReadOnlyList<Choice<ModifierKey>> Keys { get; }
    public IReadOnlyList<Choice<ThemeChoice>> Themes { get; }
    public IReadOnlyList<Choice<string>> Languages { get; }

    public ModifierKey ConvertKey { get => _convertKey; set => SetKey(ref _convertKey, value, ref _toolsKey, ref _actionsKey); }
    public ModifierKey ToolsKey { get => _toolsKey; set => SetKey(ref _toolsKey, value, ref _convertKey, ref _actionsKey); }
    public ModifierKey ActionsKey { get => _actionsKey; set => SetKey(ref _actionsKey, value, ref _convertKey, ref _toolsKey); }

    public bool HasDistinctKeys => _convertKey != _toolsKey && _convertKey != _actionsKey && _toolsKey != _actionsKey;

    public bool UseNextToOriginal
    {
        get => !_useFolder;
        set => UseFolder = !value;
    }

    public bool UseFolder
    {
        get => _useFolder;
        set
        {
            if (!Set(ref _useFolder, value)) return;
            OnPropertyChanged(nameof(UseNextToOriginal));
            Error = null;
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set { if (Set(ref _outputFolder, value)) Error = null; }
    }

    public bool PlaySounds { get => _playSounds; set => Set(ref _playSounds, value); }
    public ThemeChoice Theme { get => _theme; set => Set(ref _theme, value); }

    public string Language
    {
        get => _language;
        set { if (Set(ref _language, value)) OnPropertyChanged(nameof(LanguageChanged)); }
    }

    /// <summary>True when the chosen language differs from the saved one (shows the restart hint).</summary>
    public bool LanguageChanged => (_language == SystemLanguage ? null : _language) != _settings.Language;

    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }

    /// <summary>Validation message shown above the buttons, or null.</summary>
    public string? Error
    {
        get => _error;
        private set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => _error is not null;


    public ICommand BrowseCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private void SetKey(ref ModifierKey field, ModifierKey value, ref ModifierKey other1, ref ModifierKey other2)
    {
        if (field == value) return;
        // Give the wheel that used this key the key we are giving up, so all three stay different.
        if (other1 == value) other1 = field;
        else if (other2 == value) other2 = field;
        field = value;
        OnPropertyChanged(nameof(ConvertKey));
        OnPropertyChanged(nameof(ToolsKey));
        OnPropertyChanged(nameof(ActionsKey));
    }

    private void Browse()
    {
        var picked = _dialogs.PickFolder(string.IsNullOrWhiteSpace(_outputFolder) ? null : _outputFolder);
        if (picked is null) return;
        OutputFolder = picked;
        UseFolder = true;
    }

    private void Save()
    {
        if (_useFolder && (string.IsNullOrWhiteSpace(_outputFolder) || !Path.IsPathFullyQualified(_outputFolder.Trim())))
        {
            Error = Localizer.Current.T("settings.output.folder-missing");
            return;
        }

        var languageChanged = LanguageChanged;
        _settings.ConvertKey = _convertKey;
        _settings.ToolsKey = _toolsKey;
        _settings.ActionsKey = _actionsKey;
        _settings.OutputFolder = _useFolder ? _outputFolder.Trim() : null;
        _settings.PlaySounds = _playSounds;
        _settings.Theme = _theme;
        _settings.Language = _language == SystemLanguage ? null : _language;

        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = Localizer.Current.T("settings.save-failed", ex.Message);
            return;
        }

        // The Run value is only touched when the user flipped the switch, so an existing entry is never rewritten by accident.
        if (_startWithWindows != _startupInitially)
        {
            try { _setStartWithWindows(_startWithWindows); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Error = Localizer.Current.T("settings.save-failed", ex.Message);
                return;
            }
        }

        var restart = languageChanged && _dialogs.AskRestart();
        _saved(_settings, restart);
        _dialogs.Close();
    }
}
