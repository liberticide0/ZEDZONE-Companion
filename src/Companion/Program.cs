namespace Companion;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true, @"Local\ZEDZONE.Companion", out var first);
        if (!first)
        {
            MessageBox.Show("이미 실행 중입니다. 알림 영역의 Companion 아이콘을 확인하세요.",
                "ZED ZONE Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var overlay = new Overlay();
        Application.Run(overlay);
    }
}
