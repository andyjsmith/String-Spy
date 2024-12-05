namespace StringsApp.ViewModels;

public record StringResult(string Content, long Position);

public partial class MainViewModel : ViewModelBase {
    public StringsViewModel StringsDataContext { get; set; } = new();
    public SettingsViewModel SettingsDataContext { get; set; } = new();



    public MainViewModel() {

    }
}
