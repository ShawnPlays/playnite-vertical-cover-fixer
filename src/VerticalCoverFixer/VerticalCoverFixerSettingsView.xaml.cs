using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace VerticalCoverFixer
{
    public partial class VerticalCoverFixerSettingsView : UserControl
    {
        public VerticalCoverFixerSettingsView()
        {
            InitializeComponent();
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}
