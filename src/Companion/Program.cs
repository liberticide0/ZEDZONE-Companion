namespace Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(false, @"Local\ZEDZONE.Companion");
        if (args is ["--apply-update", var path, var pid] && int.TryParse(pid, out int parent))
        {
            UpdateWorker.ApplyAndRestart(path, parent, instance);
            return;
        }

        bool finishing = args is ["--finish-update", _, "ok" or "failed"] &&
            Guid.TryParseExact(args[1], "N", out _);
        if (!Acquire(instance, finishing ? TimeSpan.FromSeconds(30) : TimeSpan.Zero))
        {
            MessageBox.Show("이미 실행 중입니다. 알림 영역의 Companion 아이콘을 확인하세요.",
                "ZED ZONE Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string? warning = finishing && args[2] == "failed"
                ? "업데이트를 완료하지 못했습니다. 기존 버전으로 다시 열었습니다." : null;
            try { new UpdatePackage(UpdateWorker.InstallPath).Recover(); }
            catch (Exception error) when (UpdateWorker.IsUpdateError(error))
            {
                warning = "업데이트 파일을 정리하지 못했습니다. 앱은 열리지만, 제작 자료를 준비하기 전에 ZIP을 새 폴더에 풀어 실행하세요.";
            }
            if (finishing)
                UpdateWorker.CleanupLater(Guid.ParseExact(args[1], "N"));
            using var overlay = new Overlay(warning);
            Application.Run(overlay);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    internal static bool Acquire(Mutex mutex, TimeSpan timeout)
    {
        try { return mutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { return true; }
    }
}
