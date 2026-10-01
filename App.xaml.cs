using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RagdollPet;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "运行状态.log"), $"{DateTime.Now:O} 应用启动\r\n");
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "运行状态.log"), $"{DateTime.Now:O} 已调用窗口 Show，Visible={window.IsVisible}\r\n");
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "启动错误.log"), e.Exception.ToString());
        }
        catch { }
        System.Windows.MessageBox.Show(e.Exception.Message, "团团桌宠启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }
}
