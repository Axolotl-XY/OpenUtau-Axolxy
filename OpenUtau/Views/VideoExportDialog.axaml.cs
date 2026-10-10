using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App;

namespace OpenUtau.App.Views {
    public partial class VideoExportDialog : Window {
        public int Fps { get; private set; } = 30;
        public bool ExportAudio { get; private set; } = true;
        public string OutputPath { get; private set; } = "";

        public VideoExportDialog() {
            InitializeComponent();
        }

        public VideoExportDialog(string defaultPath) : this() {
            PathBox.Text = defaultPath;
        }

        async void OnBrowse(object sender, RoutedEventArgs args) {
            var file = await FilePicker.SaveFileAboutProject(
                this, "导出视频",
                new Avalonia.Platform.Storage.FilePickerFileType("MP4 视频") {
                    Patterns = new[] { "*.mp4" }
                });
            if (!string.IsNullOrEmpty(file)) {
                PathBox.Text = file;
            }
        }

        void OnOk(object sender, RoutedEventArgs args) {
            if (string.IsNullOrWhiteSpace(PathBox.Text)) {
                return;
            }
            Fps = FpsCombo.SelectedIndex switch { 0 => 24, 2 => 60, _ => 30 };
            ExportAudio = AudioCheck.IsChecked == true;
            OutputPath = PathBox.Text ?? "";
            Close(true);
        }

        void OnCancel(object sender, RoutedEventArgs args) {
            Close(false);
        }
    }
}