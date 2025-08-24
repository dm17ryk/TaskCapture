using System.Windows;

namespace TaskCapture
{
    public partial class ResultWindow : Window
    {
        private readonly string _html;
        public ResultWindow(string html)
        {
            InitializeComponent();
            _html = html;
            Loaded += async (_, __) =>
            {
                await Web.EnsureCoreWebView2Async(null);
                Web.NavigateToString(_html);
            };
        }
    }
}
