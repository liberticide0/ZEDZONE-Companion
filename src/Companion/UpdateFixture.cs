namespace Companion;

internal static class UpdateFixture
{
#if UPDATE_FIXTURE
    internal const string TitleSuffix = " · 업데이트 검증";
    internal const string MutexSuffix = ".UpdateFixture";
    internal static string DataParent => Path.Combine(UpdateWorker.InstallPath, ".qa-local");
    internal static HttpMessageHandler CreateHandler() => new FixtureHandler();

    private sealed class FixtureHandler : DelegatingHandler
    {
        internal FixtureHandler() : base(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            string endpoint = request.RequestUri?.Host == "api.github.com" ? "latest" : "asset";
            request.RequestUri = new Uri($"http://127.0.0.1:48161/{endpoint}");
            return base.SendAsync(request, cancellation);
        }
    }
#else
    internal const string TitleSuffix = "";
    internal const string MutexSuffix = "";
    internal static string DataParent => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    internal static HttpMessageHandler CreateHandler() => new HttpClientHandler { AllowAutoRedirect = false };
#endif

    internal static string DataPath => Path.Combine(DataParent, "ZEDZONE.Companion");
}
